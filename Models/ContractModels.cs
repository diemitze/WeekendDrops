using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace WeekendDrops.Models;

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

    [JsonPropertyName("bossZone")]
    public string BossZone { get; set; } = "";

    [JsonPropertyName("hostileToPlayer")]
    public bool HostileToPlayer { get; set; }
}

public class Vec3
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }
}

public class AirdropSpot
{
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
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("bossZone")]
    public string BossZone { get; set; } = "";
}

public class BossOption
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("bossName")]
    public string BossName { get; set; } = "";

    [JsonPropertyName("escortType")]
    public string EscortType { get; set; } = "";

    [JsonPropertyName("escortAmount")]
    public string EscortAmount { get; set; } = "0";

    [JsonPropertyName("maps")]
    public List<BossSpawnMap> Maps { get; set; } = [];

    [JsonPropertyName("objectiveRole")]
    public string ObjectiveRole { get; set; } = "";

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

    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("groups")]
    public List<ContractGroup> Groups { get; set; } = [];

    [JsonPropertyName("bossPool")]
    public List<BossOption> BossPool { get; set; } = [];

    [JsonPropertyName("objectiveRoles")]
    public List<string> ObjectiveRoles { get; set; } = [];

    [JsonPropertyName("objectiveCount")]
    public int ObjectiveCount { get; set; } = 1;

    [JsonPropertyName("objectiveText")]
    public string ObjectiveText { get; set; } = "";

    [JsonPropertyName("flavor")]
    public string Flavor { get; set; } = "";

    [JsonPropertyName("gpReward")]
    public int GpReward { get; set; }

    [JsonPropertyName("acceptDialog")]
    public string AcceptDialog { get; set; } = "";

    [JsonPropertyName("dialogSpeaker")]
    public string DialogSpeaker { get; set; } = "";

    [JsonPropertyName("bonusItems")]
    public List<string> BonusItems { get; set; } = [];

    [JsonPropertyName("crateTemplateId")]
    public string CrateTemplateId { get; set; } = "";

    [JsonPropertyName("requireExtract")]
    public bool RequireExtract { get; set; }

    [JsonPropertyName("triggerAirdrop")]
    public bool TriggerAirdrop { get; set; }

    [JsonPropertyName("airdropPosition")]
    public Vec3? AirdropPosition { get; set; }

    [JsonPropertyName("airdropSpots")]
    public List<AirdropSpot> AirdropSpots { get; set; } = [];

    [JsonPropertyName("cooldownHours")]
    public double CooldownHours { get; set; } = 24;

    [JsonIgnore]
    public List<Vec3> ResolvedPosts { get; set; } = [];
}

public class ContractsConfig
{
    [JsonPropertyName("contracts")]
    public List<ContractDefinition> Contracts { get; set; } = [];

    [JsonPropertyName("boardMinDays")]
    public int BoardMinDays { get; set; } = 2;

    [JsonPropertyName("boardMaxDays")]
    public int BoardMaxDays { get; set; } = 4;

    [JsonPropertyName("suppressNativeBoss")]
    public bool SuppressNativeBoss { get; set; }

    [JsonPropertyName("mapHideouts")]
    public Dictionary<string, List<Hideout>> MapHideouts { get; set; } = [];

    [JsonPropertyName("supplyRingRadius")]
    public float SupplyRingRadius { get; set; } = 16f;

    [JsonPropertyName("crewSpawnOnPosts")]
    public bool CrewSpawnOnPosts { get; set; } = true;

    [JsonPropertyName("maxContractsPerRaid")]
    public int MaxContractsPerRaid { get; set; } = 3;
}

public class Hideout
{
    [JsonPropertyName("zone")]
    public string Zone { get; set; } = "";

    [JsonPropertyName("posts")]
    public List<Vec3> Posts { get; set; } = [];
}

public class PlayerContractState
{
    public string? BoardId { get; set; }
    public List<string> OfferedContractIds { get; set; } = [];

    public DateTime? NextBoardAtUtc { get; set; }

    public bool PickConsumed { get; set; }

    public string? ActiveContractId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }

    public string? ChosenBossKey { get; set; }
    public string? ChosenMap { get; set; }

    public string? ChosenAirdropZone { get; set; }
    public float? ChosenAirdropX { get; set; }
    public float? ChosenAirdropY { get; set; }
    public float? ChosenAirdropZ { get; set; }

    public string? ChosenBossZone { get; set; }

    public List<Vec3>? ChosenHideoutPosts { get; set; }

    public string? ChosenHideoutZone { get; set; }

    public Dictionary<string, DateTime> CompletedAtUtc { get; set; } = [];
}
