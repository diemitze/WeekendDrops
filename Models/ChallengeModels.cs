using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

public enum ChallengeType
{
    KillScavs,
    KillPMCs,
    KillBoss,
    KillCultists, // cultists (Sektanty); credited from the client's per-raid cultist tally
    KillPriest,   // the cultist boss (Sektant Priest) specifically
    KillRaiders,  // Raiders (WildSpawnType pmcBot)
    KillRogues,   // Rogues (WildSpawnType exUsec)
    MeleeKills,   // kills whose lethal blow was a melee strike (rare joke challenge)
    KillsSingleRaid, // combined kills of any type in one raid (spike)
    SurviveTimeSingleRaid, // seconds survived in one raid (kept modest - raids are ~30 min)
    KillHeadshots,
    GrenadeKills, // kills whose lethal damage type is GrenadeFragment (frag-grenade shrapnel)
    SurviveTimeCumulative, // accumulated seconds across raids, resets fully on death
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
    public static string Group(ChallengeType t) => t switch
    {
        ChallengeType.KillPMCs or ChallengeType.KillPMCsSingleRaid          => "pmc",
        ChallengeType.KillScavs or ChallengeType.KillScavsSingleRaid
            or ChallengeType.ScavKills                                      => "scav",
        ChallengeType.KillBoss or ChallengeType.KillPriest                 => "boss",
        ChallengeType.KillCultists                                         => "cultist",
        ChallengeType.KillRaiders                                          => "raider",
        ChallengeType.KillRogues                                           => "rogue",
        ChallengeType.MeleeKills                                           => "melee",
        ChallengeType.KillsSingleRaid                                      => "singleraid",
        ChallengeType.SurviveTimeSingleRaid                                => "survive",
        ChallengeType.KillHeadshots                                        => "headshot",
        ChallengeType.GrenadeKills                                         => "grenade",
        ChallengeType.SurviveTimeCumulative                                => "survive",
        ChallengeType.ExtractSuccessfully or ChallengeType.ExtractFromLocation => "extract",
        ChallengeType.ScavRaidsDone or ChallengeType.ScavExtract
            or ChallengeType.ScavExtractFromLocation                       => "scavrun",
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

    // Target value: kill count, seconds for survival, extract count
    [JsonPropertyName("target")]
    public int Target { get; set; }

    // For ExtractFromLocation - null means any map
    [JsonPropertyName("targetLocation")]
    public string? TargetLocation { get; set; }

    // For KillBoss - null means any boss
    [JsonPropertyName("targetBoss")]
    public string? TargetBoss { get; set; }

    // 1 = easy, 2 = medium, 3 = hard (used for weighted random selection)
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
    // ISO week string like "2026-W23" - used to detect when a new weekend starts
    public string WeekendId { get; set; } = "";

    public string LastRaidId { get; set; } = "";

    public List<ChallengeProgress> Challenges { get; set; } = [];

    public List<int> ClaimedTiers { get; set; } = [];

    // Survival time bank in seconds - resets to 0 on death
    public float SurvivalTimeBank { get; set; }

    // The plan (challenge count + point budget) this set was built under. A mismatch on
    // load means the plan changed (Scav/LootNET toggled) and the set should reroll.
    public int PlanCount { get; set; }
    public int PlanBudget { get; set; }
}

public class ModConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    // DayOfWeek value (0=Sun, 5=Fri)
    [JsonPropertyName("weekendStartDay")]
    public int WeekendStartDay { get; set; } = 5; // Friday

    [JsonPropertyName("weekendStartHour")]
    public int WeekendStartHour { get; set; } = 18;

    // DayOfWeek value (1=Mon)
    [JsonPropertyName("weekendEndDay")]
    public int WeekendEndDay { get; set; } = 1; // Monday

    [JsonPropertyName("weekendEndHour")]
    public int WeekendEndHour { get; set; } = 4;

    [JsonPropertyName("challengesPerWeekend")]
    public int ChallengesPerWeekend { get; set; } = 8;

    [JsonPropertyName("weekendDifficultyBudget")]
    public int WeekendDifficultyBudget { get; set; } = 18;

    // How long (hours) unclaimed drop mail stays before expiring
    [JsonPropertyName("dropExpiryHours")]
    public int DropExpiryHours { get; set; } = 72;

    // When true: weekend is always active (for testing). Debug also offers all contracts
    // with unlimited picks, and exposes the in-panel debug controls.
    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; set; } = false;

    [JsonPropertyName("shopRestockHours")]
    public double ShopRestockHours { get; set; } = 6;

    // Interval (hours) for the global shop restock that refills limited stock.
    [JsonPropertyName("shopGlobalRestockHours")]
    public double ShopGlobalRestockHours { get; set; } = 24;

    // Multiplier on every GP shop price (1.0 = as-is). Scales the whole shop without
    // editing each shop.json entry; applied to both display and charge.
    [JsonPropertyName("shopPriceMultiplier")]
    public double ShopPriceMultiplier { get; set; } = 1.0;

    [JsonPropertyName("kitWeaponDrops")]
    public bool KitWeaponDrops { get; set; } = true;

    // Manual override to force LootNET loot-value challenges on (normally auto-detected).
    [JsonPropertyName("includeLootNet")]
    public bool IncludeLootNet { get; set; } = false;

    // When false, Scav-run challenges (extract/kills/raids as a Scav) are dropped
    // from both the weekend and daily pools - for players who run Scav raids off.
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

    // GP credited to the virtual balance when this tier is claimed (on top of the
    // drop crate). Shown on the tier card in the client.
    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    // One pool is picked at random per delivery
    [JsonPropertyName("pools")]
    public List<DropPool> Pools { get; set; } = [];
}

public class DropsConfig
{
    [JsonPropertyName("tiers")]
    public List<DropTier> Tiers { get; set; } = [];
}
