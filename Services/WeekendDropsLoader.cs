using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using WeekendDrops.Patches;
using WeekendDrops.Services;

namespace WeekendDrops;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class WeekendDropsLoader(
    WeekendChallengeService weekendChallengeService,
    DailyChallengeService dailyChallengeService,
    ItemHelper itemHelper,
    ISptLogger<WeekendDropsLoader> logger) : IOnLoad
{
    public Task OnLoad()
    {
        logger.LogWithColor(
            "[WeekendDrops] Loading weekend challenges & drop crates...",
            LogTextColor.Yellow);

        weekendChallengeService.LoadConfig();
        dailyChallengeService.LoadConfig();

        // Wire loot pools into the drop crates so opening them actually yields loot.
        weekendChallengeService.RegisterLootContainerPools();

        // Give the paid GP-shop Arena crates their own (richer) loot pools too -
        // otherwise they open empty / with vanilla loot and aren't worth the coins.
        weekendChallengeService.RegisterArenaShopPools();

        // Make crate ammo drop as a full stack instead of a single round / empty box.
        LootContainerAmmoStackPatch.Apply(itemHelper, logger);

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
        logger.LogWithColor(Row("loot crates · daily challenges"), LogTextColor.Cyan);
        logger.LogWithColor("  ╚" + border + "╝", LogTextColor.Cyan);
        logger.LogWithColor($"     {status}", statusColor);
        logger.Success("[WeekendDrops] Loaded successfully ✓");
    }
}
