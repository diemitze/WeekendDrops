using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

public enum ChallengeType
{
    KillScavs,
    KillPMCs,
    KillBoss,
    KillCultists,
    KillPriest,   // the cultist boss, a subset of KillCultists
    KillRaiders,  // WildSpawnType pmcBot
    KillRogues,   // WildSpawnType exUsec
    MeleeKills,
    KillsSingleRaid,        // any type, one raid
    SurviveTimeSingleRaid,  // seconds; raids are ~30 min, so targets stay modest
    KillHeadshots,
    GrenadeKills,           // GrenadeFragment damage only, not mines or artillery
    SurviveTimeCumulative,  // seconds across raids, wiped fully on death
    ExtractSuccessfully,
    ExtractFromLocation,

    KillPMCsSingleRaid,
    KillScavsSingleRaid,

    ScavExtract,
    ScavKills,
    ScavRaidsDone,
    ScavExtractFromLocation,

    ExtractWithLootValue,
    LootValueCumulative,
}

public static class ChallengeMetrics
{
    // One challenge is drawn per group, so a group of its own means it shows up most weekends.
    public static string Group(ChallengeType t) => t switch
    {
        ChallengeType.KillPMCs                                             => "pmc",
        ChallengeType.KillPMCsSingleRaid                                   => "pmc_spike",
        ChallengeType.KillScavs                                            => "scav",
        ChallengeType.KillScavsSingleRaid                                  => "scav_spike",

        // Rare spawns share one slot, or a weekend fills up with hunts that may never spawn.
        ChallengeType.KillBoss or ChallengeType.KillPriest
            or ChallengeType.KillCultists                                  => "hunt",
        ChallengeType.KillRaiders or ChallengeType.KillRogues              => "elite",

        ChallengeType.MeleeKills                                           => "melee",
        ChallengeType.KillsSingleRaid                                      => "singleraid",
        ChallengeType.SurviveTimeSingleRaid                                => "survive_raid",
        ChallengeType.SurviveTimeCumulative                                => "survive",
        ChallengeType.KillHeadshots                                        => "headshot",
        ChallengeType.GrenadeKills                                         => "grenade",

        ChallengeType.ExtractSuccessfully                                  => "extract",
        ChallengeType.ExtractFromLocation                                  => "extract_map",

        ChallengeType.ScavRaidsDone or ChallengeType.ScavExtract
            or ChallengeType.ScavKills                                     => "scavrun",
        ChallengeType.ScavExtractFromLocation                              => "scavrun_map",

        ChallengeType.ExtractWithLootValue or ChallengeType.LootValueCumulative => "loot",
        _                                                                  => t.ToString(),
    };

    // Scav-run-only challenges. Dropped from the pools when Scav raids are disabled.
    public static bool IsScavOnly(ChallengeType t) => t switch
    {
        ChallengeType.ScavExtract or ChallengeType.ScavKills
            or ChallengeType.ScavRaidsDone or ChallengeType.ScavExtractFromLocation => true,
        _ => false,
    };
}

public class ChallengeDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public ChallengeType Type { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    // Kills, extracts, or seconds, depending on Type.
    [JsonPropertyName("target")]
    public int Target { get; set; }

    // ExtractFromLocation only; null = any map.
    [JsonPropertyName("targetLocation")]
    public string? TargetLocation { get; set; }

    // KillBoss only; null = any boss.
    [JsonPropertyName("targetBoss")]
    public string? TargetBoss { get; set; }

    // 1 = easy, 2 = medium, 3 = hard.
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
    // ISO week string like "2026-W23".
    public string WeekendId { get; set; } = "";

    public string LastRaidId { get; set; } = "";

    public List<ChallengeProgress> Challenges { get; set; } = [];

    public List<int> ClaimedTiers { get; set; } = [];

    // Seconds; resets to 0 on death.
    public float SurvivalTimeBank { get; set; }

    // The plan this set was built under. A mismatch on load means the plan changed, so the
    // set rerolls.
    public int PlanCount { get; set; }
    public int PlanBudget { get; set; }
}

public class ModConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    // DayOfWeek: 0 = Sunday.
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

    // Forces the weekend always-active, offers every contract with unlimited picks, and
    // exposes the in-panel debug controls.
    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; set; } = false;

    [JsonPropertyName("shopRestockHours")]
    public double ShopRestockHours { get; set; } = 6;

    [JsonPropertyName("shopGlobalRestockHours")]
    public double ShopGlobalRestockHours { get; set; } = 24;

    // 1.0 = prices as written. Applied to both the display and the charge.
    [JsonPropertyName("shopPriceMultiplier")]
    public double ShopPriceMultiplier { get; set; } = 1.0;

    [JsonPropertyName("kitWeaponDrops")]
    public bool KitWeaponDrops { get; set; } = true;

    // Override for the auto-detection of LootNET.
    [JsonPropertyName("includeLootNet")]
    public bool IncludeLootNet { get; set; } = false;

    // False drops Scav-run challenges from both the weekend and daily pools.
    [JsonPropertyName("enableScavChallenges")]
    public bool EnableScavChallenges { get; set; } = true;
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

    // Credited on top of the drop crate.
    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    // One is picked at random per delivery.
    [JsonPropertyName("pools")]
    public List<DropPool> Pools { get; set; } = [];
}

public class DropsConfig
{
    [JsonPropertyName("tiers")]
    public List<DropTier> Tiers { get; set; } = [];
}
