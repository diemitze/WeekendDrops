using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SysPath = System.IO.Path;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using WeekendDrops.Models;

namespace WeekendDrops.Services;

[Injectable(InjectionType.Singleton)]
public class WeekendChallengeService(
    ProfileHelper profileHelper,
    MailSendService mailSendService,
    ConfigServer configServer,
    ItemHelper itemHelper,
    GpBalanceService gpBalance,
    ISptLogger<WeekendChallengeService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private ModConfig _config = new();

    // Read-only view of the loaded config for the loader/patches.
    public ModConfig Config => _config;
    private List<ChallengeDefinition> _challengePool = [];   // effective pool (loot-value quests in/out)
    private List<ChallengeDefinition> _allChallenges = [];   // full pool as loaded
    private bool _lootNetActive;                             // LootNET bridge present (client-signalled)
    private bool _scavDisabled;                              // client F12 toggle: drop Scav-run challenges
    private DropsConfig _dropsConfig = new();
    private CratePoolsConfig _cratePools = new();
    private CratePoolsConfig _wttPools = new();
    // Paid GP-shop Arena crates - keyed directly by crate template id (not by tier).
    private CratePoolsConfig _arenaPools = new();

    private readonly string _dataDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data");

    private readonly string _configDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "config");

    public void LoadConfig()
    {
        _config = LoadJson<ModConfig>(SysPath.Combine(_configDir, "config.json")) ?? new ModConfig();

        // Always the real weekend pool. Debug forces the weekend on; the in-panel debug
        // buttons handle quick completes, so the old challenges_debug.json duplicate is gone.
        _allChallenges = LoadJson<List<ChallengeDefinition>>(SysPath.Combine(_configDir, "challenges.json")) ?? [];

        // Loot-value challenges need the LootNET bridge addon (it signals the client,
        // which tags its state requests). Seed from config as an optional force; the
        // client signal flips it on at runtime via SetLootNetActive.
        _lootNetActive = _config.IncludeLootNet;
        ApplyChallengePool();

        _dropsConfig = LoadJson<DropsConfig>(SysPath.Combine(_configDir, "drops.json")) ?? new DropsConfig();
        _cratePools = LoadJson<CratePoolsConfig>(SysPath.Combine(_configDir, "crate_pools.json")) ?? new CratePoolsConfig();
        _wttPools = LoadJson<CratePoolsConfig>(SysPath.Combine(_configDir, "crate_pools_wtt.json")) ?? new CratePoolsConfig();
        _arenaPools = LoadJson<CratePoolsConfig>(SysPath.Combine(_configDir, "arena_pools.json")) ?? new CratePoolsConfig();

        if (_config.DebugMode)
            logger.Warning("[WeekendDrops] DEBUG MODE active - weekend forced on");

        // The effective weekend plan (count + budget) is clamped to what the pool can
        // actually produce in ApplyChallengePool/RecomputeWeekendPlan, which logs when
        // it has to clamp. No separate "unreachable budget" warning is needed.
        Directory.CreateDirectory(_dataDir);
    }

    // Effective pool = all challenges, minus loot-value ones unless the LootNET bridge
    // is active (so non-LootNET players never get an unprogressable quest), and minus
    // Scav-run challenges when the player has Scav challenges disabled in config.
    private void ApplyChallengePool()
    {
        _challengePool = _allChallenges
            .Where(c => _lootNetActive || !c.RequiresLootNet)
            .Where(c => ScavEnabled || !ChallengeMetrics.IsScavOnly(c.Type))
            .ToList();
        RecomputeWeekendPlan();
    }

    // Effective weekend plan: challenge count and difficulty-point budget, clamped to
    // what the current pool can actually produce. The picker takes at most one
    // challenge per group, so the count can't exceed the number of distinct groups,
    // and the budget can't exceed the largest distinct-group total reachable at that
    // count. Keeps the configured values as upper bounds and never invents an
    // unreachable target (which is what drove the reassign-every-load reset loop).
    private int _planCount;
    private int _planBudget;

    private void RecomputeWeekendPlan()
    {
        int desired = Math.Max(1, _config.ChallengesPerWeekend);
        int groups = _challengePool.Select(c => ChallengeMetrics.Group(c.Type)).Distinct().Count();
        _planCount = Math.Min(desired, Math.Max(1, groups));

        var byDifficulty = _challengePool.GroupBy(c => c.Difficulty).ToDictionary(g => g.Key, g => g.ToList());
        bool Feasible(int b) =>
            DifficultyCompositions(_planCount, b).Any(comp => TryBuildComposition(comp, byDifficulty, _planCount, out _));

        // Largest reachable budget not exceeding the configured one; the minimum
        // possible total is _planCount (all easy). If even that floor is unreachable
        // (sparse pool), walk up to the smallest reachable total instead.
        int chosen = 0;
        for (int b = Math.Max(_planCount, _config.WeekendDifficultyBudget); b >= _planCount; b--)
            if (Feasible(b)) { chosen = b; break; }
        if (chosen == 0)
            for (int b = _planCount; b <= _planCount * 3; b++)
                if (Feasible(b)) { chosen = b; break; }
        _planBudget = chosen > 0 ? chosen : _config.WeekendDifficultyBudget;

        if (_planCount != desired || _planBudget != _config.WeekendDifficultyBudget)
            logger.Info(
                $"[WeekendDrops] Weekend plan clamped to {_planCount} challenges / {_planBudget} points " +
                $"(config asks {desired}/{_config.WeekendDifficultyBudget}) - limited by {groups} usable challenge groups.");
    }

    // Scav-run challenges are in the pool only when enabled in config AND the client
    // hasn't toggled them off (F12). Either source can suppress them.
    private bool ScavEnabled => _config.EnableScavChallenges && !_scavDisabled;

    // Called when a client state request carries the LootNET bridge tag. Sticky: once
    // on for this server run, stays on (avoids churn). Rebuilds the effective pool so
    // loot-value challenges become assignable.
    public void SetLootNetActive()
    {
        if (_lootNetActive) return;
        _lootNetActive = true;
        ApplyChallengePool();
        logger.Info("[WeekendDrops] LootNET bridge detected - loot-value challenges enabled.");
    }

    // Called when a client state request carries the no-Scav toggle tag. Sticky for
    // the server run (takes full effect on restart), mirroring the LootNET bridge.
    public void SetScavChallengesDisabled()
    {
        if (_scavDisabled) return;
        _scavDisabled = true;
        ApplyChallengePool();
        logger.Info("[WeekendDrops] Client toggle: Scav-run challenges disabled for this run.");
    }

    // Replace every Scav-run challenge in the set with another quest of the same
    // difficulty (so the weekend point budget holds), in place. The replacement comes
    // from a group not already in the set, preferring a PMC quest when the PMC slot is
    // free, matching the one-per-group variety of a fresh set. Other challenges and
    // their progress are untouched; the replacement starts fresh at 0/target, so a
    // previously-completed Scav challenge is genuinely re-tasked. Returns true if
    // anything was swapped.
    private bool ReplaceScavChallenges(PlayerWeekendState state)
    {
        if (!_scavDisabled) return false;

        var rng     = new Random();
        var usedIds = state.Challenges.Select(c => c.DefinitionId).ToHashSet();
        var usedGroups = state.Challenges
            .Select(c => _allChallenges.FirstOrDefault(d => d.Id == c.DefinitionId))
            .Where(d => d is not null && !ChallengeMetrics.IsScavOnly(d.Type))
            .Select(d => ChallengeMetrics.Group(d!.Type))
            .ToHashSet();

        bool changed = false;

        foreach (var cp in state.Challenges)
        {
            var def = _allChallenges.FirstOrDefault(d => d.Id == cp.DefinitionId);
            if (def is null || !ChallengeMetrics.IsScavOnly(def.Type)) continue;

            // Same difficulty keeps the budget; group not already used keeps variety;
            // prefer PMC when its slot is free.
            var sameDiff = _challengePool.Where(d => d.Difficulty == def.Difficulty
                                                  && !usedIds.Contains(d.Id)
                                                  && !usedGroups.Contains(ChallengeMetrics.Group(d.Type))).ToList();
            var pmc      = sameDiff.Where(d => ChallengeMetrics.Group(d.Type) == "pmc").ToList();
            var pickPool = pmc.Count > 0 ? pmc : sameDiff;
            // Fall back to any same-difficulty quest not already in the set, so the
            // budget still balances even if every fresh group is taken.
            if (pickPool.Count == 0)
                pickPool = _challengePool.Where(d => d.Difficulty == def.Difficulty
                                                  && !usedIds.Contains(d.Id)).ToList();
            if (pickPool.Count == 0) continue;   // leave it; the stale check will reassign

            var pick = pickPool[rng.Next(pickPool.Count)];

            usedIds.Remove(cp.DefinitionId);
            cp.DefinitionId = pick.Id;
            cp.Target       = pick.Target;
            cp.Current      = 0;
            cp.Definition   = pick;
            usedIds.Add(pick.Id);
            usedGroups.Add(ChallengeMetrics.Group(pick.Type));
            changed = true;
        }

        return changed;
    }

    // The drop crates handed out as rewards are vanilla "RandomLootContainer" items
    // (the Twitch Drops event crates). SPT ships no loot pool for them, so opening one
    // logs "no rewards found" and yields nothing. Inject a per-tier pool at load so each
    // crate actually spits out loot. Pools are weighted (see crate_pools.json) so high-tier
    // ammo etc. stays a rare lucky pull rather than a guaranteed drop.
    public void RegisterLootContainerPools()
    {
        if (_cratePools.Tiers.Count == 0)
        {
            logger.Warning("[WeekendDrops] crate_pools.json missing/empty - drop crates will be empty when opened");
            return;
        }

        var inventory = configServer.GetConfig<InventoryConfig>();
        var wttAdded = 0;
        var wttSkipped = 0;

        foreach (var tier in _dropsConfig.Tiers)
        {
            var tierKey = tier.RequiredChallenges.ToString(CultureInfo.InvariantCulture);

            if (!_cratePools.Tiers.TryGetValue(tierKey, out var poolDef) || poolDef.Pool.Count == 0)
            {
                logger.Warning($"[WeekendDrops] No crate pool defined for tier '{tier.TierName}' (req {tier.RequiredChallenges}) - its crates stay empty");
                continue;
            }

            var rewardTplPool = poolDef.Pool.ToDictionary(
                kv => new MongoId(kv.Key),
                kv => kv.Value);

            // Fold in WTT-ContentBackport items but only those actually present in
            // the DB, so this auto-skips entirely when the mod isn't installed
            if (_wttPools.Tiers.TryGetValue(tierKey, out var wttDef))
            {
                foreach (var (tpl, weight) in wttDef.Pool)
                {
                    var id = new MongoId(tpl);
                    if (itemHelper.GetItem(id).Key)
                    {
                        rewardTplPool[id] = weight;
                        wttAdded++;
                    }
                    else
                    {
                        wttSkipped++;
                    }
                }
            }

            foreach (var crateTpl in tier.Pools.SelectMany(p => p.ItemIds).Distinct())
            {
                var details = new RewardDetails
                {
                    RewardCount = poolDef.RewardCount,
                    FoundInRaid = _cratePools.FoundInRaid,
                    RewardTplPool = rewardTplPool,
                };
                inventory.RandomLootContainers[new MongoId(crateTpl)] = details;
                Patches.WdCrateRegistry.Register(details);   // scope the loot postfixes to our crates
            }
        }

        if (wttAdded > 0)
            logger.Info($"[WeekendDrops] WTT-ContentBackport detected - added {wttAdded} bonus item(s) to drop pools");
        else if (wttSkipped > 0)
            logger.Info($"[WeekendDrops] WTT-ContentBackport not installed - {wttSkipped} optional item(s) skipped");
    }

    public void RegisterArenaShopPools()
    {
        if (_arenaPools.Tiers.Count == 0)
        {
            logger.Info("[WeekendDrops] arena_pools.json missing/empty - paid Arena crates use vanilla loot");
            return;
        }

        var inventory = configServer.GetConfig<InventoryConfig>();
        var registered = 0;
        var skipped = 0;

        foreach (var (crateTpl, poolDef) in _arenaPools.Tiers)
        {
            if (poolDef.Pool.Count == 0) continue;

            var rewardTplPool = new Dictionary<MongoId, double>();
            foreach (var (tpl, weight) in poolDef.Pool)
            {
                var id = new MongoId(tpl);
                if (itemHelper.GetItem(id).Key) rewardTplPool[id] = weight;
                else skipped++;
            }
            if (rewardTplPool.Count == 0) continue;

            var details = new RewardDetails
            {
                RewardCount = poolDef.RewardCount,
                FoundInRaid = _arenaPools.FoundInRaid,
                RewardTplPool = rewardTplPool,
            };
            inventory.RandomLootContainers[new MongoId(crateTpl)] = details;
            Patches.WdCrateRegistry.Register(details);   // scope the loot postfixes to our crates
            registered++;
        }

        logger.Info($"[WeekendDrops] Registered loot for {registered} paid Arena crate(s)" +
            (skipped > 0 ? $" ({skipped} item(s) not in DB skipped)" : ""));
    }

    public int ApplyRaidResult(MongoId sessionId, RaidResultRequest r)
    {
        if (!_config.Enabled || !IsWeekendActive()) return 0;

        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return 0;

        var state = GetOrCreateState(sessionId, profile);

        // Ignore a raid we've already applied (duplicate/retried report).
        if (!string.IsNullOrEmpty(r.RaidId) && state.LastRaidId == r.RaidId)
        {
            logger.Info($"[WeekendDrops] Raid {r.RaidId} already applied - ignoring duplicate report");
            return 0;
        }
        state.LastRaidId = r.RaidId;

        // Tier GP is "earned" the raid its difficulty threshold is first crossed.
        int pointsBefore = CompletedDifficultyPoints(state);

        // Survival-time bank accumulates only on a surviving raid; death wipes it.
        if (r.Survived) state.SurvivalTimeBank += r.SurvivedSeconds;
        else            state.SurvivalTimeBank = 0;

        // Total kills this raid - used by the scav-run "any kill" quest and the
        // single-raid spike quests.
        int totalKills = r.ScavKills + r.PmcKills + r.BossKills;

        foreach (var cp in state.Challenges.Where(c => !c.Completed))
        {
            switch (cp.Definition?.Type)
            {
                case ChallengeType.KillScavs:            cp.Current += r.ScavKills; break;
                case ChallengeType.KillPMCs:             cp.Current += r.PmcKills;  break;
                case ChallengeType.KillBoss:             cp.Current += r.BossKills; break;
                case ChallengeType.KillHeadshots:        cp.Current += r.Headshots; break;
                case ChallengeType.GrenadeKills:         cp.Current += r.GrenadeKills; break;
                case ChallengeType.SurviveTimeCumulative: cp.Current = (int)state.SurvivalTimeBank; break;
                case ChallengeType.ExtractSuccessfully:  if (r.Survived) cp.Current += 1; break;
                case ChallengeType.ExtractFromLocation:
                    if (r.Survived && !string.IsNullOrEmpty(cp.Definition.TargetLocation)
                        && LocationUtil.Matches(r.Location, cp.Definition.TargetLocation))
                        cp.Current += 1;
                    break;

                case ChallengeType.KillPMCsSingleRaid:   if (r.PmcKills  >= cp.Target) cp.Current = cp.Target; break;
                case ChallengeType.KillScavsSingleRaid:  if (r.ScavKills >= cp.Target) cp.Current = cp.Target; break;

                case ChallengeType.ScavExtract:   if (r.IsScavRaid && r.Survived) cp.Current += 1; break;
                case ChallengeType.ScavRaidsDone: if (r.IsScavRaid)               cp.Current += 1; break;
                case ChallengeType.ScavKills:     if (r.IsScavRaid)               cp.Current += totalKills; break;
                case ChallengeType.ScavExtractFromLocation:
                    if (r.IsScavRaid && r.Survived && !string.IsNullOrEmpty(cp.Definition.TargetLocation)
                        && LocationUtil.Matches(r.Location, cp.Definition.TargetLocation))
                        cp.Current += 1;
                    break;

                // Loot-value quests (LootNET) - you only keep loot you extract.
                case ChallengeType.ExtractWithLootValue: if (r.Survived && r.LootValue >= cp.Target) cp.Current = cp.Target; break;
                case ChallengeType.LootValueCumulative:  if (r.Survived) cp.Current += r.LootValue; break;
            }
        }

        int pointsAfter = CompletedDifficultyPoints(state);
        int gpEarned = _dropsConfig.Tiers
            .Where(t => t.RequiredChallenges > pointsBefore && t.RequiredChallenges <= pointsAfter)
            .Sum(t => t.GpReward);
        if (gpEarned > 0)
            logger.Info($"[WeekendDrops] Weekly tier GP earned this raid: +{gpEarned} (points {pointsBefore} to {pointsAfter})");

        SaveState(sessionId, state);

        logger.Info($"[WeekendDrops] Weekly raid result applied (survived={r.Survived}, scavRaid={r.IsScavRaid}, " +
            $"scav={r.ScavKills} pmc={r.PmcKills} boss={r.BossKills} hs={r.Headshots} nade={r.GrenadeKills}) - " +
            string.Join(", ", state.Challenges.Select(c => $"{c.Definition?.Type}:{c.Current}/{c.Target}")));
        return gpEarned;
    }

    // Weekend window

    // Localised to the current culture, e.g. "Fri 18:00 to Mon 04:00" (de-DE) or
    // "Fri 6:00 PM to Mon 4:00 AM" (en-US).
    public string GetWeekendScheduleText()
    {
        var culture = CultureInfo.CurrentCulture;
        string Day(int d) => culture.DateTimeFormat.AbbreviatedDayNames[((d % 7) + 7) % 7];
        string Time(int h) => new TimeOnly(((h % 24) + 24) % 24, 0).ToString("t", culture);
        return $"{Day(_config.WeekendStartDay)} {Time(_config.WeekendStartHour)} to " +
               $"{Day(_config.WeekendEndDay)} {Time(_config.WeekendEndHour)}";
    }

    public bool IsWeekendActive()
    {
        if (_config.DebugMode) return true;

        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var hour = now.Hour;

        // Friday 18:00 to Monday 04:00
        bool afterStart = day > _config.WeekendStartDay
            || (day == _config.WeekendStartDay && hour >= _config.WeekendStartHour);

        bool beforeEnd = day < _config.WeekendEndDay
            || (day == _config.WeekendEndDay && hour < _config.WeekendEndHour);

        // Handles Fri to Sun (no week boundary crossing) and Sun to Mon
        return afterStart && (day != 0 || beforeEnd)  // Sun is 0, needs special handling
               || (day == 0)                            // whole Sunday is active
               || (day == _config.WeekendEndDay && hour < _config.WeekendEndHour);
    }

    // Uses local time to match IsWeekendActive.
    public string GetCurrentWeekendId()
    {
        var now = DateTime.Now;
        int daysSinceStart = (((int)now.DayOfWeek - _config.WeekendStartDay) % 7 + 7) % 7;
        var anchor = now.Date.AddDays(-daysSinceStart);
        return anchor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // State management

    private readonly object _fileLock = new();

    private PlayerWeekendState GetOrCreateState(MongoId sessionId, PmcData profile)
    {
        var path = StatePath(sessionId);
        PlayerWeekendState? state;

        lock (_fileLock)
            state = File.Exists(path) ? LoadJson<PlayerWeekendState>(path) : null;

        var currentWeekendId = GetCurrentWeekendId();

        // Scav disabled: swap any Scav-run challenges in the current set for PMC quests
        // of the same difficulty (so the budget holds), in place. Runs before the
        // staleness check so it doesn't trigger a full reassign of the whole set.
        if (state is not null && state.WeekendId == currentWeekendId && ReplaceScavChallenges(state))
        {
            SaveState(sessionId, state);
            logger.Info($"[WeekendDrops] Weekend Scav challenges replaced for {sessionId} (Scav challenges disabled)");
        }

        bool stale = false;
        if (state is not null && state.WeekendId == currentWeekendId)
        {
            bool poolChanged = state.Challenges.Any(c => _challengePool.All(d => d.Id != c.DefinitionId));
            // Reroll when the effective point budget changed (pool grew/shrank enough
            // to shift it, or config edited). Compare against the budget the set was
            // stamped with, not its raw total: a set keeps its stamp even if the picker
            // ever fell short, so this can never reroll every load (the old bug), and a
            // Scav-toggle salvage (ReplaceScavChallenges, same total) is left in place.
            bool planChanged = state.PlanBudget != _planBudget;
            stale = poolChanged || planChanged;
        }

        // New weekend (or stale set) - reset everything and pick fresh challenges
        if (state is null || state.WeekendId != currentWeekendId || stale)
        {
            if (stale) logger.Info($"[WeekendDrops] Reassigning weekend for {sessionId} - cached set was stale (pool or weekend plan changed)");
            state = new PlayerWeekendState { WeekendId = currentWeekendId };
            AssignChallenges(state);
            logger.Info($"[WeekendDrops] New weekend started for {sessionId} - assigned {state.Challenges.Count} challenges");

            SaveState(sessionId, state);
        }

        // Wire up Definition references (not stored in JSON)
        foreach (var cp in state.Challenges)
            cp.Definition = _challengePool.FirstOrDefault(d => d.Id == cp.DefinitionId);

        return state;
    }

    private void SaveState(MongoId sessionId, PlayerWeekendState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        lock (_fileLock)
            File.WriteAllText(StatePath(sessionId), json);
    }

    private string StatePath(MongoId sessionId) =>
        SysPath.Combine(_dataDir, $"{sessionId}.json");

    // Challenge selection

    private void AssignChallenges(PlayerWeekendState state)
    {
        var rng = new Random();
        // Use the effective plan (clamped to what the pool can actually produce), not
        // the raw config values, so the assigned set hits the budget and stays stable.
        int n = _planCount;
        int budget = _planBudget;

        var chosen = PickByDifficultyBudget(rng, n, budget) ?? PickWeighted(rng, n);

        state.Challenges = chosen.Select(d => new ChallengeProgress
        {
            DefinitionId = d.Id,
            Target = d.Target,
            Definition = d
        }).ToList();

        // Stamp the plan this set was built under so the staleness check can tell a
        // genuine plan change from a set that simply couldn't reach the target.
        state.PlanCount = _planCount;
        state.PlanBudget = _planBudget;

        int total = chosen.Sum(c => c.Difficulty);
        logger.Info($"[WeekendDrops] Assigned {chosen.Count} challenges totalling {total} difficulty points (budget {budget})");
    }

    private List<ChallengeDefinition>? PickByDifficultyBudget(Random rng, int n, int budget)
    {
        var byDifficulty = _challengePool
            .GroupBy(c => c.Difficulty)
            .ToDictionary(g => g.Key, g => g.OrderBy(_ => rng.Next()).ToList());

        foreach (var comp in DifficultyCompositions(n, budget).OrderBy(_ => rng.Next()))
        {
            if (TryBuildComposition(comp, byDifficulty, n, out var picked))
                return picked.OrderBy(_ => rng.Next()).ToList();
        }
        return null;
    }

    // Greedily fill a difficulty composition with distinct-group challenges.
    // Returns true only when a full n-challenge set was built, so a true result
    // is a sound witness that the budget is genuinely reachable right now.
    private static bool TryBuildComposition(
        Dictionary<int, int> comp,
        Dictionary<int, List<ChallengeDefinition>> byDifficulty,
        int n,
        out List<ChallengeDefinition> picked)
    {
        picked = new List<ChallengeDefinition>();
        var usedGroups = new HashSet<string>();
        foreach (var (diff, count) in comp)
        {
            if (!byDifficulty.TryGetValue(diff, out var avail)) return false;
            int need = count;
            foreach (var cand in avail)
            {
                if (need == 0) break;
                if (!usedGroups.Add(ChallengeMetrics.Group(cand.Type))) continue;  // group already taken
                picked.Add(cand);
                need--;
            }
            if (need > 0) return false;
        }
        return picked.Count == n;
    }

    private static IEnumerable<Dictionary<int, int>> DifficultyCompositions(int n, int budget)
    {
        for (int hard = 0; hard <= n; hard++)
            for (int med = 0; med <= n - hard; med++)
            {
                int easy = n - hard - med;
                if (easy * 1 + med * 2 + hard * 3 != budget) continue;

                var map = new Dictionary<int, int>();
                if (easy > 0) map[1] = easy;
                if (med  > 0) map[2] = med;
                if (hard > 0) map[3] = hard;
                yield return map;
            }
    }

    private List<ChallengeDefinition> PickWeighted(Random rng, int n) =>
        _challengePool
            .SelectMany(c => Enumerable.Repeat(c, Math.Max(1, 4 - c.Difficulty)))
            .OrderBy(_ => rng.Next())
            .DistinctBy(c => c.Id)
            .DistinctBy(c => ChallengeMetrics.Group(c.Type))
            .Take(n)
            .ToList();

    // Drop delivery

    // Manually claim a single tier's reward. Returns true only when the tier
    // was actually earned, not yet claimed, and the reward was mailed.
    public bool ClaimTier(MongoId sessionId, int requiredChallenges)
    {
        if (!_config.Enabled || !IsWeekendActive()) return false;

        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return false;

        // RequiredChallenges is now a difficulty-point threshold (e.g. 4/6/8), not a
        // challenge count - and doubles as the crate_pools key for this tier.
        var tier = _dropsConfig.Tiers.FirstOrDefault(t => t.RequiredChallenges == requiredChallenges);
        if (tier is null)
        {
            logger.Warning($"[WeekendDrops] Claim rejected - no tier requires {requiredChallenges} points");
            return false;
        }

        var state = GetOrCreateState(sessionId, profile);
        int completedPoints = CompletedDifficultyPoints(state);

        // Debug mode bypasses the completion gate so the claim flow (mail +
        // claimed state + notification) can be exercised from the UI.
        if (!_config.DebugMode && completedPoints < tier.RequiredChallenges)
        {
            logger.Warning($"[WeekendDrops] Claim rejected - {completedPoints}/{tier.RequiredChallenges} difficulty points done");
            return false;
        }
        if (state.ClaimedTiers.Contains(tier.RequiredChallenges))
        {
            logger.Warning($"[WeekendDrops] Claim rejected - '{tier.TierName}' already claimed");
            return false;
        }

        SendDropTier(sessionId, tier);
        // Tiers award GP (credited to the virtual balance) on top of the drop crate.
        if (tier.GpReward > 0)
            gpBalance.Add(sessionId.ToString(), tier.GpReward);
        state.ClaimedTiers.Add(tier.RequiredChallenges);
        SaveState(sessionId, state);

        int totalPoints = state.Challenges.Sum(c => c.Definition?.Difficulty ?? 0);
        logger.Info($"[WeekendDrops] Player {sessionId} claimed '{tier.TierName}' ({completedPoints}/{totalPoints} difficulty points done)");
        return true;
    }

    // Sum the difficulty of completed challenges - the metric tiers gate on.
    private static int CompletedDifficultyPoints(PlayerWeekendState state) =>
        state.Challenges.Where(c => c.Completed).Sum(c => c.Definition?.Difficulty ?? 0);

    // Debug helpers
    // Lets the UI exercise the claim / tier-reached flow without grinding raids.
    // Always gated behind debugMode so it can't be hit in a real profile.
    public bool DebugAction(MongoId sessionId, string? action)
    {
        if (!_config.DebugMode)
        {
            logger.Warning("[WeekendDrops] Debug action ignored - debugMode is off");
            return false;
        }

        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return false;

        var state = GetOrCreateState(sessionId, profile);

        switch (action?.ToLowerInvariant())
        {
            case "resetclaims":
                state.ClaimedTiers.Clear();
                break;
            case "resetprogress":
                state.ClaimedTiers.Clear();
                foreach (var c in state.Challenges) c.Current = 0;
                // Clear the survival-time bank so SurviveTimeCumulative resets too.
                state.SurvivalTimeBank = 0;
                break;
            case "reroll":
                // Fresh random pick from the pool (unlike resetprogress, which keeps
                // the same set). Clears claims + progress so it's a clean slate.
                state.ClaimedTiers.Clear();
                state.SurvivalTimeBank = 0;
                AssignChallenges(state);   // replaces Challenges; Current defaults to 0
                break;
            case "completeone":
                var next = state.Challenges.FirstOrDefault(c => !c.Completed);
                if (next is not null) next.Current = next.Target;
                break;
            case "completeall":
                foreach (var c in state.Challenges) c.Current = c.Target;
                break;
            default:
                logger.Warning($"[WeekendDrops] Unknown debug action '{action}'");
                return false;
        }

        SaveState(sessionId, state);
        logger.Info($"[WeekendDrops] Debug action '{action}' applied for {sessionId}");
        return true;
    }

    private void SendDropTier(MongoId sessionId, DropTier tier)
    {
        var rng = new Random();
        var pool = tier.Pools[rng.Next(tier.Pools.Count)];
        var itemId = pool.ItemIds[rng.Next(pool.ItemIds.Count)];

        var rewardItems = BuildRewardItems(itemId, pool.Count);
        long expirySeconds = (long)TimeSpan.FromHours(_config.DropExpiryHours).TotalSeconds;

        mailSendService.SendSystemMessageToPlayer(
            sessionId,
            $"Weekend Drop Unlocked: {tier.TierName}",
            rewardItems,
            expirySeconds
        );
    }

    private static List<Item> BuildRewardItems(string templateId, int count)
    {
        var root = new Item
        {
            Id = new MongoId(),
            Template = new MongoId(templateId),
            ParentId = null,
            SlotId = "main",
        };

        // If stackable, set stack count on the root item.
        if (count > 1)
        {
            root.Upd ??= new();
            root.Upd.StackObjectsCount = count;
        }

        return [root];
    }

    // Client state endpoint

    public WeekendStateDto GetClientState(MongoId sessionId)
    {
        bool active = IsWeekendActive();

        var dto = new WeekendStateDto
        {
            IsWeekendActive = active,
            WeekendId = GetCurrentWeekendId(),
            TimeRemainingSeconds = active ? GetSecondsUntilWeekendEnd() : 0,
            TierThresholds = _dropsConfig.Tiers.Select(t => t.RequiredChallenges).ToList(),
            TierGpRewards = _dropsConfig.Tiers.Select(t => t.GpReward).ToList(),
            ScheduleText = GetWeekendScheduleText(),
            DebugMode = _config.DebugMode
        };

        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return dto;

        dto.GpCoins = gpBalance.Get(sessionId.ToString());

        if (!active) return dto;

        // GetOrCreateState persists a freshly-assigned weekend itself; reads must
        // NOT write here, or concurrent /state fetches collide on the file.
        var state = GetOrCreateState(sessionId, profile);

        dto.WeekendId = state.WeekendId;
        dto.ClaimedTiers = state.ClaimedTiers;
        dto.Challenges = state.Challenges.Select(cp => new ChallengeDto
        {
            Id = cp.DefinitionId,
            Type = cp.Definition?.Type.ToString() ?? "",
            Description = cp.Definition?.Description ?? cp.DefinitionId,
            Current = cp.Current,
            Target = cp.Target,
            Completed = cp.Completed,
            Difficulty = cp.Definition?.Difficulty ?? 1
        }).ToList();

        return dto;
    }

    private DateTime _debugWeekendEnd = DateTime.MinValue;
    private const double DebugWeekendHours = 48;

    private double GetSecondsUntilWeekendEnd()
    {
        if (_config.DebugMode)
        {
            if (_debugWeekendEnd <= DateTime.UtcNow)
                _debugWeekendEnd = DateTime.UtcNow.AddHours(DebugWeekendHours);
            return (_debugWeekendEnd - DateTime.UtcNow).TotalSeconds;
        }

        var now = DateTime.Now;
        // Next occurrence of the configured end day at weekendEndHour.
        int daysUntilEnd = (_config.WeekendEndDay - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilEnd == 0 && now.Hour >= _config.WeekendEndHour)
            daysUntilEnd = 7;

        var end = now.Date.AddDays(daysUntilEnd).AddHours(_config.WeekendEndHour);
        return (end - now).TotalSeconds;
    }

    // JSON helpers

    // Tolerant load: a malformed or half-written file (bad manual edit, or a save
    // truncated by a crash mid-write) falls back to default with a named log line
    // instead of throwing - so one bad file can't brick the whole mod at startup.
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
