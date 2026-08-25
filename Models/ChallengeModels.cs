using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Repeatable;

namespace WeekendDrops.Models;

public enum ChallengeType
{
    KillScavs,
    KillPMCs,
    KillBoss,
    KillCultists,
    KillPriest,
    KillRaiders,
    KillRogues,
    MeleeKills,
    KillsSingleRaid,
    KillsCumulative,
    SurviveTimeSingleRaid,
    KillHeadshots,
    KillHeadshotsSingleRaid,
    GrenadeKills,

    KillLegs,
    KillArms,
    KillStomach,
    SurviveTimeCumulative,
    ExtractSuccessfully,
    ExtractFromLocation,
    RaidsDone,

    KillPMCsSingleRaid,
    KillScavsSingleRaid,

    ScavExtract,
    ScavKills,
    ScavKillsSingleRaid,
    ScavRaidsDone,
    ScavExtractFromLocation,

    ExtractWithLootValue,
    LootValueCumulative,

    KillsAtDistance,
    KillsAtDistanceSingleRaid,

    KillsWithWeaponClass,

    KillsSuppressed,
    KillsWithOptic,
    KillsIronSights,
}

public static class ChallengeMetrics
{
    public static string Group(ChallengeType t) => t switch
    {
        ChallengeType.KillPMCs                                             => "pmc",
        ChallengeType.KillPMCsSingleRaid                                   => "pmc_spike",
        ChallengeType.KillScavs                                            => "scav",
        ChallengeType.KillScavsSingleRaid                                  => "scav_spike",

        ChallengeType.KillBoss or ChallengeType.KillPriest
            or ChallengeType.KillCultists                                  => "hunt",
        ChallengeType.KillRaiders or ChallengeType.KillRogues              => "elite",

        ChallengeType.MeleeKills                                           => "melee",
        ChallengeType.KillsSingleRaid                                      => "singleraid",
        ChallengeType.KillsCumulative                                      => "anykill",
        ChallengeType.SurviveTimeSingleRaid                                => "survive_raid",
        ChallengeType.SurviveTimeCumulative                                => "survive",
        ChallengeType.KillHeadshots                                        => "headshot",
        ChallengeType.KillHeadshotsSingleRaid                              => "headshot_spike",
        ChallengeType.GrenadeKills                                         => "grenade",

        ChallengeType.KillLegs or ChallengeType.KillArms
            or ChallengeType.KillStomach                                   => "limb",

        ChallengeType.ExtractSuccessfully                                  => "extract",
        ChallengeType.ExtractFromLocation                                  => "extract_map",
        ChallengeType.RaidsDone                                            => "raids",

        ChallengeType.ScavRaidsDone or ChallengeType.ScavExtract
            or ChallengeType.ScavKills                                     => "scavrun",
        ChallengeType.ScavKillsSingleRaid                                  => "scavrun_spike",
        ChallengeType.ScavExtractFromLocation                              => "scavrun_map",

        ChallengeType.ExtractWithLootValue or ChallengeType.LootValueCumulative => "loot",

        ChallengeType.KillsAtDistance                                      => "distance",
        ChallengeType.KillsAtDistanceSingleRaid                            => "distance_spike",
        ChallengeType.KillsWithWeaponClass                                 => "weapclass",
        ChallengeType.KillsSuppressed                                      => "suppressed",
        ChallengeType.KillsWithOptic or ChallengeType.KillsIronSights      => "sight",

        _                                                                  => t.ToString(),
    };

    public static bool IsScavOnly(ChallengeType t) => t switch
    {
        ChallengeType.ScavExtract or ChallengeType.ScavKills
            or ChallengeType.ScavKillsSingleRaid
            or ChallengeType.ScavRaidsDone or ChallengeType.ScavExtractFromLocation => true,
        _ => false,
    };
}

/// The fields ChallengeProgression needs, so weekly and daily definitions can share it.
public interface IChallengeDefinition
{
    ChallengeType Type { get; }
    string? TargetLocation { get; }
    string? TargetBoss { get; }
    string? TargetWeaponClass { get; }
    int MinDistanceMeters { get; }
}

public class ChallengeDefinition : IChallengeDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public ChallengeType Type { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("target")]
    public int Target { get; set; }

    [JsonPropertyName("targetLocation")]
    public string? TargetLocation { get; set; }

    [JsonPropertyName("targetBoss")]
    public string? TargetBoss { get; set; }

    [JsonPropertyName("targetWeaponClass")]
    public string? TargetWeaponClass { get; set; }

    [JsonPropertyName("minDistanceMeters")]
    public int MinDistanceMeters { get; set; }

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; } = 1;

    [JsonPropertyName("requiresLootNet")]
    public bool RequiresLootNet { get; set; }
}

public class ChallengeProgress
{
    public string DefinitionId { get; set; } = "";
    public int Current { get; set; }
    public bool Completed => Current >= Target;
    public int Target { get; set; }

    [JsonIgnore]
    public ChallengeDefinition? Definition { get; set; }
}

public class PlayerWeekendState
{
    public string WeekendId { get; set; } = "";

    public string LastRaidId { get; set; } = "";

    public List<ChallengeProgress> Challenges { get; set; } = [];

    public List<int> ClaimedTiers { get; set; } = [];

    public float SurvivalTimeBank { get; set; }

    public int RerollsUsed { get; set; }

    public int PlanCount { get; set; }
    public int PlanBudget { get; set; }
}

public class ModConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("weekendStartDay")]
    public int WeekendStartDay { get; set; } = 5;

    [JsonPropertyName("weekendStartHour")]
    public int WeekendStartHour { get; set; } = 18;

    [JsonPropertyName("weekendEndDay")]
    public int WeekendEndDay { get; set; } = 1;

    [JsonPropertyName("weekendEndHour")]
    public int WeekendEndHour { get; set; } = 4;

    [JsonPropertyName("challengesPerWeekend")]
    public int ChallengesPerWeekend { get; set; } = 8;

    [JsonPropertyName("weekendDifficultyBudget")]
    public int WeekendDifficultyBudget { get; set; } = 18;

    [JsonPropertyName("dropExpiryHours")]
    public int DropExpiryHours { get; set; } = 72;

    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; set; } = false;

    [JsonPropertyName("shopRestockHours")]
    public double ShopRestockHours { get; set; } = 6;

    [JsonPropertyName("shopGlobalRestockHours")]
    public double ShopGlobalRestockHours { get; set; } = 24;

    [JsonPropertyName("shopPriceMultiplier")]
    public double ShopPriceMultiplier { get; set; } = 1.0;

    [JsonPropertyName("kitWeaponDrops")]
    public bool KitWeaponDrops { get; set; } = true;

    [JsonPropertyName("includeLootNet")]
    public bool IncludeLootNet { get; set; } = false;

    [JsonPropertyName("enableScavChallenges")]
    public bool EnableScavChallenges { get; set; } = true;

    [JsonPropertyName("enableWeekendReroll")]
    public bool EnableWeekendReroll { get; set; } = true;

    [JsonPropertyName("weekendRerollCost")]
    public int WeekendRerollCost { get; set; } = 40;

    [JsonPropertyName("weekendRerollCostStep")]
    public int WeekendRerollCostStep { get; set; } = 20;

    [JsonPropertyName("weekendRerollMaxPerWeekend")]
    public int WeekendRerollMaxPerWeekend { get; set; } = 3;

    [JsonPropertyName("enableDailyReroll")]
    public bool EnableDailyReroll { get; set; } = true;

    [JsonPropertyName("dailyRerollCost")]
    public int DailyRerollCost { get; set; } = 25;

    [JsonPropertyName("dailyRerollCostStep")]
    public int DailyRerollCostStep { get; set; } = 25;

    [JsonPropertyName("dailyRerollMaxPerDay")]
    public int DailyRerollMaxPerDay { get; set; } = 2;
}

public enum WeekendModifierKind
{
    WeaponClass,
    GpMultiplier,
    XpMultiplier,
    HeadshotKill,
    MeleeKill,
    GrenadeKill,
    SuppressedKill,
    LongRangeKill,
}

public static class WeekendModifierKinds
{
    public static bool IsPerKill(WeekendModifierKind k) => k switch
    {
        WeekendModifierKind.WeaponClass or WeekendModifierKind.HeadshotKill
            or WeekendModifierKind.MeleeKill or WeekendModifierKind.GrenadeKill
            or WeekendModifierKind.SuppressedKill or WeekendModifierKind.LongRangeKill => true,
        _ => false,
    };
}

public class WeekendModifier
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public WeekendModifierKind Kind { get; set; } = WeekendModifierKind.WeaponClass;

    [JsonPropertyName("weapClass")]
    public string WeapClass { get; set; } = "";

    [JsonPropertyName("multiplier")]
    public double Multiplier { get; set; } = 2.0;

    [JsonPropertyName("gpPerKill")]
    public int? GpPerKill { get; set; }

    [JsonPropertyName("maxKillsPerRaid")]
    public int? MaxKillsPerRaid { get; set; }

    [JsonPropertyName("minDistanceMeters")]
    public int MinDistanceMeters { get; set; } = 100;

    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public class ModifiersConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("gpPerKill")]
    public int GpPerKill { get; set; } = 12;

    [JsonPropertyName("maxKillsPerRaid")]
    public int MaxKillsPerRaid { get; set; } = 15;

    [JsonPropertyName("noneWeight")]
    public int NoneWeight { get; set; } = 4;

    [JsonPropertyName("modifiers")]
    public List<WeekendModifier> Modifiers { get; set; } = [];
}

public class DropPool
{
    [JsonPropertyName("itemIds")]
    public List<string> ItemIds { get; set; } = [];

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}

public class DropTier
{
    [JsonPropertyName("requiredChallenges")]
    public int RequiredChallenges { get; set; }

    [JsonPropertyName("tierName")]
    public string TierName { get; set; } = "";

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    [JsonPropertyName("pools")]
    public List<DropPool> Pools { get; set; } = [];
}

public class DropsConfig
{
    [JsonPropertyName("tiers")]
    public List<DropTier> Tiers { get; set; } = [];
}
