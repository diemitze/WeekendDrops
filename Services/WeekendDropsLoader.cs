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

// Must run AFTER content-adding mods (WTT-ContentBackport registers items at PostDBModLoader+2/3).
// IOnLoad runs ascending by TypePriority, so a larger offset folds those items into the pools only
// once they're in the DB. Still inside the PostDBModLoader band (before trader/ragfair).
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

        // The crew rides on the vanilla 'cursedAssault' type (SAIN drives its brain, and it never
        // spawns via normal waves), so only its DB definition is overridden. No MoreBotsAPI.
        LoadContractBotTypes();

        weekendChallengeService.LoadConfig();
        dailyChallengeService.LoadConfig();
        contractService.LoadConfig();

        // Without a pool, the crates open empty.
        weekendChallengeService.RegisterLootContainerPools();

        // Arena crates need richer pools of their own, or they aren't worth the coins.
        weekendChallengeService.RegisterArenaShopPools();

        // Make crate ammo drop as a full stack instead of a single round / empty box.
        LootContainerAmmoStackPatch.Apply(itemHelper, logger);

        // One bulky wearable per crate, or an Equipment crate can roll 3 backpacks.
        CrateCategoryCapPatch.Apply(itemHelper, logger);

        // Per-session, so neither of these edits the DB globally.
        ContractSpawnPatch.Apply(contractService, logger);
        ContractBotLootPatch.Apply(contractService, logger);

        // Crate weapons arrive kitted instead of as the bare default preset.
        if (weekendChallengeService.Config.KitWeaponDrops)
            WeaponKitPatch.Apply(itemHelper, logger);

        // Progress comes from the client POSTing each raid result to /weekenddrops/raidend.
        // No server-side raid-end hook, because PvE/co-op (PitFireTeam) owns that flow.

        bool active = weekendChallengeService.IsWeekendActive();
        PrintBanner(active, weekendChallengeService.GetWeekendScheduleText());
        return Task.CompletedTask;
    }

    // Filename = the WildSpawnType key. The whole entry is replaced, not merged, so pinned
    // appearance/inventory arrays don't fall back to randomness.
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

            // Match the existing key case-insensitively, to override rather than duplicate.
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

        // Centre each line to exactly this width so the right border lines up.
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
