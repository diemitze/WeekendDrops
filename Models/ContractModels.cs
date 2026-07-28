using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

// A leader (BossName) plus escorts, dropped into a zone. Boss contracts have one group, event
// contracts several. Uses EFT WildSpawnType ids; Raiders/Rogues spawn geared via the boss pipeline.
public class ContractGroup
{
    [JsonPropertyName("bossName")]
    public string BossName { get; set; } = "";

    [JsonPropertyName("bossDifficulty")]
    public string BossDifficulty { get; set; } = "normal";

    [JsonPropertyName("escortType")]
    public string EscortType { get; set; } = "";

    [JsonPropertyName("escortAmount")]
    public string EscortAmount { get; set; } = "0";

    [JsonPropertyName("escortDifficulty")]
    public string EscortDifficulty { get; set; } = "normal";

    // Optional spawn zone; blank uses the map's default boss zones.
    [JsonPropertyName("bossZone")]
    public string BossZone { get; set; } = "";

    // Hostile to the player only, so the crew has nothing to chase and holds its zone. False
    // for boss bounties, which keep their vanilla relations to their guards.
    [JsonPropertyName("hostileToPlayer")]
    public bool HostileToPlayer { get; set; }
}

// As an airdrop landing point only x/z are honoured: the crate keeps its own drop altitude.
public class Vec3
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }
}

// One is rolled per accept, so the drop moves run to run.
public class AirdropSpot
{
    // A single BotZoneName near the coordinate.
    [JsonPropertyName("bossZone")]
    public string BossZone { get; set; } = "";

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }
}

public class BossSpawnMap
{
    // Location id, matched via LocationUtil.
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    // Spawn zone(s), comma-separated (EFT picks one at random). Blank = map default.
    [JsonPropertyName("bossZone")]
    public string BossZone { get; set; } = "";
}

// On accept, one of these plus one of its Maps is rolled and locked in, so the spawn and the
// kill objective always agree.
public class BossOption
{
    // Stored in the player's state once rolled, so it must survive config reordering.
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    // WildSpawnType of the boss and its guards (e.g. bossBully / followerBully).
    [JsonPropertyName("bossName")]
    public string BossName { get; set; } = "";

    [JsonPropertyName("escortType")]
    public string EscortType { get; set; } = "";

    [JsonPropertyName("escortAmount")]
    public string EscortAmount { get; set; } = "0";

    // One is rolled at accept.
    [JsonPropertyName("maps")]
    public List<BossSpawnMap> Maps { get; set; } = [];

    // Role whose death completes the objective; usually equals BossName.
    [JsonPropertyName("objectiveRole")]
    public string ObjectiveRole { get; set; } = "";

    // Supports {map}. The speaker is DisplayName.
    [JsonPropertyName("acceptDialog")]
    public string AcceptDialog { get; set; } = "";
}

public class ContractDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    // Location id, matched against the raid via LocationUtil.
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    // One group = boss contract, several = roaming event. Ignored when BossPool is set.
    [JsonPropertyName("groups")]
    public List<ContractGroup> Groups { get; set; } = [];

    // Non-empty = a randomized boss bounty, where the rolled option supplies map, group and
    // objective role. Groups / Map / ObjectiveRoles are then pre-roll placeholders only.
    [JsonPropertyName("bossPool")]
    public List<BossOption> BossPool { get; set; } = [];

    // WildSpawnType roles whose deaths count, e.g. ["bossKnight"] or ["exUsec"].
    [JsonPropertyName("objectiveRoles")]
    public List<string> ObjectiveRoles { get; set; } = [];

    [JsonPropertyName("objectiveCount")]
    public int ObjectiveCount { get; set; } = 1;

    // Shown on the card, e.g. "Eliminate Knight" or "Clear out 8 Rogues".
    [JsonPropertyName("objectiveText")]
    public string ObjectiveText { get; set; } = "";

    // Intel line on the card. Supports {map} and {boss}. Blank = no intel line.
    [JsonPropertyName("flavor")]
    public string Flavor { get; set; } = "";

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    // Supports {map}. Overridden by a rolled BossOption.AcceptDialog; blank = no popup.
    [JsonPropertyName("acceptDialog")]
    public string AcceptDialog { get; set; } = "";

    // Blank falls back to the contract name; boss pools use the rolled boss's display name.
    [JsonPropertyName("dialogSpeaker")]
    public string DialogSpeaker { get; set; } = "";

    // Item tpls force-added to every bot this contract spawns, so the crew is worth looting.
    [JsonPropertyName("bonusItems")]
    public List<string> BonusItems { get; set; } = [];

    // Optional crate mailed on completion (template id); blank = GP only.
    [JsonPropertyName("crateTemplateId")]
    public string CrateTemplateId { get; set; } = "";

    // When true, the objective only pays if the player also extracts alive.
    [JsonPropertyName("requireExtract")]
    public bool RequireExtract { get; set; }

    // Supply Run: forces an airdrop, which the client relocates to AirdropPosition.
    // Airdrop-capable maps only, so not factory or labs.
    [JsonPropertyName("triggerAirdrop")]
    public bool TriggerAirdrop { get; set; }

    // The single fixed site, used only when AirdropSpots is empty.
    [JsonPropertyName("airdropPosition")]
    public Vec3? AirdropPosition { get; set; }

    // One is rolled and locked in per accept, so the drop moves run to run. The spot's bossZone
    // replaces the group's, so the crew always spawns on the crate.
    [JsonPropertyName("airdropSpots")]
    public List<AirdropSpot> AirdropSpots { get; set; } = [];

    // Hours before this contract can be accepted again after completion.
    [JsonPropertyName("cooldownHours")]
    public double CooldownHours { get; set; } = 24;

    // Filled by Resolve, never by config. ContractSpawnPatch turns these into spawn points.
    [JsonIgnore]
    public List<Vec3> ResolvedPosts { get; set; } = [];
}

public class ContractsConfig
{
    [JsonPropertyName("contracts")]
    public List<ContractDefinition> Contracts { get; set; } = [];

    // Contracts are NOT daily. Once a board is spent, the next appears a random whole number
    // of UTC days later in [BoardMinDays, BoardMaxDays]. Set both equal for a fixed gap.
    [JsonPropertyName("boardMinDays")]
    public int BoardMinDays { get; set; } = 2;

    [JsonPropertyName("boardMaxDays")]
    public int BoardMaxDays { get; set; } = 4;

    // False gives a "two warlords" raid. Bounty zones sit away from native homes, so the
    // two don't stack.
    [JsonPropertyName("suppressNativeBoss")]
    public bool SuppressNativeBoss { get; set; }

    // Dead spots where no bot normally spawns, keyed by map id. One is rolled per accept;
    // a missing map falls back to a bossZone spawn.
    [JsonPropertyName("mapHideouts")]
    public Dictionary<string, List<Hideout>> MapHideouts { get; set; } = [];

    // Metres. Wide enough that the crew doesn't all spot the player at once.
    [JsonPropertyName("supplyRingRadius")]
    public float SupplyRingRadius { get; set; } = 16f;

    // Replaces the zone's bot spawn points for that raid. Off = vanilla zone spawn, which
    // drops the crew a few hundred metres out to walk in.
    [JsonPropertyName("crewSpawnOnPosts")]
    public bool CrewSpawnOnPosts { get; set; } = true;
}

// One post per crew member; a single post means they spread around it. `zone` is the real BSG
// zone nearest them, used only to name the "spotted near X" intel toast.
public class Hideout
{
    [JsonPropertyName("zone")]
    public string Zone { get; set; } = "";

    [JsonPropertyName("posts")]
    public List<Vec3> Posts { get; set; } = [];
}

// Per-session state in data/{sessionId}_contracts.json. One active contract at a time.
public class PlayerContractState
{
    // Legacy name from the old per-day board cadence, kept so old state files still load.
    public string? BoardId { get; set; }
    public List<string> OfferedContractIds { get; set; } = [];

    // Null = evaluate now, so new players get a board immediately. Sits in the past while one is
    // live; pushed out a random BoardMin..Max days when spent.
    public DateTime? NextBoardAtUtc { get; set; }

    // Left set on abandon too, so backing out burns the pick. Reset only on a new board.
    public bool PickConsumed { get; set; }

    public string? ActiveContractId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }

    // Null for non-pool contracts, or when nothing is active.
    public string? ChosenBossKey { get; set; }
    public string? ChosenMap { get; set; }

    // The AirdropSpot rolled at accept, so the crew spawn and the client's crate-relocate
    // agree. Null outside a multi-spot Supply Run.
    public string? ChosenAirdropZone { get; set; }
    public float? ChosenAirdropX { get; set; }
    public float? ChosenAirdropY { get; set; }
    public float? ChosenAirdropZ { get; set; }

    // Locks a multi-zone BossZone to one zone, or SPT picks at random while the intel toast
    // names the first. Null for Supply Runs and single-zone contracts.
    public string? ChosenBossZone { get; set; }

    // Null = no hideouts for this map, so fall back to a bossZone spawn.
    public List<Vec3>? ChosenHideoutPosts { get; set; }

    // Names the crew's real location in the intel toast. Null falls back to the spawn zone.
    public string? ChosenHideoutZone { get; set; }

    public Dictionary<string, DateTime> CompletedAtUtc { get; set; } = [];
}
