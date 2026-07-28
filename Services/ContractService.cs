using System.Text.Json;
using System.Text.Json.Serialization;
using SysPath = System.IO.Path;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using WeekendDrops.Models;

namespace WeekendDrops.Services;

// Loads contract defs, tracks the accepted contract, pays out on completion. The boss spawn
// itself is injected at raid start by ContractSpawnPatch.
[Injectable(InjectionType.Singleton)]
public class ContractService(
    MailSendService mailSendService,
    GpBalanceService gpBalance,
    ISptLogger<ContractService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private ContractsConfig _config = new();
    private bool _debug;                     // mirrors config.json debugMode

    public bool SuppressNativeBoss => _config.SuppressNativeBoss;

    public bool CrewSpawnOnPosts => _config.CrewSpawnOnPosts;
    private readonly object _fileLock = new();

    // sessionId -> active contract id, so the per-bot loot hook doesn't re-read the file.
    private readonly Dictionary<string, string?> _activeCache = [];

    // Sessions running the "real board" debug sim: live board rules despite debugMode.
    private readonly HashSet<string> _realBoardSim = [];

    private readonly string _configDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "config");

    private readonly string _dataDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data");

    private const double CrateExpiryHours = 72;

    public void LoadConfig()
    {
        _config = LoadJson<ContractsConfig>(SysPath.Combine(_configDir, "contracts.json")) ?? new ContractsConfig();

        _debug = (LoadJson<ModConfig>(SysPath.Combine(_configDir, "config.json")) ?? new ModConfig()).DebugMode;

        Directory.CreateDirectory(_dataDir);
        logger.Info($"[WeekendDrops] Loaded {_config.Contracts.Count} contract(s){(_debug ? " (DEBUG: all offered, unlimited picks)" : "")}");
    }

    // Client state

    // Debug presentation (all contracts, unsealed, unlimited picks), unless this session
    // opted back into the live flow via the real-board sim.
    private bool BoardDebug(MongoId sessionId) =>
        _debug && !_realBoardSim.Contains(sessionId.ToString());

    public ContractsStateDto GetContractsState(MongoId sessionId)
    {
        var state = LoadState(sessionId);
        if (EnsureBoard(state)) SaveState(sessionId, state);

        bool boardDebug = BoardDebug(sessionId);

        var offeredIds = boardDebug ? _config.Contracts.Select(c => c.Id) : state.OfferedContractIds;
        var offered = offeredIds
            .Select(id => _config.Contracts.FirstOrDefault(c => c.Id == id))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        double nextRefresh = 0;
        if (offered.Count == 0 && state.NextBoardAtUtc is DateTime next)
            nextRefresh = Math.Max(0, (next - DateTime.UtcNow).TotalSeconds);

        return new ContractsStateDto
        {
            ActiveContractId   = state.ActiveContractId ?? "",
            PickAvailable      = boardDebug || !state.PickConsumed,
            NextRefreshSeconds = nextRefresh,
            DebugMode          = boardDebug,
            Contracts = offered.Select(c =>
            {
                bool isActive = state.ActiveContractId == c.Id;

                if (!boardDebug && !isActive)
                    return SealedDto(c, CooldownSecondsLeft(state, c));

                var d = isActive ? Resolve(c, state) : c;
                return new ContractDto
                {
                    Id          = d.Id,
                    Name        = d.Name,
                    Description = d.Description,
                    Map         = d.Map,
                    ObjectiveText  = d.ObjectiveText,
                    Flavor      = d.Flavor,
                    // A repaired Supply Run's stored zone is stale; name the landing site.
                    Zone        = isActive
                        ? ((SupplyPostsNeedRepair(c, state) ? state.ChosenAirdropZone : state.ChosenHideoutZone)
                           ?? FirstZone(d))
                        : "",
                    ObjectiveRoles = d.ObjectiveRoles,
                    ObjectiveCount = d.ObjectiveCount,
                    GpReward    = d.GpReward,
                    Dialog        = d.AcceptDialog,
                    DialogSpeaker = string.IsNullOrEmpty(d.DialogSpeaker) ? d.Name : d.DialogSpeaker,
                    Active      = state.ActiveContractId == c.Id,
                    CooldownSeconds = CooldownSecondsLeft(state, c),
                    TriggerAirdrop  = d.TriggerAirdrop,
                    AirdropX        = d.AirdropPosition?.X ?? 0f,
                    AirdropY        = d.AirdropPosition?.Y ?? 0f,
                    AirdropZ        = d.AirdropPosition?.Z ?? 0f,
                    // Resolved, not the raw roll: client and server must agree on the posts.
                    HideoutPosts    = isActive ? d.ResolvedPosts : [],
                };
            }).ToList()
        };
    }

    // Built server-side, so the withheld values never reach the client at all.
    private static ContractDto SealedDto(ContractDefinition c, double cooldownSeconds)
    {
        bool supply = c.TriggerAirdrop;
        return new ContractDto
        {
            Id          = c.Id,
            Name        = supply ? "Sealed Supply Run" : "Sealed Bounty",
            Description = "",
            Map         = "",
            ObjectiveText = supply
                ? "Clear the crew and secure the drop"
                : "Accept to reveal the objective",
            Flavor = supply
                ? "A supply drop is inbound. Accept the contract to reveal the location."
                : "Target classified. Accept the contract to reveal who you're hunting, and where.",
            ObjectiveRoles  = [],
            ObjectiveCount  = 0,
            GpReward        = c.GpReward,           // payout stays visible
            Active          = false,
            CooldownSeconds = cooldownSeconds,
            Sealed          = true,
            TriggerAirdrop  = supply,               // lets the client label the type
            AirdropX = 0f, AirdropY = 0f, AirdropZ = 0f,   // coords withheld until accept
        };
    }

    private static string FirstZone(ContractDefinition d)
    {
        var g = d.Groups?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.BossZone));
        if (g is null) return "";
        var z = g.BossZone.Split(',')[0].Trim();
        return z;
    }


    public string AcceptContract(MongoId sessionId, string contractId)
    {
        var def = _config.Contracts.FirstOrDefault(c => c.Id == contractId);
        if (def is null) return "contract_not_found";

        var state = LoadState(sessionId);
        EnsureBoard(state);

            if (state.ActiveContractId == contractId) return "ok";

        // One pick per board period, so the board can't be farmed.
        if (!BoardDebug(sessionId))
        {
            if (state.PickConsumed) return "pick_used";
            if (!state.OfferedContractIds.Contains(contractId)) return "not_offered";
            if (CooldownSecondsLeft(state, def) > 0) return "on_cooldown";
        }

        ClearRolled(state);

        // Rolled once and locked in, so spawn, objective and card never disagree.
        if (def.BossPool.Count > 0)
        {
            var boss = def.BossPool[Random.Shared.Next(def.BossPool.Count)];
            state.ChosenBossKey = boss.Key;
            state.ChosenMap = boss.Maps.Count > 0
                ? boss.Maps[Random.Shared.Next(boss.Maps.Count)].Map
                : null;
        }

        // Landing site rolled once, so the crew spawn and the crate always agree.
        if (def.TriggerAirdrop && def.AirdropSpots.Count > 0 && string.IsNullOrEmpty(state.ChosenAirdropZone))
        {
            var spot = def.AirdropSpots[Random.Shared.Next(def.AirdropSpots.Count)];
            state.ChosenAirdropZone = spot.BossZone;
            state.ChosenAirdropX = spot.X;
            state.ChosenAirdropY = spot.Y;
            state.ChosenAirdropZ = spot.Z;
        }

        // Without this, SPT picks at random from a multi-zone list while the intel toast names
        // the first ("said Broken Village, crew was at RUAF").
        if (string.IsNullOrEmpty(state.ChosenAirdropZone))
        {
            string? zoneCsv = def.BossPool.Count > 0
                ? def.BossPool.FirstOrDefault(b => b.Key == state.ChosenBossKey)?
                      .Maps.FirstOrDefault(m => m.Map == state.ChosenMap)?.BossZone
                : def.Groups.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.BossZone))?.BossZone;
            state.ChosenBossZone = PickOneZone(zoneCsv);
        }

        // Crew posts. Boss bounties self-place, so they're skipped.
        if (def.BossPool.Count == 0)
        {
            // The crew guards the crate, so ring them around the rolled landing site.
            if (def.TriggerAirdrop && !string.IsNullOrEmpty(state.ChosenAirdropZone))
            {
                state.ChosenHideoutPosts = RingPosts(
                    state.ChosenAirdropX ?? 0f, state.ChosenAirdropY ?? 0f, state.ChosenAirdropZ ?? 0f,
                    CrewSize(def), _config.SupplyRingRadius);
                state.ChosenHideoutZone = state.ChosenAirdropZone;
            }
            // Crew bounty: a hideout from this map's pool, re-rolled per accept.
            else if (!def.TriggerAirdrop && _config.MapHideouts is { Count: > 0 })
            {
                var pool = _config.MapHideouts
                    .FirstOrDefault(kv => string.Equals(kv.Key, def.Map, StringComparison.OrdinalIgnoreCase))
                    .Value;
                if (pool is { Count: > 0 })
                {
                    var hideout = pool[Random.Shared.Next(pool.Count)];
                    if (hideout?.Posts is { Count: > 0 })
                    {
                        state.ChosenHideoutPosts = hideout.Posts;
                        state.ChosenHideoutZone = string.IsNullOrWhiteSpace(hideout.Zone) ? null : hideout.Zone;
                    }
                }
            }
        }

        state.ActiveContractId = contractId;
        state.AcceptedAtUtc = DateTime.UtcNow;
        state.PickConsumed = true;
        SaveState(sessionId, state);
        _activeCache[sessionId.ToString()] = contractId;

        var rolled = def.BossPool.Count > 0 ? $" -> {state.ChosenBossKey} @ {state.ChosenMap}" : "";
        logger.Info($"[WeekendDrops] Contract accepted: {def.Name}{rolled} by {sessionId}");
        return "ok";
    }

    public string AbandonContract(MongoId sessionId)
    {
        var state = LoadState(sessionId);
        state.ActiveContractId = null;
        state.AcceptedAtUtc = null;
        ClearRolled(state);
        // Backing out spends the whole board, so it can't be used to fish for a better pick.
        ScheduleNextBoard(state);
        SaveState(sessionId, state);
        _activeCache[sessionId.ToString()] = null;
        return "ok";
    }

    // Clears the per-contract roll only. Leaves the board and the consumed-pick flag alone.
    private static void ClearRolled(PlayerContractState state)
    {
        state.ChosenBossKey = null;
        state.ChosenMap = null;
        state.ChosenAirdropZone = null;
        state.ChosenAirdropX = null;
        state.ChosenAirdropY = null;
        state.ChosenAirdropZ = null;
        state.ChosenBossZone = null;
        state.ChosenHideoutPosts = null;
        state.ChosenHideoutZone = null;
    }

    private static int CrewSize(ContractDefinition def)
    {
        int n = 0;
        foreach (var g in def.Groups)
        {
            if (string.IsNullOrEmpty(g.BossName)) continue;
            n++;
            if (int.TryParse(g.EscortAmount, out int escorts) && escorts > 0) n += escorts;
        }
        return Math.Max(1, n);
    }

    // FNV-1a, not GetHashCode: string hashing is randomized per process, so the ring would move
    // every restart.
    private static int Seed(string contractId, PlayerContractState state)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in $"{contractId}|{state.ChosenAirdropX}|{state.ChosenAirdropZ}")
                h = (h ^ c) * 16777619;
            return (int)(h & 0x7FFFFFFF);
        }
    }

    // Radius is jittered so the posts don't read as a drawn circle. The client snaps them to
    // the navmesh; a seed makes the ring reproducible.
    private static List<Vec3> RingPosts(float x, float y, float z, int count, float radius, int? seed = null)
    {
        var rng = seed is null ? Random.Shared : new Random(seed.Value);
        var posts = new List<Vec3>(count);
        double start = rng.NextDouble() * Math.Tau;
        for (int i = 0; i < count; i++)
        {
            double angle = start + i * Math.Tau / count;
            double r = radius * (0.6 + rng.NextDouble() * 0.4);
            posts.Add(new Vec3
            {
                X = x + (float)(Math.Cos(angle) * r),
                Y = y,
                Z = z + (float)(Math.Sin(angle) * r),
            });
        }
        return posts;
    }

    private static string? PickOneZone(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var parts = csv.Split(',')
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToArray();
        return parts.Length == 0 ? null : parts[Random.Shared.Next(parts.Length)];
    }

    // Debug actions (gated by config.debugMode)

    public bool DebugAction(MongoId sessionId, string action)
    {
        if (!_debug)
        {
            logger.Warning("[WeekendDrops] Contract debug action ignored - debugMode is off");
            return false;
        }

        var state = LoadState(sessionId);

        switch (action?.ToLowerInvariant())
        {
            case "completeactive":
                if (string.IsNullOrEmpty(state.ActiveContractId)) return false;
                var rawDef = _config.Contracts.FirstOrDefault(c => c.Id == state.ActiveContractId);
                if (rawDef is null) return false;
                var def = Resolve(rawDef, state);
                if (def.GpReward > 0) gpBalance.Add(sessionId.ToString(), def.GpReward);
                if (!string.IsNullOrEmpty(def.CrateTemplateId))
                    mailSendService.SendSystemMessageToPlayer(
                        sessionId, $"Contract complete: {def.Name}",
                        BuildCrateReward(def.CrateTemplateId),
                        (long)TimeSpan.FromHours(CrateExpiryHours).TotalSeconds);
                state.CompletedAtUtc[def.Id] = DateTime.UtcNow;
                state.ActiveContractId = null;
                state.AcceptedAtUtc = null;
                ClearRolled(state);
                logger.Info($"[WeekendDrops] DEBUG force-completed contract '{def.Id}' (+{def.GpReward} GP)");
                break;

            // Toggle the real-board sim. On wipes contract state; off restores the debug board.
            case "realboard":
                var rsid = sessionId.ToString();
                if (_realBoardSim.Remove(rsid))
                {
                    logger.Info("[WeekendDrops] DEBUG real-board sim OFF (all contracts offered again)");
                    break;
                }
                _realBoardSim.Add(rsid);
                state.ActiveContractId = null;
                state.AcceptedAtUtc = null;
                state.PickConsumed = false;
                state.CompletedAtUtc.Clear();
                state.BoardId = null;
                state.OfferedContractIds = [];
                state.NextBoardAtUtc = null;     // forces a fresh real board on next fetch
                ClearRolled(state);
                logger.Info("[WeekendDrops] DEBUG real-board sim ON (sealed, 3 cards, single pick)");
                break;

            case "reset":
                state.ActiveContractId = null;
                state.AcceptedAtUtc = null;
                state.PickConsumed = false;
                state.CompletedAtUtc.Clear();
                state.BoardId = null;
                state.OfferedContractIds = [];
                state.NextBoardAtUtc = null;     // forces a fresh board on next fetch
                ClearRolled(state);
                logger.Info("[WeekendDrops] DEBUG contract state reset");
                break;

            default:
                logger.Warning($"[WeekendDrops] Unknown contract debug action '{action}'");
                return false;
        }

        SaveState(sessionId, state);
        _activeCache[sessionId.ToString()] = state.ActiveContractId;
        return true;
    }

    // Spawn lookup (called by ContractSpawnPatch)

    public ContractDefinition? GetActiveContractForMap(MongoId sessionId, string location)
    {
        var state = LoadState(sessionId);
        if (string.IsNullOrEmpty(state.ActiveContractId)) return null;

        var def = _config.Contracts.FirstOrDefault(c => c.Id == state.ActiveContractId);
        if (def is null) return null;
        var resolved = Resolve(def, state);
        return LocationUtil.Matches(location, resolved.Map) ? resolved : null;
    }

    // Under Fika the raid-generating session usually holds no contract, so fall back to the
    // first active one on this map from any profile. Solo SPT never reaches this.
    public ContractDefinition? GetAnyActiveContractForMap(string location)
    {
        if (!Directory.Exists(_dataDir)) return null;
        foreach (var path in Directory.EnumerateFiles(_dataDir, "*_contracts.json"))
        {
            var file = SysPath.GetFileName(path);
            var idStr = file[..^"_contracts.json".Length];
            MongoId sessionId;
            try { sessionId = new MongoId(idStr); } catch { continue; }

            var def = GetActiveContractForMap(sessionId, location);
            if (def != null) return def;
        }
        return null;
    }

    // Non-rolled contracts pass through unchanged.
    private ContractDefinition Resolve(ContractDefinition def, PlayerContractState state)
    {
        var result = ResolveBoss(def, state);

        if (def.TriggerAirdrop && !string.IsNullOrEmpty(state.ChosenAirdropZone))
            result = ApplyAirdropSpot(result, state);
        // Narrow the multi-zone list to the one rolled at accept. Cloned, never mutates config.
        else if (!string.IsNullOrEmpty(state.ChosenBossZone))
            result = ApplyChosenZone(result, state.ChosenBossZone!);

        // With nothing rolled, `result` is still the shared config object, so clone before
        // writing the posts ContractSpawnPatch reads.
        if (ReferenceEquals(result, def)) result = CloneWithGroups(def, def.Groups);
        result.ResolvedPosts = SupplyPostsNeedRepair(def, state)
            ? RingPosts(state.ChosenAirdropX ?? 0f, state.ChosenAirdropY ?? 0f, state.ChosenAirdropZ ?? 0f,
                        CrewSize(result), _config.SupplyRingRadius, Seed(def.Id, state))
            : state.ChosenHideoutPosts ?? [];
        return result;
    }

    // Older Supply Runs stored a hideout unrelated to the drop. Repaired on read, not at accept,
    // so a contract in flight fixes itself instead of costing the player their board pick.
    private static bool SupplyPostsNeedRepair(ContractDefinition def, PlayerContractState state)
        => def.TriggerAirdrop
           && !string.IsNullOrEmpty(state.ChosenAirdropZone)
           && !string.Equals(state.ChosenHideoutZone, state.ChosenAirdropZone, StringComparison.OrdinalIgnoreCase);

    // Copy with every group's BossZone forced to the rolled zone (blank stays blank).
    private static ContractDefinition ApplyChosenZone(ContractDefinition def, string zone) =>
        CloneWithGroups(def, def.Groups
            .Select(g => CloneGroup(g, string.IsNullOrWhiteSpace(g.BossZone) ? null : zone))
            .ToList());

    // Copy with the rolled landing site applied to both the crew zone and the crate coord.
    private static ContractDefinition ApplyAirdropSpot(ContractDefinition def, PlayerContractState state) =>
        CloneWithGroups(def,
            def.Groups.Select(g => CloneGroup(g, state.ChosenAirdropZone)).ToList(),
            new Vec3
            {
                X = state.ChosenAirdropX ?? 0f,
                Y = state.ChosenAirdropY ?? 0f,
                Z = state.ChosenAirdropZ ?? 0f,
            });

    // null override = keep the group's own zone.
    private static ContractGroup CloneGroup(ContractGroup g, string? zoneOverride) => new()
    {
        BossName         = g.BossName,
        BossDifficulty   = g.BossDifficulty,
        EscortType       = g.EscortType,
        EscortAmount     = g.EscortAmount,
        EscortDifficulty = g.EscortDifficulty,
        BossZone         = zoneOverride ?? g.BossZone,
        HostileToPlayer  = g.HostileToPlayer,
    };

    // Copy carrying the given groups; airdropPosition replaces the config coordinate when set.
    private static ContractDefinition CloneWithGroups(
        ContractDefinition def, List<ContractGroup> groups, Vec3? airdropPosition = null) => new()
    {
        Id              = def.Id,
        Name            = def.Name,
        Description     = def.Description,
        Map             = def.Map,
        Groups          = groups,
        BossPool        = def.BossPool,
        ObjectiveRoles  = def.ObjectiveRoles,
        ObjectiveCount  = def.ObjectiveCount,
        ObjectiveText   = def.ObjectiveText,
        Flavor          = def.Flavor,
        AcceptDialog    = def.AcceptDialog,
        DialogSpeaker   = def.DialogSpeaker,
        GpReward        = def.GpReward,
        BonusItems      = def.BonusItems,
        CrateTemplateId = def.CrateTemplateId,
        RequireExtract  = def.RequireExtract,
        CooldownHours   = def.CooldownHours,
        TriggerAirdrop  = def.TriggerAirdrop,
        AirdropPosition = airdropPosition ?? def.AirdropPosition,
        AirdropSpots    = def.AirdropSpots,
    };

    // Applies a pooled contract's rolled boss and map.
    private static ContractDefinition ResolveBoss(ContractDefinition def, PlayerContractState state)
    {
        if (def.BossPool.Count == 0) return def;

        var opt = def.BossPool.FirstOrDefault(o => o.Key == state.ChosenBossKey);
        if (opt is null) return def;

        // The map rolled alongside the boss (fall back to the boss's first map).
        var map = opt.Maps.FirstOrDefault(m => m.Map == state.ChosenMap)
                  ?? opt.Maps.FirstOrDefault()
                  ?? new BossSpawnMap();

        return new ContractDefinition
        {
            Id          = def.Id,
            Name        = $"Bounty: {opt.DisplayName}",
            Description = def.Description,
            Map         = map.Map,
            Groups =
            [
                new ContractGroup
                {
                    BossName         = opt.BossName,
                    BossDifficulty   = "normal",
                    EscortType       = opt.EscortType,
                    EscortAmount     = opt.EscortAmount,
                    EscortDifficulty = "normal",
                    BossZone         = map.BossZone,
                }
            ],
            ObjectiveRoles  = [string.IsNullOrEmpty(opt.ObjectiveRole) ? opt.BossName : opt.ObjectiveRole],
            ObjectiveCount  = 1,
            ObjectiveText   = $"Eliminate {opt.DisplayName}",
            Flavor          = $"{opt.DisplayName} has been spotted on {{map}}.",
            AcceptDialog    = opt.AcceptDialog,
            DialogSpeaker   = opt.DisplayName,
            GpReward        = def.GpReward,
            BonusItems      = def.BonusItems,
            CrateTemplateId = def.CrateTemplateId,
            RequireExtract  = def.RequireExtract,
            CooldownHours   = def.CooldownHours,
            BossPool        = def.BossPool,
            TriggerAirdrop  = def.TriggerAirdrop,
            AirdropPosition = def.AirdropPosition,
        };
    }

    // Completion

    public string CompleteContract(MongoId sessionId, ContractResultRequest r)
    {
        var rawDef = _config.Contracts.FirstOrDefault(c => c.Id == r.ContractId);
        if (rawDef is null) return "contract_not_found";

        var state = LoadState(sessionId);
        if (state.ActiveContractId != rawDef.Id)           return "not_active";

        var def = Resolve(rawDef, state);
        if (!LocationUtil.Matches(r.Location, def.Map))    return "wrong_map";
        if (def.RequireExtract && !r.Survived)             return "not_extracted";

        if (def.GpReward > 0)
            gpBalance.Add(sessionId.ToString(), def.GpReward);

        if (!string.IsNullOrEmpty(def.CrateTemplateId))
            mailSendService.SendSystemMessageToPlayer(
                sessionId,
                $"Contract complete: {def.Name}",
                BuildCrateReward(def.CrateTemplateId),
                (long)TimeSpan.FromHours(CrateExpiryHours).TotalSeconds);

        state.ActiveContractId = null;
        state.AcceptedAtUtc = null;
        ClearRolled(state);
        state.CompletedAtUtc[def.Id] = DateTime.UtcNow;
        // Finishing spends the board too, so contracts stay occasional.
        ScheduleNextBoard(state);
        SaveState(sessionId, state);
        _activeCache[sessionId.ToString()] = null;

        logger.Info($"[WeekendDrops] Contract complete: {def.Name} (+{def.GpReward} GP) by {sessionId}");
        return "ok";
    }

    // Bot loot hook (called by ContractBotLootPatch)

    // The active contract regardless of map.
    private ContractDefinition? GetActiveContract(MongoId sessionId)
    {
        var sid = sessionId.ToString();
        if (!_activeCache.TryGetValue(sid, out var activeId))
        {
            activeId = LoadState(sessionId).ActiveContractId;
            _activeCache[sid] = activeId;
        }
        return string.IsNullOrEmpty(activeId)
            ? null
            : _config.Contracts.FirstOrDefault(c => c.Id == activeId);
    }

    // Item tpls forced onto a bot of this role, if it belongs to the active contract.
    public List<string> BonusItemsForRole(MongoId sessionId, string role)
    {
        var def = GetActiveContract(sessionId);
        if (def is null || def.BonusItems.Count == 0 || string.IsNullOrEmpty(role)) return [];

        // A pooled boss contract carries its spawn group only after resolving the roll.
        if (def.BossPool.Count > 0)
            def = Resolve(def, LoadState(sessionId));

        bool isSpawnRole = def.Groups.Any(g =>
            string.Equals(g.BossName, role, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(g.EscortType, role, StringComparison.OrdinalIgnoreCase));
        return isSpawnRole ? def.BonusItems : [];
    }

    // Board (the offered set)

    // Offered at once; the player picks exactly one.
    private const int BoardSize = 3;

    // Rolls a board only once NextBoardAtUtc arrives. True if anything changed.
    private bool EnsureBoard(PlayerContractState state)
    {
        // An accepted, in-progress contract owns the board until it's completed.
        if (!string.IsNullOrEmpty(state.ActiveContractId)) return false;

        // An unspent board never expires; it waits until they accept or abandon.
        if (state.OfferedContractIds.Count > 0 && !state.PickConsumed) return false;

        // A brand new player (null) gets one immediately, so the feature is visible.
        if (state.NextBoardAtUtc is DateTime due && DateTime.UtcNow < due) return false;

        RollBoard(state);
        return true;
    }

    // BoardSize distinct contracts, skipping cooldowns (falls back to the full pool if too few).
    private void RollBoard(PlayerContractState state)
    {
        var available = _config.Contracts
            .Where(c => CooldownSecondsLeft(state, c) <= 0)
            .ToList();
        if (available.Count < BoardSize)
            available = _config.Contracts.ToList();

        state.OfferedContractIds = available
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Min(BoardSize, available.Count))
            .Select(c => c.Id)
            .ToList();

        state.BoardId          = DateTime.UtcNow.ToString("yyyy-MM-dd");
        state.PickConsumed     = false;
        state.ActiveContractId = null;
        state.AcceptedAtUtc    = null;
        ClearRolled(state);

        // The board is available right now; the next one is scheduled when this is spent.
        state.NextBoardAtUtc = DateTime.UtcNow;

        logger.Info($"[WeekendDrops] Contract board rolled: {string.Join(", ", state.OfferedContractIds)}");
    }

    // A random BoardMinDays..BoardMaxDays out, so there are quiet days between offers.
    private void ScheduleNextBoard(PlayerContractState state)
    {
        int min = Math.Max(0, _config.BoardMinDays);
        int max = Math.Max(min, _config.BoardMaxDays);
        int days = min == max ? min : Random.Shared.Next(min, max + 1);

        // Align to the start of that UTC day so the countdown reads in clean whole days.
        state.NextBoardAtUtc     = DateTime.UtcNow.Date.AddDays(Math.Max(1, days));
        state.OfferedContractIds = [];
        state.PickConsumed       = true;   // nothing to pick until the next board rolls
        logger.Info($"[WeekendDrops] Next contract board scheduled for {state.NextBoardAtUtc:yyyy-MM-dd} ({days}d)");
    }

    // Helpers

    private static double CooldownSecondsLeft(PlayerContractState state, ContractDefinition def)
    {
        if (def.CooldownHours <= 0) return 0;
        if (!state.CompletedAtUtc.TryGetValue(def.Id, out var done)) return 0;
        var ready = done.AddHours(def.CooldownHours);
        return ready > DateTime.UtcNow ? (ready - DateTime.UtcNow).TotalSeconds : 0;
    }

    private static List<Item> BuildCrateReward(string templateId) =>
    [
        new Item
        {
            Id       = new MongoId(),
            Template = new MongoId(templateId),
            ParentId = null,
            SlotId   = "main",
        }
    ];

    private string StatePath(MongoId sessionId) =>
        SysPath.Combine(_dataDir, $"{sessionId}_contracts.json");

    private PlayerContractState LoadState(MongoId sessionId)
    {
        lock (_fileLock)
        {
            var path = StatePath(sessionId);
            return (File.Exists(path) ? LoadJson<PlayerContractState>(path) : null)
                   ?? new PlayerContractState();
        }
    }

    private void SaveState(MongoId sessionId, PlayerContractState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        lock (_fileLock)
            File.WriteAllText(StatePath(sessionId), json);
    }

    // A malformed or half-written file falls back to default instead of throwing.
    private T? LoadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            logger.Error($"[WeekendDrops] Could not read {SysPath.GetFileName(path)}: {ex.Message}. Ignoring this file (using defaults).");
            return default;
        }
    }
}
