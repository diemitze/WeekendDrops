using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

public class CratePoolTier
{
    [JsonPropertyName("rewardCount")]
    public int RewardCount { get; set; } = 2;

    [JsonPropertyName("pool")]
    public Dictionary<string, double> Pool { get; set; } = new();
}

public class CratePoolsConfig
{
    [JsonPropertyName("foundInRaid")]
    public bool FoundInRaid { get; set; } = true;

    [JsonPropertyName("tiers")]
    public Dictionary<string, CratePoolTier> Tiers { get; set; } = new();
}
