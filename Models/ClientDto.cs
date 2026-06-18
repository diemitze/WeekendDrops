using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace WeekendDrops.Models;

public class ChallengeDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    // ChallengeType name (e.g. "KillScavs") - lets the client map live in-raid
    // kills to the right challenge. Sent as a string so it survives the router's
    // serializer, which has no enum converter.
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("target")]
    public int Target { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

  
    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }
}

public class WeekendStateDto
{
    [JsonPropertyName("isWeekendActive")]
    public bool IsWeekendActive { get; set; }

    [JsonPropertyName("weekendId")]
    public string WeekendId { get; set; } = "";

    [JsonPropertyName("timeRemainingSeconds")]
    public double TimeRemainingSeconds { get; set; }

    [JsonPropertyName("challenges")]
    public List<ChallengeDto> Challenges { get; set; } = [];

    [JsonPropertyName("claimedTiers")]
    public List<int> ClaimedTiers { get; set; } = [];

    [JsonPropertyName("tierThresholds")]
    public List<int> TierThresholds { get; set; } = [];

 
    [JsonPropertyName("tierGpRewards")]
    public List<int> TierGpRewards { get; set; } = [];

    [JsonPropertyName("gpCoins")]
    public int GpCoins { get; set; }

    // Localized weekend schedule, e.g. "Fri 18:00 → Mon 04:00" / "Fri 6:00 PM → Mon 4:00 AM".
    [JsonPropertyName("scheduleText")]
    public string ScheduleText { get; set; } = "";

    // True when the server is in debugMode. The client only shows the debug
    // controls (Reset/Complete) when this is set, since the server rejects those
    // actions otherwise - so the buttons aren't dead when debugMode is off.
    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; set; }
}

public class DailyChallengeDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("target")]
    public int Target { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    [JsonPropertyName("rewardClaimed")]
    public bool RewardClaimed { get; set; }
}

public class ShopItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("gpCost")]
    public int GpCost { get; set; }

    [JsonPropertyName("stock")]
    public int Stock { get; set; }

    // Item used for the card icon (the container/representative item).
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = "";

    // When non-empty, the purchase delivers these items instead of TemplateId.
    [JsonPropertyName("contents")]
    public List<ShopContentDto> Contents { get; set; } = [];

    // Seconds until this item can be bought again (0 = available now).
    [JsonPropertyName("restockSeconds")]
    public double RestockSeconds { get; set; }
}

public class ShopContentDto
{
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class DailyStateDto
{
    [JsonPropertyName("challenges")]
    public List<DailyChallengeDto> Challenges { get; set; } = [];

    [JsonPropertyName("shopItems")]
    public List<ShopItemDto> ShopItems { get; set; } = [];

    [JsonPropertyName("nextResetSeconds")]
    public double NextResetSeconds { get; set; }

    // Seconds until the next global shop stock refill (0 = disabled).
    [JsonPropertyName("globalRestockSeconds")]
    public double GlobalRestockSeconds { get; set; }
}

public class StringIdRequest : IRequestData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}


public class RaidResultRequest : IRequestData
{

    [JsonPropertyName("raidId")]
    public string RaidId { get; set; } = "";

    [JsonPropertyName("scavKills")]
    public int ScavKills { get; set; }

    [JsonPropertyName("pmcKills")]
    public int PmcKills { get; set; }

    [JsonPropertyName("bossKills")]
    public int BossKills { get; set; }

    [JsonPropertyName("headshots")]
    public int Headshots { get; set; }

    [JsonPropertyName("grenadeKills")]
    public int GrenadeKills { get; set; }

    
    [JsonPropertyName("lootValue")]
    public int LootValue { get; set; }

    // True when the player extracted alive (ExitStatus.Survived / Runner).
    [JsonPropertyName("survived")]
    public bool Survived { get; set; }

    [JsonPropertyName("survivedSeconds")]
    public float SurvivedSeconds { get; set; }

    // Map id (GameWorld.LocationId), for ExtractFromLocation challenges.
    [JsonPropertyName("location")]
    public string Location { get; set; } = "";


    [JsonPropertyName("isScavRaid")]
    public bool IsScavRaid { get; set; }
}
