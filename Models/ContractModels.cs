using System.Text.Json.Serialization;

namespace WeekendDrops.Models;

// One spawn group: a leader (BossName) plus its escorts, dropped into a zone. A boss
// contract has one group; an event ("clean-out") contract has several roaming groups.
// Phase 1 uses existing EFT WildSpawnType ids (e.g. bossKnight, exUsec, pmcBot); Phase 2
// swaps these for a custom type. Raiders (pmcBot) and Rogues (exUsec) spawn fine through
// the boss pipeline and come geared, which is what the event style wants.
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

    // When true, the group's role is hostile to the player only and neutral to all AI
    // (scavs, bosses, AI PMCs). This keeps the Cleanup Crew anchored to its spawn zone -
    // it has nothing to chase across the map, so it holds position and only engages when
    // the player shows up. Leave false for boss bounties (a boss keeps its vanilla
    // relations to its own guards).
    [JsonPropertyName("hostileToPlayer")]
    public bool HostileToPlayer { get; set; }
}

// A world position (x,y,z). Used for the Supply Run airdrop landing point - the crate
// keeps its own drop altitude, so only x/z are honoured (y is informational).
public class Vec3
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }
}

// One candidate Supply Run landing site: a spawn zone for the crew paired with the world
// coordinate the crate is steered to. AcceptContract rolls one of these so the drop lands
// somewhere different each run instead of always the same spot.
public class AirdropSpot
{
    // Spawn zone the crew is dropped into (a single BotZoneName near the coordinate).
    [JsonPropertyName("bossZone")]
    public string BossZone { get; set; } = "";

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }
}

// One map a boss can be hunted on, with the zone(s) it spawns at there.
public class BossSpawnMap
{
    // Location id, matched via LocationUtil (e.g. customs, woods, reserve, shoreline).
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    // Spawn zone(s), comma-separated (EFT picks one at random). Blank = map default.
    [JsonPropertyName("bossZone")]
    public string BossZone { get; set; } = "";
}

// One entry in a randomized boss contract's pool. On accept the service rolls one of
// these AND one of its Maps, then locks both in for the duration of the contract, so the
// forced spawn and the client-side kill objective always refer to the same boss on the
// same map. Each boss carries its own map pool (incl. its lore home map).
public class BossOption
{
    // Stable id stored in the player's state once rolled (survives config reordering).
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    // Name shown on the card after the roll, e.g. "Reshala".
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    // WildSpawnType of the boss and its guards (e.g. bossBully / followerBully).
    [JsonPropertyName("bossName")]
    public string BossName { get; set; } = "";

    [JsonPropertyName("escortType")]
    public string EscortType { get; set; } = "";

    [JsonPropertyName("escortAmount")]
    public string EscortAmount { get; set; } = "0";

    // The maps this boss can be hunted on; one is rolled at accept.
    [JsonPropertyName("maps")]
    public List<BossSpawnMap> Maps { get; set; } = [];

    // Role whose death completes the objective; usually equals BossName.
    [JsonPropertyName("objectiveRole")]
    public string ObjectiveRole { get; set; } = "";

    // Short in-character line shown as a "transmission" popup when this boss is rolled and
    // accepted. Supports {map} (the rolled map's pretty name). The speaker is DisplayName.
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

    // Target map (location id), matched against the raid via LocationUtil.
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    // What spawns. One group = boss contract; several = roaming event. Ignored when
    // BossPool is set (the rolled boss supplies the group instead).
    [JsonPropertyName("groups")]
    public List<ContractGroup> Groups { get; set; } = [];

    // When non-empty, this is a randomized boss bounty: accepting rolls one of these and
    // locks it in. The rolled option supplies the map, spawn group and objective role, so
    // Groups / Map / ObjectiveRoles below act only as the pre-roll (menu) placeholders.
    [JsonPropertyName("bossPool")]
    public List<BossOption> BossPool { get; set; } = [];

    // WildSpawnType roles whose deaths count toward the objective (e.g. ["bossKnight"]
    // for a boss kill, ["exUsec"] for clearing a Rogue crew).
    [JsonPropertyName("objectiveRoles")]
    public List<string> ObjectiveRoles { get; set; } = [];

    // How many of those roles must die to complete the contract.
    [JsonPropertyName("objectiveCount")]
    public int ObjectiveCount { get; set; } = 1;

    // Short objective line shown on the card, e.g. "Eliminate Knight" or "Clear out 8 Rogues".
    [JsonPropertyName("objectiveText")]
    public string ObjectiveText { get; set; } = "";

    // Flavour "intel" line shown on the card, e.g. "The Cleanup Crew have been spotted
    // on {map}." Supports {map} (resolved to the pretty map name client-side) and {boss}
    // (the rolled boss name, for pool contracts). Blank = no intel line.
    [JsonPropertyName("flavor")]
    public string Flavor { get; set; } = "";

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    // Short in-character line shown as a "transmission" popup the moment the contract is
    // accepted and revealed (the crew or target taunting the player). Supports {map}. For a
    // boss pool, the rolled BossOption.AcceptDialog overrides this. Blank = no popup.
    [JsonPropertyName("acceptDialog")]
    public string AcceptDialog { get; set; } = "";

    // Who's "speaking" the accept line (e.g. "DROPFALL CREW", "CLEANUP CREW"). Blank falls
    // back to the contract name client-side. Boss pools use the rolled boss's display name.
    [JsonPropertyName("dialogSpeaker")]
    public string DialogSpeaker { get; set; } = "";

    // Item tpls force-added to every bot this contract spawns (e.g. a LEDX so the
    // crew is worth looting). Foundation for the later gear layer; empty = none.
    [JsonPropertyName("bonusItems")]
    public List<string> BonusItems { get; set; } = [];

    // Optional crate mailed on completion (template id); blank = GP only.
    [JsonPropertyName("crateTemplateId")]
    public string CrateTemplateId { get; set; } = "";

    // When true, the objective only pays if the player also extracts alive.
    [JsonPropertyName("requireExtract")]
    public bool RequireExtract { get; set; }

    // Supply Run: when true the contract forces a guaranteed airdrop on its map and the
    // client relocates the crate to AirdropPosition so the Cleanup Crew (spawned at the
    // paired bossZone) guards the landing spot. Only valid on airdrop-capable maps
    // (woods, shoreline, lighthouse, customs, interchange, reserve - NOT factory/labs).
    [JsonPropertyName("triggerAirdrop")]
    public bool TriggerAirdrop { get; set; }

    // Where the forced airdrop should land (world x,z; the crate keeps its drop altitude,
    // so y is ignored). Pair this with the group's bossZone so the crew is on the crate.
    // Used as the single fixed site when AirdropSpots is empty.
    [JsonPropertyName("airdropPosition")]
    public Vec3? AirdropPosition { get; set; }

    // Candidate landing sites for a Supply Run. When non-empty, accepting rolls one and
    // locks it in (zone + coordinate) so the drop lands somewhere different each run. Each
    // spot's bossZone replaces the group's zone so the crew always spawns on the crate.
    [JsonPropertyName("airdropSpots")]
    public List<AirdropSpot> AirdropSpots { get; set; } = [];

    // Hours before this contract can be accepted again after completion.
    [JsonPropertyName("cooldownHours")]
    public double CooldownHours { get; set; } = 24;
}

public class ContractsConfig
{
    [JsonPropertyName("contracts")]
    public List<ContractDefinition> Contracts { get; set; } = [];

    // Contracts are NOT a daily thing. Once a board is used up (a contract completed or
    // the pick abandoned), the next board appears a random number of WHOLE UTC days later,
    // somewhere in [BoardMinDays, BoardMaxDays]. So the player goes a few dry days between
    // offers - that quiet stretch is intended, not a bug. Set both equal for a fixed gap.
    [JsonPropertyName("boardMinDays")]
    public int BoardMinDays { get; set; } = 2;

    [JsonPropertyName("boardMaxDays")]
    public int BoardMaxDays { get; set; } = 4;
}

// Per-session contract state (data/{sessionId}_contracts.json). One active contract at
// a time; CompletedAtUtc drives cooldowns.
public class PlayerContractState
{
    // The contract "board": a fixed offer of a few contracts. The player may activate
    // exactly ONE of them. Boards are NOT daily - a fresh one appears only once UtcNow has
    // reached NextBoardAtUtc, which is pushed several days out whenever a board is spent.
    // (BoardId is legacy from the old per-day cadence; kept so old state files still load.)
    public string? BoardId { get; set; }
    public List<string> OfferedContractIds { get; set; } = [];

    // When the next board may appear. Null = "evaluate now" (a brand-new player gets a
    // board immediately). While a board is live and unspent this sits in the past; it's
    // pushed to UtcNow + random(BoardMinDays..BoardMaxDays) days when the board is used up
    // (contract completed or pick abandoned), so offers are irregular rather than daily.
    public DateTime? NextBoardAtUtc { get; set; }

    // True once the player has spent their single pick this period - set on accept and
    // left set on abandon, so abandoning burns the pick (no re-picking a different offer
    // until the board refreshes). Reset only when a new board is rolled.
    public bool PickConsumed { get; set; }

    public string? ActiveContractId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }

    // For a randomized boss contract: the BossOption.Key and the map rolled when it was
    // accepted. Null for non-pool contracts or when nothing is active.
    public string? ChosenBossKey { get; set; }
    public string? ChosenMap { get; set; }

    // For a Supply Run with multiple AirdropSpots: the spot rolled at accept (zone +
    // coordinate), so the crew spawn and the client crate-relocate agree. Null otherwise.
    public string? ChosenAirdropZone { get; set; }
    public float? ChosenAirdropX { get; set; }
    public float? ChosenAirdropY { get; set; }
    public float? ChosenAirdropZ { get; set; }

    public Dictionary<string, DateTime> CompletedAtUtc { get; set; } = [];
}
