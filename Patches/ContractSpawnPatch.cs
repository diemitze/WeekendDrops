using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using WeekendDrops.Models;
using WeekendDrops.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils.Json;

namespace WeekendDrops.Patches;

public static class ContractSpawnPatch
{
    private static ContractService? _contracts;
    private static ISptLogger<WeekendDropsLoader>? _logger;
    private static bool _applied;

    private static readonly bool _fikaPresent = WdPaths.SiblingModExists("fika-server");

    public static void Apply(ContractService contracts, ISptLogger<WeekendDropsLoader> logger)
    {
        _contracts = contracts;
        _logger = logger;
        if (_applied) return;

        var target = AccessTools.Method(
            typeof(LocationLifecycleService),
            nameof(LocationLifecycleService.GenerateLocationAndLoot));
        if (target is null)
        {
            logger.Error("[WeekendDrops] Could not find LocationLifecycleService.GenerateLocationAndLoot - contract spawns disabled");
            return;
        }

        var postfix = typeof(ContractSpawnPatch)
            .GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic);

        new Harmony("com.20fpsguy.WeekendDrops.ContractSpawn")
            .Patch(target, postfix: new HarmonyMethod(postfix));

        _applied = true;
    }

    private static void Postfix(MongoId __0, string __1, LocationBase __result)
    {
        if (_contracts is null || __result is null) return;

        List<ContractDefinition> defs;
        if (_fikaPresent)
        {
            defs = _contracts.GetAllActiveContractsForMap(__1);
            if (defs.Count > 1)
                _logger?.Info($"[WeekendDrops] Fika: {defs.Count} active contracts on {__1} - injecting all of them.");
        }
        else
        {
            var own = _contracts.GetActiveContractForMap(__0, __1);
            defs = own is null ? [] : [own];
        }

        defs = defs.Where(d => d.Groups is { Count: > 0 }).ToList();
        if (defs.Count == 0) return;

        __result.BossLocationSpawn ??= [];

        var native = __result.BossLocationSpawn.ToList();

        if (_contracts.SuppressNativeBoss && defs.Any(d => d.BossPool is { Count: > 0 }))
            foreach (var existing in native)
                if (!string.IsNullOrEmpty(existing.BossName) &&
                    existing.BossName.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
                    existing.BossChance = 0;

        int injected = 0;
        var zonesPlaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in defs)
        {
            foreach (var g in def.Groups)
            {
                if (string.IsNullOrEmpty(g.BossName)) continue;

                foreach (var existing in native)
                    if (string.Equals(existing.BossName, g.BossName, StringComparison.OrdinalIgnoreCase))
                        existing.BossChance = 0;

                var spawn = new BossLocationSpawn
                {
                    BossName        = g.BossName,
                    BossChance      = 100,
                    ForceSpawn      = true,
                    IgnoreMaxBots   = true,
                    BossDifficulty  = g.BossDifficulty,
                    BossZone        = g.BossZone,
                    Time            = -1,
                    ShowOnTarkovMap = false,
                };

                if (!string.IsNullOrEmpty(g.EscortType) && g.EscortAmount != "0")
                {
                    spawn.BossEscortType       = g.EscortType;
                    spawn.BossEscortDifficulty = g.EscortDifficulty;

                    // Init() sums this field AND the Supports amounts into EscortCount, but only
                    // the Supports entries ever spawn. Leaving it set asks for a group twice the
                    // size of the crew that turns up (the same quirk makes the goons' exUsec 2 a
                    // no-show in vanilla).
                    spawn.BossEscortAmount     = "0";

                    // The client builds the escort list from Supports; with it empty
                    // BossLocationSpawn.GetEscors() hands back null and only the leader spawns.
                    spawn.Supports =
                    [
                        new BossSupport
                        {
                            BossEscortType       = g.EscortType,
                            BossEscortAmount     = g.EscortAmount,
                            BossEscortDifficulty = new ListOrT<string>(
                                [string.IsNullOrEmpty(g.EscortDifficulty) ? "normal" : g.EscortDifficulty], null),
                        },
                    ];
                }

                __result.BossLocationSpawn.Add(spawn);
                injected++;

                if (g.HostileToPlayer)
                    ApplyPlayerHostility(__result, g.BossName, g.EscortType);

                if (_contracts.CrewSpawnOnPosts && def.ResolvedPosts.Count > 0 &&
                    !string.IsNullOrEmpty(g.BossZone) && zonesPlaced.Add(g.BossZone))
                {
                    // Posts authored for another zone would drop the crew outside the one they
                    // belong to, so leave the zone's own spawn points alone instead.
                    if (!string.IsNullOrEmpty(def.ResolvedPostsZone) &&
                        !string.Equals(def.ResolvedPostsZone, g.BossZone, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger?.Warning(
                            $"[WeekendDrops] Contract '{def.Id}' - posts belong to '{def.ResolvedPostsZone}' but the " +
                            $"crew spawns in '{g.BossZone}'; keeping the zone's own spawn points");
                    }
                    else
                    {
                        InjectCrewSpawnPoints(__result, g.BossZone, def.ResolvedPosts, def.Id,
                                              1 + ParseEscortAmount(g.EscortAmount), def.TriggerAirdrop);
                    }
                }
            }
        }

        var supplyRuns = defs.Where(d => d.TriggerAirdrop).ToList();
        if (supplyRuns.Count > 0)
            ForceAirdrop(__result, supplyRuns.Count, string.Join(", ", supplyRuns.Select(d => d.Id)));

        if (injected > 0)
            _logger?.Info(
                $"[WeekendDrops] Injected {injected} group(s) from {defs.Count} contract(s) " +
                $"({string.Join(", ", defs.Select(d => d.Id))}) into {__1} for {__0}");
    }

    private static void ForceAirdrop(LocationBase loc, int count, string contractIds)
    {
        loc.AirdropParameters ??= [];
        if (loc.AirdropParameters.Count == 0)
            loc.AirdropParameters.Add(new AirdropParameter());

        foreach (var ap in loc.AirdropParameters)
        {
            ap.PlaneAirdropChance = 1.0;
            ap.MinimumPlayersCountToSpawnAirdrop = 1;
            ap.PlaneAirdropMax = count;
            ap.PlaneAirdropStartMin = 999999;
            ap.PlaneAirdropStartMax = 999999;
        }

        _logger?.Info($"[WeekendDrops] Contract(s) '{contractIds}' - {count} airdrop(s) armed for on-wipe summon (no auto-timer)");
    }

    /// BSG writes escort counts as "3" or as a "1,2,3" pick-one list; the crew can be as
    /// large as the biggest number in it.
    private static int ParseEscortAmount(string? amount)
    {
        if (string.IsNullOrWhiteSpace(amount)) return 0;

        int max = 0;
        foreach (var part in amount.Split(','))
            if (int.TryParse(part.Trim(), out var n) && n > max) max = n;
        return max;
    }

    private static void InjectCrewSpawnPoints(LocationBase loc, string zone, List<Vec3> posts,
                                              string contractId, int crewSize, bool postsAreGenerated)
    {
        var all = loc.SpawnPointParams?.ToList();
        if (all is null || all.Count == 0) return;

        static bool IsBotPoint(SpawnPointParam p) =>
            p.Categories?.Any(c => string.Equals(c, "Bot", StringComparison.OrdinalIgnoreCase)) == true;

        bool InZone(SpawnPointParam p) =>
            IsBotPoint(p) && string.Equals(p.BotZoneName, zone, StringComparison.OrdinalIgnoreCase);

        var zonePoints = all.Where(InZone).ToList();

        var template = zonePoints.FirstOrDefault() ?? all.FirstOrDefault(IsBotPoint);
        if (template is null) return;

        // A generated ring is pure maths: it carries the crate's Y all the way round and never
        // touches the navmesh. A bot handed a position more than 0.6m off the mesh never leaves
        // PreActive, and its only fallback is another point from the same zone, so on sloped
        // ground most of the crew is stuck invisible and only the one lucky member turns up.
        // Real spawn points are always walkable, so post the crew on the ones nearest the crate.
        if (postsAreGenerated && zonePoints.Count > 0)
        {
            var centre = Centroid(posts);
            var keep = zonePoints
                .OrderBy(p => SqrDistanceTo(p, centre))
                .Take(Math.Max(1, crewSize))
                .ToList();

            all.RemoveAll(p => InZone(p) && !keep.Any(k => ReferenceEquals(k, p)));
            loc.SpawnPointParams = all;

            float nearest = (float)Math.Sqrt(SqrDistanceTo(keep[0], centre));
            _logger?.Info(
                $"[WeekendDrops] Contract '{contractId}' - crew posted on the {keep.Count} real spawn point(s) " +
                $"nearest the drop in '{zone}' (closest {nearest:F0}m out)");
            return;
        }

        all.RemoveAll(InZone);

        // The zone's own spawn points have just been removed, so these are the only ones the
        // crew has left. Fewer points than bodies and most of the crew never spawns, which is
        // why a hand-placed hideout with one post used to yield a lone bot.
        var spots = BuildCrewSpots(posts, Math.Max(crewSize, posts.Count));

        for (int i = 0; i < spots.Count; i++)
        {
            all.Add(template with
            {
                Id          = $"WD_{contractId}_{zone}_{i}",
                BotZoneName = zone,
                Position    = new Vector3 { X = spots[i].X, Y = spots[i].Y, Z = spots[i].Z },
            });
        }

        loc.SpawnPointParams = all;

        if (spots.Count > posts.Count)
            _logger?.Info(
                $"[WeekendDrops] Contract '{contractId}' - zone '{zone}' has {posts.Count} post(s) for a crew of " +
                $"{crewSize}; padded out to {spots.Count} spawn point(s) around them");
        else
            _logger?.Info($"[WeekendDrops] Contract '{contractId}' - zone '{zone}' bot spawns replaced with {spots.Count} post(s)");
    }

    private static Vec3 Centroid(List<Vec3> posts)
    {
        float x = 0f, y = 0f, z = 0f;
        foreach (var p in posts) { x += p.X; y += p.Y; z += p.Z; }
        return new Vec3 { X = x / posts.Count, Y = y / posts.Count, Z = z / posts.Count };
    }

    private static float SqrDistanceTo(SpawnPointParam p, Vec3 target)
    {
        if (p.Position is not { } pos) return float.MaxValue;
        float dx = pos.X - target.X, dy = pos.Y - target.Y, dz = pos.Z - target.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// Rings extra spawn points around the authored posts until there is one per crew member.
    private static List<Vec3> BuildCrewSpots(List<Vec3> posts, int wanted)
    {
        var spots = new List<Vec3>(posts);

        for (int extra = 0; spots.Count < wanted; extra++)
        {
            var basePost = posts[extra % posts.Count];
            int ring     = 1 + extra / posts.Count;

            // Golden angle keeps successive points from stacking on each other.
            double angle  = extra * 2.39996;
            float  radius = 2.5f * ring;

            spots.Add(new Vec3
            {
                X = basePost.X + (float)(Math.Cos(angle) * radius),
                Y = basePost.Y,
                Z = basePost.Z + (float)(Math.Sin(angle) * radius),
            });
        }

        return spots;
    }

    private static void ApplyPlayerHostility(LocationBase loc, params string[] ownRoles)
    {
        loc.BotLocationModifier ??= new BotLocationModifier();
        var list = loc.BotLocationModifier.AdditionalHostilitySettings?.ToList()
                   ?? new List<AdditionalHostilitySettings>();

        var own = new HashSet<string>(
            ownRoles.Where(r => !string.IsNullOrEmpty(r)), StringComparer.OrdinalIgnoreCase);
        if (own.Count == 0) return;

        foreach (var role in own)
        {
            list.RemoveAll(h => string.Equals(h.BotRole, role, StringComparison.OrdinalIgnoreCase));
            list.Add(new AdditionalHostilitySettings
            {
                BotRole = role,
                AlwaysEnemies = new HashSet<string>(),
                AlwaysFriends = own,
                BearPlayerBehaviour = "AlwaysEnemies",
                UsecPlayerBehaviour = "AlwaysEnemies",
                SavagePlayerBehaviour = "AlwaysEnemies",
            });
            _logger?.Info($"[WeekendDrops] Player-only hostility applied to '{role}' (neutral to AI, hunts player)");
        }

        loc.BotLocationModifier.AdditionalHostilitySettings = list;
    }
}
