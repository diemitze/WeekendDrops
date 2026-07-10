using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using WeekendDrops.Services;

namespace WeekendDrops.Patches;

// Injects a contract's boss spawn as the raid is generated. GenerateLocationAndLoot builds the
// LocationBase whose BossLocationSpawn list drives boss spawns (the Goons pipeline). Forced spawn
// only when the requesting player has an active contract for that map: per-raid, per-session.
public static class ContractSpawnPatch
{
    private static ContractService? _contracts;
    private static ISptLogger<WeekendDropsLoader>? _logger;
    private static bool _applied;

    // Detect Fika once by the presence of its server mod. Only when Fika is installed do
    // we allow the "any profile's contract" fallback below - so solo SPT with several save
    // profiles never spawns a different profile's contract crew into an unrelated raid.
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

        // Fika/headless: the session that GENERATES the raid is the headless/non-host client,
        // which never accepts contracts, so its own lookup is empty. Fall back to any profile's
        // active contract so the crew/airdrop still spawns.
        if (def is null && _fikaPresent)
        {
            def = _contracts.GetAnyActiveContractForMap(__1);
            if (def != null)
                _logger?.Info($"[WeekendDrops] Fika fallback: raid session {__0} has no contract; spawning another profile's active contract '{def.Id}' for {__1}");
        }

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

    // Prepare the Supply Run airdrop WITHOUT a self-firing timer: the client summons the plane on
    // crew-wipe (InitAirdrop), so the drop rewards the kill. Keep the subsystem enabled (chance>0,
    // MinPlayers 1) so AirdropPoints load, but push the auto-trigger window past any raid length so
    // the native timer never fires. Per-raid only (edits the generated LocationBase).
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

    // Make the given role(s) hostile to the PLAYER only, neutral to all AI, per-raid via the
    // location's AdditionalHostilitySettings. AlwaysEnemies empty (nothing to chase, holds its
    // zone); AlwaysFriends = own squad; PlayerBehaviour = AlwaysEnemies so it's winnable in PMC and Scav.
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
