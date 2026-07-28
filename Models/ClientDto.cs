using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace WeekendDrops.Models;

public class ChallengeDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    // Sent as a string, not the enum: the router's serializer has no enum converter.
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

    [JsonPropertyName("scheduleText")]
    public string ScheduleText { get; set; } = "";

    // Gates the client's debug controls, which the server would otherwise reject.
    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; set; }

    // Drained on read, so each gift appears exactly once. The GP is already in GpCoins.
    [JsonPropertyName("pendingGifts")]
    public List<ReceivedGiftDto> PendingGifts { get; set; } = [];
}

// The announcement only: the coins are already in the balance.
public class ReceivedGiftDto
{
    [JsonPropertyName("fromNickname")]
    public string FromNickname { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

public class GiftFriendDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";
}

public class FriendsStateDto
{
    [JsonPropertyName("friends")]
    public List<GiftFriendDto> Friends { get; set; } = [];
}

// Unlike the gift picker, this exposes balances and earnings.
public class SquadRowDto
{
    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("gpBalance")]
    public int GpBalance { get; set; }

    [JsonPropertyName("gpEarnedWeekend")]
    public int GpEarnedWeekend { get; set; }

    [JsonPropertyName("weeklyDone")]
    public int WeeklyDone { get; set; }

    [JsonPropertyName("weeklyTotal")]
    public int WeeklyTotal { get; set; }

    [JsonPropertyName("isYou")]
    public bool IsYou { get; set; }
}

public class SquadStateDto
{
    [JsonPropertyName("rows")]
    public List<SquadRowDto> Rows { get; set; } = [];
}

public class GiftRequest : IRequestData
{
    [JsonPropertyName("toId")]
    public string ToId { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
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

    // Drawn as the card icon.
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = "";

    // When non-empty, the purchase delivers these items instead of TemplateId.
    [JsonPropertyName("contents")]
    public List<ShopContentDto> Contents { get; set; } = [];

    // When non-empty, this entry is a Trade-in: hand these over to receive Contents.
    [JsonPropertyName("barterCost")]
    public List<ShopContentDto> BarterCost { get; set; } = [];

    // 0 = available now.
    [JsonPropertyName("restockSeconds")]
    public double RestockSeconds { get; set; }

    // Plays the full-screen ceremony on purchase instead of the toast.
    [JsonPropertyName("trophy")]
    public bool Trophy { get; set; }
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

    // 0 = disabled.
    [JsonPropertyName("globalRestockSeconds")]
    public double GlobalRestockSeconds { get; set; }

    // Paid for clearing the whole daily set: 50% of the set's total GP.
    [JsonPropertyName("dailyBonusGp")]
    public int DailyBonusGp { get; set; }

    // Authoritative, so the client's button state survives a restart.
    [JsonPropertyName("dailyBonusClaimed")]
    public bool DailyBonusClaimed { get; set; }
}

public class StringIdRequest : IRequestData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

// The F12 toggles the server can't infer. Carried in the body, since SPT's router drops the
// query string. Sticky server-side.
public class ClientFlagsRequest : IRequestData
{
    [JsonPropertyName("noScav")]
    public bool NoScav { get; set; }

    [JsonPropertyName("lootNet")]
    public bool LootNet { get; set; }
}

public class ContractDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("objectiveText")]
    public string ObjectiveText { get; set; } = "";

    // Supports a {map} token substituted client-side.
    [JsonPropertyName("flavor")]
    public string Flavor { get; set; } = "";

    // Resolved BossZone, e.g. "ZoneScavBase". Accepted contract only; the client prettifies it.
    [JsonPropertyName("zone")]
    public string Zone { get; set; } = "";

    // WildSpawnType roles the client kill hook matches against.
    [JsonPropertyName("objectiveRoles")]
    public List<string> ObjectiveRoles { get; set; } = [];

    [JsonPropertyName("objectiveCount")]
    public int ObjectiveCount { get; set; } = 1;

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    // Sent on the active card only. Supports a {map} token.
    [JsonPropertyName("dialog")]
    public string Dialog { get; set; } = "";

    [JsonPropertyName("dialogSpeaker")]
    public string DialogSpeaker { get; set; } = "";

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    // Identifying fields are redacted server-side, so a sealed board can't be datamined.
    // GP reward and type stay visible.
    [JsonPropertyName("sealed")]
    public bool Sealed { get; set; }

    // 0 = available now.
    [JsonPropertyName("cooldownSeconds")]
    public double CooldownSeconds { get; set; }

    // Supply Run: the client forces the raid airdrop to land at (airdropX, airdropZ).
    [JsonPropertyName("triggerAirdrop")]
    public bool TriggerAirdrop { get; set; }

    [JsonPropertyName("airdropX")]
    public float AirdropX { get; set; }

    [JsonPropertyName("airdropY")]
    public float AirdropY { get; set; }

    [JsonPropertyName("airdropZ")]
    public float AirdropZ { get; set; }

    // Active contract only. Empty = no hideout for this map, so a bossZone spawn.
    [JsonPropertyName("hideoutPosts")]
    public List<Vec3> HideoutPosts { get; set; } = [];
}

public class ContractsStateDto
{
    // Only what's offered this period, not the whole config pool.
    [JsonPropertyName("contracts")]
    public List<ContractDto> Contracts { get; set; } = [];

    [JsonPropertyName("activeContractId")]
    public string ActiveContractId { get; set; } = "";

    // False once the pick for this period is spent, by accepting or abandoning.
    [JsonPropertyName("pickAvailable")]
    public bool PickAvailable { get; set; }

    // Until the board re-rolls, which matches the daily reset.
    [JsonPropertyName("nextRefreshSeconds")]
    public double NextRefreshSeconds { get; set; }

    // In debugMode the board lists every contract and picks are unlimited.
    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; set; }
}

public class ContractResultRequest : IRequestData
{
    [JsonPropertyName("contractId")]
    public string ContractId { get; set; } = "";

    // Validated against the contract's target map.
    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    // Checked by contracts with requireExtract.
    [JsonPropertyName("survived")]
    public bool Survived { get; set; }
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

    [JsonPropertyName("cultistKills")]
    public int CultistKills { get; set; }

    [JsonPropertyName("priestKills")]
    public int PriestKills { get; set; }

    [JsonPropertyName("raiderKills")]
    public int RaiderKills { get; set; }

    [JsonPropertyName("rogueKills")]
    public int RogueKills { get; set; }

    [JsonPropertyName("meleeKills")]
    public int MeleeKills { get; set; }

    [JsonPropertyName("headshots")]
    public int Headshots { get; set; }

    [JsonPropertyName("grenadeKills")]
    public int GrenadeKills { get; set; }

    [JsonPropertyName("lootValue")]
    public int LootValue { get; set; }

    // ExitStatus.Survived or Runner.
    [JsonPropertyName("survived")]
    public bool Survived { get; set; }

    [JsonPropertyName("survivedSeconds")]
    public float SurvivedSeconds { get; set; }

    // GameWorld.LocationId, for ExtractFromLocation challenges.
    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("isScavRaid")]
    public bool IsScavRaid { get; set; }
}
