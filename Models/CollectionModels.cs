using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

public enum CollectionPerkKind
{
    None,
    ContractPick,
    DailyReroll,
    GpPercent,
    ShopStock,
}

public class CollectionMilestone
{
    [JsonPropertyName("required")]
    public int Required { get; set; }

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    [JsonPropertyName("perk")]
    public CollectionPerkKind Perk { get; set; } = CollectionPerkKind.None;

    [JsonPropertyName("perkValue")]
    public double PerkValue { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public class CollectionSetDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("itemIds")]
    public List<string> ItemIds { get; set; } = [];

    [JsonPropertyName("gpPerItem")]
    public int GpPerItem { get; set; } = 250;

    [JsonPropertyName("milestones")]
    public List<CollectionMilestone> Milestones { get; set; } = [];
}

public class CollectionConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("requireFoundInRaid")]
    public bool RequireFoundInRaid { get; set; } = true;

    [JsonPropertyName("wipeWithProfile")]
    public bool WipeWithProfile { get; set; } = true;

    [JsonPropertyName("prestigeGpPerPiece")]
    public int PrestigeGpPerPiece { get; set; } = 150;

    [JsonPropertyName("prestigeGpPerSet")]
    public int PrestigeGpPerSet { get; set; } = 1000;

    [JsonPropertyName("protectedQuestIds")]
    public List<string> ProtectedQuestIds { get; set; } = ["5c51aac186f77432ea65c552"];

    [JsonPropertyName("sets")]
    public List<CollectionSetDefinition> Sets { get; set; } = [];
}

public class PlayerCollectionState
{
    public List<string> Donated { get; set; } = [];

    public List<string> ClaimedMilestones { get; set; } = [];

    public string ProfileStamp { get; set; } = "";

    public int PendingPrestigeGp { get; set; }
    public int PendingPrestigePieces { get; set; }
    public int PendingPrestigeSets { get; set; }
}
