using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using WeekendDrops.Patches;
using WeekendDrops.Services;

namespace WeekendDrops;

// Must run AFTER content-adding mods (e.g. WTT-ContentBackport registers its
// items at PostDBModLoader + 2/3). IOnLoad runs ascending by TypePriority, so a
// larger offset means we fold those items into the drop pools only once they're
// actually in the DB. Still well inside the PostDBModLoader band (next phase is
// +100000), so this stays before trader/ragfair registration.
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1000)]
public class WeekendDropsLoader(
    WeekendChallengeService weekendChallengeService,
    DailyChallengeService dailyChallengeService,
    ContractService contractService,
    DatabaseService databaseService,
    JsonUtil jsonUtil,
    ItemHelper itemHelper,
    ISptLogger<WeekendDropsLoader> logger) : IOnLoad
{
    public Task OnLoad()
    {
        logger.LogWithColor(
            "[WeekendDrops] Loading weekend challenges & drop crates...",
            LogTextColor.Yellow);

        // The contract crew rides on the vanilla 'cursedAssault' WildSpawnType - its
        // CursAssault brain is one SAIN actually controls, and it never spawns via normal
        // waves - so we just override that bot type's definition in the DB from
        // db/bots/types - no MoreBotsAPI, no custom enum.
        LoadContractBotTypes();

        weekendChallengeService.LoadConfig();
        dailyChallengeService.LoadConfig();
        contractService.LoadConfig();

        // Wire loot pools into the drop crates so opening them actually yields loot.
        weekendChallengeService.RegisterLootContainerPools();

        // Give the paid GP-shop Arena crates their own (richer) loot pools too -
        // otherwise they open empty / with vanilla loot and aren't worth the coins.
        weekendChallengeService.RegisterArenaShopPools();

        // Make crate ammo drop as a full stack instead of a single round / empty box.
        LootContainerAmmoStackPatch.Apply(itemHelper, logger);

        // Cap bulky wearables (backpack / armor / rig / headwear) to one per crate so
        // an Equipment crate can't hand out 3 backpacks at once.
        CrateCategoryCapPatch.Apply(itemHelper, logger);

        // Contracts: force-spawn an accepted contract's boss group on the player's next
        // raid of the target map (per-session, no global DB edit).
        ContractSpawnPatch.Apply(contractService, logger);

        // Drop each contract's bonusItems onto the bots it spawns (e.g. a LEDX so the
        // crew is worth looting).
        ContractBotLootPatch.Apply(contractService, logger);

        // Bolt compatible attachments onto weapons pulled from crates so they arrive
        // kitted instead of as the bare default preset.
        if (weekendChallengeService.Config.KitWeaponDrops)
            WeaponKitPatch.Apply(itemHelper, logger);

        // Challenge progress is driven by the client reporting each raid result to
        // /weekenddrops/raidend (see WeekendChallengeService.ApplyRaidResult) - no
        // server-side raid-end hook, because PvE/co-op (PitFireTeam) owns that flow.

        bool active = weekendChallengeService.IsWeekendActive();
        PrintBanner(active, weekendChallengeService.GetWeekendScheduleText());
        return Task.CompletedTask;
    }

    // Override vanilla bot types with our own definitions shipped in db/bots/types
    // (e.g. cursedassault.json = the Cleanup Crew look/loadout/loot). Each file's
    // name is the WildSpawnType key; we replace the whole entry so pinned single-value
    // appearance/inventory arrays aren't merged back into randomness. SAIN still drives
    // the combat brain off the WildSpawnType, so the crew fights properly.
    private void LoadContractBotTypes()
    {
        var modDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var typesDir = System.IO.Path.Combine(modDir, "db", "bots", "types");
        if (!Directory.Exists(typesDir))
        {
            logger.Warning($"[WeekendDrops] No bot types dir at {typesDir} - contract crew will use vanilla gear");
            return;
        }

        var types = databaseService.GetBots().Types;
        foreach (var file in Directory.GetFiles(typesDir, "*.json"))
        {
            var role = System.IO.Path.GetFileNameWithoutExtension(file);
            var botType = jsonUtil.DeserializeFromFile<BotType>(file);
            if (botType is null)
            {
                logger.Error($"[WeekendDrops] Failed to read bot type {file}");
                continue;
            }

            // Match the existing key case-insensitively so we override (not duplicate) it.
            var key = types.Keys.FirstOrDefault(k => string.Equals(k, role, StringComparison.OrdinalIgnoreCase)) ?? role;
            types[key] = botType;
            logger.Info($"[WeekendDrops] Contract bot type '{key}' overridden from {System.IO.Path.GetFileName(file)}");
        }
    }

    private void PrintBanner(bool weekendActive, string schedule)
    {
        var status = weekendActive
            ? "WEEKEND AVAILABLE! Go get loot!"
            : $"weekend closed, opens {schedule}";
        var statusColor = weekendActive ? LogTextColor.Green : LogTextColor.Gray;

        // Inner width between the vertical bars. Centre each line to exactly this
        // many chars so the right border always lines up (no hand-counted spaces).
        const int width = 42;
        string Row(string text)
        {
            var pad = width - text.Length;
            var left = pad / 2;
            return "  ║" + new string(' ', left) + text + new string(' ', pad - left) + "║";
        }

        var border = new string('═', width);
        logger.LogWithColor("  ╔" + border + "╗", LogTextColor.Cyan);
        logger.LogWithColor(Row("W E E K E N D   D R O P S"), LogTextColor.Cyan);
        logger.LogWithColor(Row("loot crates and daily challenges"), LogTextColor.Cyan);
        logger.LogWithColor("  ╚" + border + "╝", LogTextColor.Cyan);
        logger.LogWithColor($"     {status}", statusColor);
        logger.Success("[WeekendDrops] Loaded successfully");
    }
}
