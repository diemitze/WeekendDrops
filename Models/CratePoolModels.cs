using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

public class CratePoolTier
{
    // How many items the crate spawns when opened.
    [JsonPropertyName("rewardCount")]
    public int RewardCount { get; set; } = 2;

    // itemTpl -> weight (higher = more likely). Low weights make a pull rare.
    [JsonPropertyName("pool")]
    public Dictionary<string, double> Pool { get; set; } = new();
}

public class CratePoolsConfig
{
    // Mark spawned rewards as found-in-raid.
    [JsonPropertyName("foundInRaid")]
    public bool FoundInRaid { get; set; } = true;

    [JsonPropertyName("tiers")]
    public Dictionary<string, CratePoolTier> Tiers { get; set; } = new();
}
