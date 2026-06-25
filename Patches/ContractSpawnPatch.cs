using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using WeekendDrops.Services;

namespace WeekendDrops.Patches;

// Injects a contract's boss spawn into a raid as it's generated. LocationLifecycleService.
// GenerateLocationAndLoot(sessionId, name) builds the LocationBase whose BossLocationSpawn
// list drives boss spawns, the same pipeline the Goons use. We add a forced spawn ONLY when
// the requesting player has an active contract for that map - so it's per-raid and per-session,
// never a global DB edit.
public static class ContractSpawnPatch
{
    private static ContractService? _contracts;
    private static ISptLogger<WeekendDropsLoader>? _logger;
    private static bool _applied;

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
        if (def is null || def.Groups is null || def.Groups.Count == 0) return;

        __result.BossLocationSpawn ??= [];
        int injected = 0;

        // One forced spawn per group - one group = a boss + guards, several groups = the
        // roaming event crews.
        foreach (var g in def.Groups)
        {
            if (string.IsNullOrEmpty(g.BossName)) continue;

            // If this map already spawns the same boss natively (a boss hunted on its
            // lore home map), disable the vanilla copy so the player faces exactly one -
            // ours, with the contract's escort and zone - instead of two of the same boss.
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

            // Make the crew hostile to the player only and neutral to all AI - so it has
            // nothing to chase across the map and holds its spawn zone until the player
            // arrives.
            if (g.HostileToPlayer)
                ApplyPlayerHostility(__result, g.BossName, g.EscortType);
        }

        // Supply Run: force a guaranteed airdrop on this map. The client relocates the
        // crate to the contract's AirdropPosition so it lands on the crew's zone.
        if (def.TriggerAirdrop)
            ForceAirdrop(__result, def.Id);

        if (injected > 0)
        {
            _logger?.Info($"[WeekendDrops] Contract '{def.Id}' - injected {injected} group(s) into {__1} for {__0}");

            // TEMP debug: dump the full boss spawn list so we can see exactly what went
            // into the raid (boss name, escort type/amount, difficulty, zone). Remove
            // once custom-type spawning is confirmed.
            foreach (var s in __result.BossLocationSpawn)
                _logger?.Info($"[WeekendDrops] DEBUG BossLocationSpawn: boss={s.BossName} chance={s.BossChance} force={s.ForceSpawn} diff={s.BossDifficulty} zone='{s.BossZone}' escort={s.BossEscortType}x{s.BossEscortAmount} escortDiff={s.BossEscortDifficulty}");
        }
    }

    // Prepare the Supply Run airdrop WITHOUT letting it self-fire on a timer. The client
    // summons the plane on demand the moment the player wipes the crew (GameWorld.InitAirdrop),
    // so the drop is the reward for the kill, not a coincident timed event.
    //
    // We keep the airdrop subsystem ENABLED (chance > 0, MinPlayers 1) so the map's
    // AirdropPoints load and the manager initialises - InitAirdrop needs those points. But
    // we push the auto-trigger window far beyond any raid length, so the native timer never
    // elapses and never drops a crate on its own. The only drop that happens is the one the
    // client calls in on crew-wipe. Per-raid only (we edit the generated LocationBase).
    private static void ForceAirdrop(LocationBase loc, string contractId)
    {
        loc.AirdropParameters ??= [];
        if (loc.AirdropParameters.Count == 0)
            loc.AirdropParameters.Add(new AirdropParameter());

        foreach (var ap in loc.AirdropParameters)
        {
            ap.PlaneAirdropChance = 1.0;                  // keep the subsystem enabled (points load)
            ap.MinimumPlayersCountToSpawnAirdrop = 1;     // solo PvE counts
            ap.PlaneAirdropMax = 1;                       // at most one
            ap.PlaneAirdropStartMin = 999999;             // auto-timer never elapses; the
            ap.PlaneAirdropStartMax = 999999;             // client summons the drop on crew-wipe
        }

        _logger?.Info($"[WeekendDrops] Contract '{contractId}' - airdrop armed for on-wipe summon (no auto-timer)");
    }

    // Make the given role(s) hostile to the PLAYER only and neutral to all AI, per-raid,
    // via the location's AdditionalHostilitySettings. AlwaysEnemies stays empty (no AI is
    // a target -> nothing to chase, holds its zone); AlwaysFriends = own squad so it never
    // shoots itself; the *PlayerBehaviour = AlwaysEnemies covers the human player (any
    // side, so the bounty is winnable in PMC and Scav raids alike).
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
