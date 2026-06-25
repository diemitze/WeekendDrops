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

// Backbone for the Contracts feature: load contract defs, track the player's accepted
// contract, and pay out on completion. The actual boss spawn is injected at raid start
// by ContractSpawnPatch, which asks this service for the active contract.
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
    private readonly object _fileLock = new();

    // sessionId -> active contract id, so the per-bot loot hook doesn't re-read the
    // state file for every bot. Updated by accept/abandon/complete; seeded lazily.
    private readonly Dictionary<string, string?> _activeCache = [];

    // Sessions running the "real board" debug sim: the board behaves exactly as a normal
    // player sees it (3 sealed cards, single pick, cooldowns) even while debugMode is on,
    // so the live flow can be tested without losing the other debug helpers. Toggled by
    // the contract_realboard debug action; in-memory only.
    private readonly HashSet<string> _realBoardSim = [];

    private readonly string _configDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "config");

    private readonly string _dataDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data");

    // Mail expiry for a contract reward crate.
    private const double CrateExpiryHours = 72;

    public void LoadConfig()
    {
        _config = LoadJson<ContractsConfig>(SysPath.Combine(_configDir, "contracts.json")) ?? new ContractsConfig();

        // Share the mod's debug switch (config.json). In debug the board offers EVERY
        // contract and picks are unlimited, so all of them can be tested back to back.
        _debug = (LoadJson<ModConfig>(SysPath.Combine(_configDir, "config.json")) ?? new ModConfig()).DebugMode;

        Directory.CreateDirectory(_dataDir);
        logger.Info($"[WeekendDrops] Loaded {_config.Contracts.Count} contract(s){(_debug ? " (DEBUG: all offered, unlimited picks)" : "")}");
    }

    // Client state

    // Whether the board should use its debug presentation (all contracts, unsealed,
    // unlimited picks) for this session. True only when debugMode is on AND the session
    // isn't running the real-board sim - so a tester can opt back into the live flow.
    private bool BoardDebug(MongoId sessionId) =>
        _debug && !_realBoardSim.Contains(sessionId.ToString());

    public ContractsStateDto GetContractsState(MongoId sessionId)
    {
        var state = LoadState(sessionId);
        if (EnsureBoard(state)) SaveState(sessionId, state);

        bool boardDebug = BoardDebug(sessionId);

        // Debug offers every contract (so each can be tested); normally just the board's
        // three, in offer order.
        var offeredIds = boardDebug ? _config.Contracts.Select(c => c.Id) : state.OfferedContractIds;
        var offered = offeredIds
            .Select(id => _config.Contracts.FirstOrDefault(c => c.Id == id))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        // Countdown to the next board, shown only while the player is actually waiting
        // (no cards on offer). Once a board is up, or a contract is active, there's nothing
        // to count down to. Debug always has cards, so it never shows a countdown.
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

                // Seal every card the player hasn't accepted: the target and map are
                // withheld until they commit a pick. Only GP and the contract type leak.
                // Debug shows everything (unsealed + resolved) so each can be tested.
                if (!boardDebug && !isActive)
                    return SealedDto(c, CooldownSecondsLeft(state, c));

                // Reveal the rolled boss (name / map / objective role) only on the
                // contract the player has actually accepted.
                var d = isActive ? Resolve(c, state) : c;
                return new ContractDto
                {
                    Id          = d.Id,
                    Name        = d.Name,
                    Description = d.Description,
                    Map         = d.Map,
                    ObjectiveText  = d.ObjectiveText,
                    Flavor      = d.Flavor,
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
                };
            }).ToList()
        };
    }

    // A redacted card for an un-accepted contract: GP reward and contract type stay
    // visible (so the player can weigh payout and Bounty-vs-Supply-Run), but who the
    // target is and which map it's on are withheld until acceptance. Built here, server
    // side, so the real values never reach an un-accepted client.
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

    // Accept / abandon

    public string AcceptContract(MongoId sessionId, string contractId)
    {
        var def = _config.Contracts.FirstOrDefault(c => c.Id == contractId);
        if (def is null) return "contract_not_found";

        var state = LoadState(sessionId);
        EnsureBoard(state);

        // Re-accepting the contract you already picked is a harmless no-op.
        if (state.ActiveContractId == contractId) return "ok";

        // You get one pick from the board per period. Spending it (accept) or burning it
        // (abandon) locks the board until it refreshes - so it can't be farmed or fished.
        // Debug skips all of this so every contract can be picked (and switched) freely;
        // the real-board sim re-enforces it for the session that opted in.
        if (!BoardDebug(sessionId))
        {
            if (state.PickConsumed) return "pick_used";
            if (!state.OfferedContractIds.Contains(contractId)) return "not_offered";
            if (CooldownSecondsLeft(state, def) > 0) return "on_cooldown";
        }

        // Fresh pick: clear any roll left over from a previous contract (matters when
        // debug-switching from one active contract straight to another).
        ClearRolled(state);

        // Roll the boss once, here, and lock it in. Everything downstream (the forced
        // spawn, the kill objective, the card) reads this single choice, so server and
        // client never disagree about which boss this contract is for.
        if (def.BossPool.Count > 0)
        {
            var boss = def.BossPool[Random.Shared.Next(def.BossPool.Count)];
            state.ChosenBossKey = boss.Key;
            state.ChosenMap = boss.Maps.Count > 0
                ? boss.Maps[Random.Shared.Next(boss.Maps.Count)].Map
                : null;
        }

        // Roll a Supply Run landing site (zone + coordinate) once, so the crew spawn and
        // the client crate-relocate always agree, and the drop varies run to run.
        if (def.TriggerAirdrop && def.AirdropSpots.Count > 0 && string.IsNullOrEmpty(state.ChosenAirdropZone))
        {
            var spot = def.AirdropSpots[Random.Shared.Next(def.AirdropSpots.Count)];
            state.ChosenAirdropZone = spot.BossZone;
            state.ChosenAirdropX = spot.X;
            state.ChosenAirdropY = spot.Y;
            state.ChosenAirdropZ = spot.Z;
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
        // Backing out spends the whole board: no crew spawns, the offer is cleared, and the
        // next board is days away (not pickable again until then). This is the "I died three
        // times, I want out" escape hatch - and it stops the player from bailing on a blind
        // pick just to fish for a better one. (Debug ignores the schedule, so testers keep
        // picking freely.)
        ScheduleNextBoard(state);
        SaveState(sessionId, state);
        _activeCache[sessionId.ToString()] = null;
        return "ok";
    }

    // Clears the per-contract roll (boss/map/airdrop site). Does NOT touch the board or
    // the consumed-pick flag.
    private static void ClearRolled(PlayerContractState state)
    {
        state.ChosenBossKey = null;
        state.ChosenMap = null;
        state.ChosenAirdropZone = null;
        state.ChosenAirdropX = null;
        state.ChosenAirdropY = null;
        state.ChosenAirdropZ = null;
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
            // Force-complete the active contract (pay GP + crate) without needing the raid,
            // to test the reward path.
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

            // Toggle the real-board sim: present the board exactly as a player sees it
            // (sealed cards, 3-card roll, single pick, cooldowns) while debugMode stays
            // on for everything else. Turning it on wipes contract state to a clean slate
            // so a fresh real board rolls on the next fetch; turning it off restores the
            // "all offered, unlimited picks" debug board.
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

            // Wipe contract state to a clean slate: no active contract, fresh board, no
            // cooldowns - so testing can start over.
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

    // The contract whose target map matches this raid, if the player has one active.
    public ContractDefinition? GetActiveContractForMap(MongoId sessionId, string location)
    {
        var state = LoadState(sessionId);
        if (string.IsNullOrEmpty(state.ActiveContractId)) return null;

        var def = _config.Contracts.FirstOrDefault(c => c.Id == state.ActiveContractId);
        if (def is null) return null;
        var resolved = Resolve(def, state);
        return LocationUtil.Matches(location, resolved.Map) ? resolved : null;
    }

    // Resolves a config contract into the definition for THIS player's accepted state:
    // applies a randomized boss contract's rolled boss/map, then overlays a Supply Run's
    // rolled landing site. Non-rolled contracts pass through unchanged.
    private static ContractDefinition Resolve(ContractDefinition def, PlayerContractState state)
    {
        var result = ResolveBoss(def, state);

        // Supply Run: overlay the landing site rolled at accept (crew zone + crate coord).
        if (def.TriggerAirdrop && !string.IsNullOrEmpty(state.ChosenAirdropZone))
            result = ApplyAirdropSpot(result, state);

        return result;
    }

    // Returns a copy of `def` with the rolled Supply Run landing site applied: the crew's
    // spawn zone and the crate coordinate both point at the chosen spot. Cloned so the
    // shared config object is never mutated.
    private static ContractDefinition ApplyAirdropSpot(ContractDefinition def, PlayerContractState state)
    {
        var groups = def.Groups
            .Select(g => new ContractGroup
            {
                BossName         = g.BossName,
                BossDifficulty   = g.BossDifficulty,
                EscortType       = g.EscortType,
                EscortAmount     = g.EscortAmount,
                EscortDifficulty = g.EscortDifficulty,
                BossZone         = state.ChosenAirdropZone ?? g.BossZone,
                HostileToPlayer  = g.HostileToPlayer,
            })
            .ToList();

        return new ContractDefinition
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
            AirdropPosition = new Vec3
            {
                X = state.ChosenAirdropX ?? 0f,
                Y = state.ChosenAirdropY ?? 0f,
                Z = state.ChosenAirdropZ ?? 0f,
            },
            AirdropSpots    = def.AirdropSpots,
        };
    }

    // Applies a randomized boss contract's rolled choice (the original Resolve body).
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
        // Finishing a contract spends the board too: the next offer is days out, so contracts
        // stay an occasional event rather than a daily grind.
        ScheduleNextBoard(state);
        SaveState(sessionId, state);
        _activeCache[sessionId.ToString()] = null;

        logger.Info($"[WeekendDrops] Contract complete: {def.Name} (+{def.GpReward} GP) by {sessionId}");
        return "ok";
    }

    // Bot loot hook (called by ContractBotLootPatch)

    // The active contract regardless of map (used by the per-bot loot hook).
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

    // Bonus item tpls to force onto a bot of the given role, if it belongs to the
    // active contract's spawn groups. Empty when there's nothing to add.
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

    // How many contracts are offered at once. The player picks exactly one.
    private const int BoardSize = 3;

    // Ensures the player has a board when one is due. Contracts are NOT daily: a fresh
    // board appears only once UtcNow has reached NextBoardAtUtc (pushed several days out
    // each time a board is spent). A live, unspent board is left alone, and an in-progress
    // contract is never disturbed. Returns true if anything changed (so the caller saves).
    private bool EnsureBoard(PlayerContractState state)
    {
        // An accepted, in-progress contract owns the board until it's completed.
        if (!string.IsNullOrEmpty(state.ActiveContractId)) return false;

        // A live board the player hasn't spent their pick on yet: keep it waiting for them
        // (no expiry - it sits there until they accept or abandon).
        if (state.OfferedContractIds.Count > 0 && !state.PickConsumed) return false;

        // No live board. A new one only appears once the scheduled time arrives. A brand
        // new player (NextBoardAtUtc null) gets one immediately so the feature is visible.
        if (state.NextBoardAtUtc is DateTime due && DateTime.UtcNow < due) return false;

        RollBoard(state);
        return true;
    }

    // Rolls a fresh offer of BoardSize distinct contracts. Contracts on cooldown are
    // skipped; if that leaves too few to fill the board, the full pool is used so the
    // board is never short. The NEXT board isn't scheduled here - that happens when this
    // one is spent (ScheduleNextBoard, on complete/abandon).
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

    // Spends the current board: clears the offer and schedules the next board a random
    // whole number of UTC days out (BoardMinDays..BoardMaxDays). Called when a contract is
    // completed or abandoned, so the player goes a few quiet days before the next offer.
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

    // Tolerant load (see the challenge services): a malformed or half-written file
    // falls back to default instead of throwing, so it can't brick the mod.
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
