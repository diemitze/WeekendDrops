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
                    spawn.BossEscortAmount     = g.EscortAmount;
                    spawn.BossEscortDifficulty = g.EscortDifficulty;
                }

                __result.BossLocationSpawn.Add(spawn);
                injected++;

                if (g.HostileToPlayer)
                    ApplyPlayerHostility(__result, g.BossName, g.EscortType);

                if (_contracts.CrewSpawnOnPosts && def.ResolvedPosts.Count > 0 &&
                    !string.IsNullOrEmpty(g.BossZone) && zonesPlaced.Add(g.BossZone))
                    InjectCrewSpawnPoints(__result, g.BossZone, def.ResolvedPosts, def.Id);
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

    private static void InjectCrewSpawnPoints(LocationBase loc, string zone, List<Vec3> posts, string contractId)
    {
        var all = loc.SpawnPointParams?.ToList();
        if (all is null || all.Count == 0) return;

        static bool IsBotPoint(SpawnPointParam p) =>
            p.Categories?.Any(c => string.Equals(c, "Bot", StringComparison.OrdinalIgnoreCase)) == true;

        var template = all.FirstOrDefault(p => IsBotPoint(p) &&
                            string.Equals(p.BotZoneName, zone, StringComparison.OrdinalIgnoreCase))
                    ?? all.FirstOrDefault(IsBotPoint);
        if (template is null) return;

        all.RemoveAll(p => IsBotPoint(p) &&
                           string.Equals(p.BotZoneName, zone, StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < posts.Count; i++)
        {
            all.Add(template with
            {
                Id          = $"WD_{contractId}_{zone}_{i}",
                BotZoneName = zone,
                Position    = new Vector3 { X = posts[i].X, Y = posts[i].Y, Z = posts[i].Z },
            });
        }

        loc.SpawnPointParams = all;
        _logger?.Info($"[WeekendDrops] Contract '{contractId}' - zone '{zone}' bot spawns replaced with {posts.Count} post(s)");
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
