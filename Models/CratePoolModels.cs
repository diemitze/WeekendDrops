using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

/// One entry in a tier's recipe. Counts across all slots must add up to RewardCount;
/// a slot with a Chance rolls for it and uses Fallback when the roll misses.
public class CrateSlot
{
    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("chance")]
    public double Chance { get; set; } = 1.0;

    [JsonPropertyName("fallback")]
    public string? Fallback { get; set; }
}

public class CratePoolTier
{
    [JsonPropertyName("rewardCount")]
    public int RewardCount { get; set; } = 2;

    [JsonPropertyName("pool")]
    public Dictionary<string, double> Pool { get; set; } = new();

    /// Empty means the old behaviour: RewardCount independent draws from the whole pool.
    [JsonPropertyName("slots")]
    public List<CrateSlot> Slots { get; set; } = new();

    /// Grade of attachment WeaponKitPatch fits to a gun from this tier.
    [JsonPropertyName("modTier")]
    public string ModTier { get; set; } = "";
}

public class CratePoolsConfig
{
    [JsonPropertyName("foundInRaid")]
    public bool FoundInRaid { get; set; } = true;

    [JsonPropertyName("tiers")]
    public Dictionary<string, CratePoolTier> Tiers { get; set; } = new();
}
