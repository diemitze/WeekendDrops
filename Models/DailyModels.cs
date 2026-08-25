using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Spt.Repeatable;

namespace WeekendDrops.Models;

public class DailyChallengeDefinition : IChallengeDefinition
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

    public bool BonusClaimed { get; set; }

    public string LastRaidId { get; set; } = "";

    public float SurvivalTimeBank { get; set; }

    public int RerollsUsed { get; set; }
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

    [JsonPropertyName("stock")]
    public int Stock { get; set; } = -1;

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("contents")]
    public List<ShopBundleEntry>? Contents { get; set; }

    [JsonPropertyName("barterCost")]
    public List<ShopBundleEntry>? BarterCost { get; set; }

    [JsonPropertyName("restockHours")]
    public double? RestockHours { get; set; }

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
