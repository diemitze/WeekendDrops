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

    // Localized weekend schedule, e.g. "Fri 18:00 to Mon 04:00" / "Fri 6:00 PM to Mon 4:00 AM".
    [JsonPropertyName("scheduleText")]
    public string ScheduleText { get; set; } = "";

    // True when the server is in debugMode. The client only shows the debug
    // controls (Reset/Complete) when this is set, since the server rejects those
    // actions otherwise - so the buttons aren't dead when debugMode is off.
    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; set; }

    // GP gifts sent to this player since their last /state poll. Drained on read
    // server-side, so each gift appears here exactly once; the client fires the
    // "gift received" toast for each. The GP itself is already in GpCoins above.
    [JsonPropertyName("pendingGifts")]
    public List<ReceivedGiftDto> PendingGifts { get; set; } = [];
}

// A GP gift the player just received (drives the receive toast). The coins are already
// reflected in the balance; this is only the announcement.
public class ReceivedGiftDto
{
    [JsonPropertyName("fromNickname")]
    public string FromNickname { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

// A profile the player can gift GP to (every other real profile on the server).
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

// One row of the Fika squad rivalry board: a real profile on the server with its GP
// standing this weekend. Balances/earnings are exposed here (unlike the gift picker).
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

// Client -> server: send `amount` GP to the profile `toId`.
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

    // GP paid for clearing the whole daily set (50% of the set's total GP).
    [JsonPropertyName("dailyBonusGp")]
    public int DailyBonusGp { get; set; }

    // True once the complete-all bonus has been collected today. Authoritative -
    // the client renders the button state from this so it survives a restart.
    [JsonPropertyName("dailyBonusClaimed")]
    public bool DailyBonusClaimed { get; set; }
}

public class StringIdRequest : IRequestData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

// Client pushes the F12 toggles the server can't infer. Carried in the request body, not the
// URL (SPT's router drops the query string). Sticky server-side.
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

    // Short objective line for the card (e.g. "Eliminate Knight", "Clear out 8 Rogues").
    [JsonPropertyName("objectiveText")]
    public string ObjectiveText { get; set; } = "";

    // Flavour "intel" line for the card (supports {map} substituted client-side).
    [JsonPropertyName("flavor")]
    public string Flavor { get; set; } = "";

    // Effective spawn zone of the active contract's target (resolved BossZone, e.g. "ZoneScavBase").
    // Populated on the accepted contract only; the client prettifies it for the intel toast.
    [JsonPropertyName("zone")]
    public string Zone { get; set; } = "";

    // WildSpawnType roles whose deaths count, and how many, so the client kill hook can
    // recognise the kills that complete the active contract.
    [JsonPropertyName("objectiveRoles")]
    public List<string> ObjectiveRoles { get; set; } = [];

    [JsonPropertyName("objectiveCount")]
    public int ObjectiveCount { get; set; } = 1;

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    // In-character "transmission" line shown when the contract is accepted/revealed (the
    // crew or boss taunting the player). Only sent on the active card. Supports {map}.
    [JsonPropertyName("dialog")]
    public string Dialog { get; set; } = "";

    [JsonPropertyName("dialogSpeaker")]
    public string DialogSpeaker { get; set; } = "";

    // True for the player's currently-accepted contract.
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    // True while the card is sealed: target and map withheld until accept. GP reward and type stay
    // visible; identifying fields are redacted server-side so the board can't be datamined.
    [JsonPropertyName("sealed")]
    public bool Sealed { get; set; }

    // Seconds until this contract can be accepted again (0 = available now).
    [JsonPropertyName("cooldownSeconds")]
    public double CooldownSeconds { get; set; }

    // Supply Run: when true the client forces the raid airdrop to land at (airdropX,
    // airdropZ) on this contract's map, so the crate drops on the Cleanup Crew.
    [JsonPropertyName("triggerAirdrop")]
    public bool TriggerAirdrop { get; set; }

    [JsonPropertyName("airdropX")]
    public float AirdropX { get; set; }

    [JsonPropertyName("airdropY")]
    public float AirdropY { get; set; }

    [JsonPropertyName("airdropZ")]
    public float AirdropZ { get; set; }
}

public class ContractsStateDto
{
    // The board: only the contracts currently offered this period (not the whole config
    // pool). The chosen one, if any, has Active = true.
    [JsonPropertyName("contracts")]
    public List<ContractDto> Contracts { get; set; } = [];

    // The currently-accepted contract id, or empty when none.
    [JsonPropertyName("activeContractId")]
    public string ActiveContractId { get; set; } = "";

    // False once the player has spent their pick for this period (accepted or abandoned).
    // The client uses this to grey out the accept buttons until the board refreshes.
    [JsonPropertyName("pickAvailable")]
    public bool PickAvailable { get; set; }

    // Seconds until the board re-rolls a fresh set of offers (matches the daily reset).
    [JsonPropertyName("nextRefreshSeconds")]
    public double NextRefreshSeconds { get; set; }

    // True when the server is in debugMode: the board lists every contract and picks are
    // unlimited, so the client lets any card be accepted/switched for testing.
    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; set; }
}

// Client tells the server a contract objective was met in raid (mirrors RaidResultRequest).
public class ContractResultRequest : IRequestData
{
    [JsonPropertyName("contractId")]
    public string ContractId { get; set; } = "";

    // Map the kill happened on, validated against the contract's target map.
    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    // True when the player extracted alive (for contracts with requireExtract).
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
