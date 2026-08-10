using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Services.InRaid;
using WeekendDrops.Services;

namespace WeekendDrops.Patches;

public static class WeekendXpPatch
{
    private static WeekendModifierService? _modifiers;
    private static ProfileHelper? _profiles;
    private static ISptLogger<WeekendDropsLoader>? _logger;
    private static bool _applied;

    public static void Apply(WeekendModifierService modifiers, ProfileHelper profiles,
                             ISptLogger<WeekendDropsLoader> logger)
    {
        _modifiers = modifiers;
        _profiles = profiles;
        _logger = logger;
        if (_applied) return;

        var target = AccessTools.Method(typeof(LocationLifecycleService), "HandlePostRaidPmc");
        if (target is null)
        {
            logger.Error("[WeekendDrops] HandlePostRaidPmc not found - the XP modifier will not pay", null);
            return;
        }

        new Harmony("com.weekenddrops.xp").Patch(
            target,
            prefix:  new HarmonyMethod(typeof(WeekendXpPatch), nameof(Prefix)),
            postfix: new HarmonyMethod(typeof(WeekendXpPatch), nameof(Postfix)));
        _applied = true;
    }

    private static void Prefix(MongoId sessionId, SptProfile fullServerProfile, out long __state)
    {
        __state = -1;
        try
        {
            if (_modifiers is null || _modifiers.XpMultiplier <= 1.0) return;
            __state = fullServerProfile?.CharacterData?.PmcData?.Info?.Experience ?? -1;
        }
        catch { __state = -1; }
    }

    private static void Postfix(MongoId sessionId, SptProfile fullServerProfile, long __state)
    {
        try
        {
            if (__state < 0 || _modifiers is null) return;

            double mult = _modifiers.XpMultiplier;
            if (mult <= 1.0) return;

            long after = fullServerProfile?.CharacterData?.PmcData?.Info?.Experience ?? -1;
            long gained = after - __state;
            if (gained <= 0) return;

            int bonus = (int)Math.Round(gained * (mult - 1.0));
            if (bonus <= 0) return;

            _profiles?.AddExperienceToPmc(sessionId, bonus);
            _logger?.Info($"[WeekendDrops] XP modifier paid +{bonus} XP on top of {gained} (x{mult:0.##})");
        }
        catch (Exception ex)
        {
            _logger?.Error($"[WeekendDrops] XP modifier failed: {ex.Message}", null);
        }
    }
}
