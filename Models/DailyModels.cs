using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

public class DailyChallengeDefinition
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

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; } = 50;

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; } = 1;

    [JsonPropertyName("requiresLootNet")]
    public bool RequiresLootNet { get; set; }
}

public class DailyChallengeProgress
{
    public string DefinitionId { get; set; } = "";
    public int Current { get; set; }
    public int Target { get; set; }
    public bool Completed => Current >= Target;
    public bool RewardClaimed { get; set; }

    [JsonIgnore]
    public DailyChallengeDefinition? Definition { get; set; }
}

public class PlayerDailyState
{
    public string DailyId { get; set; } = "";
    public List<DailyChallengeProgress> Challenges { get; set; } = [];

    // True once the complete-all daily bonus has been collected for this DailyId.
    // Persisted so the bonus can't be re-claimed after a game/server restart.
    public bool BonusClaimed { get; set; }

    public string LastRaidId { get; set; } = "";

    public float SurvivalTimeBank { get; set; }
}

public class ShopItemDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = "";

    [JsonPropertyName("gpCost")]
    public int GpCost { get; set; }

    // -1 = unlimited, 0 = sold out, >0 = remaining stock
    [JsonPropertyName("stock")]
    public int Stock { get; set; } = -1;

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("contents")]
    public List<ShopBundleEntry>? Contents { get; set; }

    // When set, this entry is a Trade-in (handover) rather than a GP purchase: the player
    // hands over these junk items and receives Contents in return. GpCost is ignored.
    [JsonPropertyName("barterCost")]
    public List<ShopBundleEntry>? BarterCost { get; set; }

    [JsonPropertyName("restockHours")]
    public double? RestockHours { get; set; }

    // When true, buying this item fires the full-screen trophy ceremony client-side
    // (the Gamma container and any future top-end grail item).
    [JsonPropertyName("trophy")]
    public bool Trophy { get; set; }
}

public class ShopBundleEntry
{
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}
