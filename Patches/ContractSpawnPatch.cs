using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using WeekendDrops.Models;
using WeekendDrops.Services;

namespace WeekendDrops.Patches;

// Injects a contract's spawn as the raid is generated. GenerateLocationAndLoot builds the
// LocationBase whose BossLocationSpawn list drives boss spawns. Per-raid, per-session.
public static class ContractSpawnPatch
{
    private static ContractService? _contracts;
    private static ISptLogger<WeekendDropsLoader>? _logger;
    private static bool _applied;

    // Fika detected once by its server mod. The "any profile's contract" fallback below is
    // gated on this, so solo SPT with several profiles never pulls in another profile's crew.
    private static readonly bool _fikaPresent = Directory.Exists(
        System.IO.Path.Combine(AppContext.BaseDirectory, "user", "mods", "fika-server"));

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

    // __0 = sessionId, __1 = location name, __result = the generated LocationBase.
    private static void Postfix(MongoId __0, string __1, LocationBase __result)
    {
        if (_contracts is null || __result is null) return;

        var def = _contracts.GetActiveContractForMap(__0, __1);

        // The session that GENERATES the raid is the headless/non-host client, which never
        // accepts contracts, so fall back to any profile's.
        if (def is null && _fikaPresent)
        {
            def = _contracts.GetAnyActiveContractForMap(__1);
            if (def != null)
                _logger?.Info($"[WeekendDrops] Fika fallback: raid session {__0} has no contract; spawning another profile's active contract '{def.Id}' for {__1}");
        }

        if (def is null || def.Groups is null || def.Groups.Count == 0) return;

        __result.BossLocationSpawn ??= [];
        int injected = 0;
        var zonesPlaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Off by default, which gives a "two warlords" raid. A same-named native is deduped
        // below either way.
        if (def.BossPool is { Count: > 0 } && _contracts.SuppressNativeBoss)
            foreach (var existing in __result.BossLocationSpawn)
                if (!string.IsNullOrEmpty(existing.BossName) &&
                    existing.BossName.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
                    existing.BossChance = 0;

        foreach (var g in def.Groups)
        {
            if (string.IsNullOrEmpty(g.BossName)) continue;

            // Disable a native copy of the same boss, or the player faces two of them.
            foreach (var existing in __result.BossLocationSpawn)
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
                Time            = -1,           // available from raid start
                ShowOnTarkovMap = false,
            };

            if (!string.IsNullOrEmpty(g.EscortType) && g.EscortAmount != "0")
            {
                spawn.BossEscortType       = g.EscortType;
                spawn.BossEscortAmount     = g.EscortAmount;
                spawn.BossEscortDifficulty = g.EscortDifficulty;
            }

            __result.BossLocationSpawn.Add(spawn);
            injected++;

            // Hostile to the player only, so it has nothing to chase and holds its zone.
            if (g.HostileToPlayer)
                ApplyPlayerHostility(__result, g.BossName, g.EscortType);

            // Once per zone: several groups sharing one zone must not each replace its points.
            if (_contracts.CrewSpawnOnPosts && def.ResolvedPosts.Count > 0 &&
                !string.IsNullOrEmpty(g.BossZone) && zonesPlaced.Add(g.BossZone))
                InjectCrewSpawnPoints(__result, g.BossZone, def.ResolvedPosts, def.Id);
        }

        // The client relocates the crate to AirdropPosition, so it lands on the crew's zone.
        if (def.TriggerAirdrop)
            ForceAirdrop(__result, def.Id);

        if (injected > 0)
            _logger?.Info($"[WeekendDrops] Contract '{def.Id}' - injected {injected} group(s) into {__1} for {__0}");
    }

    // Armed but with no self-firing timer: the client summons the plane on crew-wipe, so the drop
    // rewards the kill. Kept enabled so AirdropPoints load; the window is pushed past raid length.
    private static void ForceAirdrop(LocationBase loc, string contractId)
    {
        loc.AirdropParameters ??= [];
        if (loc.AirdropParameters.Count == 0)
            loc.AirdropParameters.Add(new AirdropParameter());

        foreach (var ap in loc.AirdropParameters)
        {
            ap.PlaneAirdropChance = 1.0;                  // keeps the subsystem enabled
            ap.MinimumPlayersCountToSpawnAirdrop = 1;     // so solo PvE counts
            ap.PlaneAirdropMax = 1;
            ap.PlaneAirdropStartMin = 999999;             // the auto-timer must never elapse; the
            ap.PlaneAirdropStartMax = 999999;             // client summons the drop on crew-wipe
        }

        _logger?.Info($"[WeekendDrops] Contract '{contractId}' - airdrop armed for on-wipe summon (no auto-timer)");
    }

    // A zone spans 300-500m, so a vanilla spawn left the crew walking in. Points are cloned
    // from a real one, keeping collider and side data valid.
    private static void InjectCrewSpawnPoints(LocationBase loc, string zone, List<Vec3> posts, string contractId)
    {
        var all = loc.SpawnPointParams?.ToList();
        if (all is null || all.Count == 0) return;

        static bool IsBotPoint(SpawnPointParam p) =>
            p.Categories?.Any(c => string.Equals(c, "Bot", StringComparison.OrdinalIgnoreCase)) == true;

        // Prefer a template from the target zone so sides/colliders match what spawns there.
        var template = all.FirstOrDefault(p => IsBotPoint(p) &&
                            string.Equals(p.BotZoneName, zone, StringComparison.OrdinalIgnoreCase))
                    ?? all.FirstOrDefault(IsBotPoint);
        if (template is null) return;

        // Drop the zone's vanilla bot points, or the crew rolls one of those instead of a post.
        all.RemoveAll(p => IsBotPoint(p) &&
                           string.Equals(p.BotZoneName, zone, StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < posts.Count; i++)
        {
            all.Add(template with
            {
                Id          = $"WD_{contractId}_{zone}_{i}",
                BotZoneName = zone,
                Position    = new XYZ { X = posts[i].X, Y = posts[i].Y, Z = posts[i].Z },
            });
        }

        loc.SpawnPointParams = all;
        _logger?.Info($"[WeekendDrops] Contract '{contractId}' - zone '{zone}' bot spawns replaced with {posts.Count} post(s)");
    }

    // Per-raid, via the location's AdditionalHostilitySettings. AlwaysEnemies empty (nothing to
    // chase); PlayerBehaviour = AlwaysEnemies so it's winnable as PMC and as Scav.
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
                AlwaysEnemies = new HashSet<string>(),   // neutral to all AI
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
