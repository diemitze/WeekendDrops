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
    GpGiftService giftService,
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
    private List<ChallengeDefinition> _challengePool = [];   // after the filters below
    private List<ChallengeDefinition> _allChallenges = [];   // full pool as loaded
    private bool _lootNetActive;
    private bool _scavDisabled;
    private DropsConfig _dropsConfig = new();
    private CratePoolsConfig _cratePools = new();
    private CratePoolsConfig _wttPools = new();
    // Keyed by crate template id, not by tier.
    private CratePoolsConfig _arenaPools = new();

    private readonly string _dataDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data");

    private readonly string _configDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "config");

    public void LoadConfig()
    {
        _config = LoadJson<ModConfig>(SysPath.Combine(_configDir, "config.json")) ?? new ModConfig();

        // Always the real pool: debugMode only forces the weekend on, and the in-panel debug
        // buttons handle quick completes.
        _allChallenges = LoadJson<List<ChallengeDefinition>>(SysPath.Combine(_configDir, "challenges.json")) ?? [];

        // The client bridge can still flip this on later, for setups undetected at boot.
        _lootNetActive = _config.IncludeLootNet || ModPresence.LootNetInstalled;
        ApplyChallengePool();

        _dropsConfig = LoadJson<DropsConfig>(SysPath.Combine(_configDir, "drops.json")) ?? new DropsConfig();
        _cratePools = LoadJson<CratePoolsConfig>(SysPath.Combine(_configDir, "crate_pools.json")) ?? new CratePoolsConfig();
        _wttPools = LoadJson<CratePoolsConfig>(SysPath.Combine(_configDir, "crate_pools_wtt.json")) ?? new CratePoolsConfig();
        _arenaPools = LoadJson<CratePoolsConfig>(SysPath.Combine(_configDir, "arena_pools.json")) ?? new CratePoolsConfig();

        if (_config.DebugMode)
            logger.Warning("[WeekendDrops] DEBUG MODE active - weekend forced on");

        Directory.CreateDirectory(_dataDir);
    }

    // Without the LootNET bridge, loot-value challenges would never progress.
    private void ApplyChallengePool()
    {
        _challengePool = _allChallenges
            .Where(c => _lootNetActive || !c.RequiresLootNet)
            .Where(c => ScavEnabled || !ChallengeMetrics.IsScavOnly(c.Type))
            .ToList();
        RecomputeWeekendPlan();
    }

    // Clamped to what the pool can produce (one challenge per group). An unreachable target
    // used to cause a reset loop.
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

        // Largest reachable budget at or under the configured one. If even the all-easy floor
        // is unreachable, walk up to the smallest total that is.
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

    // Either the config or the client toggle can suppress them.
    private bool ScavEnabled => _config.EnableScavChallenges && !_scavDisabled;

    // Sticky for the server run, to avoid churn.
    public void SetLootNetActive()
    {
        if (_lootNetActive) return;
        _lootNetActive = true;
        ApplyChallengePool();
        logger.Info("[WeekendDrops] LootNET bridge detected - loot-value challenges enabled.");
    }

    // Sticky for the server run, so it takes full effect on restart.
    public void SetScavChallengesDisabled()
    {
        if (_scavDisabled) return;
        _scavDisabled = true;
        ApplyChallengePool();
        logger.Info("[WeekendDrops] Client toggle: Scav-run challenges disabled for this run.");
    }

    // Swaps each Scav-run challenge for one of the same difficulty (so the budget holds) from
    // an unused group. Replacements start at 0, so a completed one is genuinely re-tasked.
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

            // Same difficulty keeps the budget, an unused group keeps variety.
            var sameDiff = _challengePool.Where(d => d.Difficulty == def.Difficulty
                                                  && !usedIds.Contains(d.Id)
                                                  && !usedGroups.Contains(ChallengeMetrics.Group(d.Type))).ToList();
            var pmc      = sameDiff.Where(d => ChallengeMetrics.Group(d.Type) == "pmc").ToList();
            var pickPool = pmc.Count > 0 ? pmc : sameDiff;
            // Keeps the budget balanced even when every fresh group is taken.
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

    // Reward crates are vanilla RandomLootContainer items with no SPT pool, so they'd open
    // empty. Weighted per tier (crate_pools.json) so high-tier pulls stay rare.
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

            // Only items actually in the DB, so this auto-skips when the mod isn't installed.
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
                Patches.WdCrateRegistry.Register(details);
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
            Patches.WdCrateRegistry.Register(details);
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

        // A duplicate or retried report.
        if (!string.IsNullOrEmpty(r.RaidId) && state.LastRaidId == r.RaidId)
        {
            logger.Info($"[WeekendDrops] Raid {r.RaidId} already applied - ignoring duplicate report");
            return 0;
        }
        state.LastRaidId = r.RaidId;

        // Tier GP is "earned" the raid its difficulty threshold is first crossed.
        int pointsBefore = CompletedDifficultyPoints(state);

        // Death wipes the bank.
        if (r.Survived) state.SurvivalTimeBank += r.SurvivedSeconds;
        else            state.SurvivalTimeBank = 0;

        // Raiders/Rogues are excluded from ScavKills, so add them back or they never count
        // toward the any-kill and single-raid quests.
        int totalKills = r.ScavKills + r.PmcKills + r.BossKills + r.RaiderKills + r.RogueKills;

        foreach (var cp in state.Challenges.Where(c => !c.Completed))
        {
            switch (cp.Definition?.Type)
            {
                case ChallengeType.KillScavs:            cp.Current += r.ScavKills; break;
                case ChallengeType.KillPMCs:             cp.Current += r.PmcKills;  break;
                case ChallengeType.KillBoss:             cp.Current += r.BossKills; break;
                case ChallengeType.KillCultists:         cp.Current += r.CultistKills; break;
                case ChallengeType.KillPriest:           cp.Current += r.PriestKills;  break;
                case ChallengeType.KillRaiders:          cp.Current += r.RaiderKills;  break;
                case ChallengeType.KillRogues:           cp.Current += r.RogueKills;   break;
                case ChallengeType.MeleeKills:           cp.Current += r.MeleeKills;   break;
                case ChallengeType.KillsSingleRaid:      if (totalKills >= cp.Target) cp.Current = cp.Target; break;
                case ChallengeType.SurviveTimeSingleRaid: if (r.Survived && r.SurvivedSeconds >= cp.Target) cp.Current = cp.Target; break;
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

                // Only extracted loot is kept, so a death credits nothing.
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

        // Sunday is 0, so the window crosses the week boundary and needs the extra cases.
        return afterStart && (day != 0 || beforeEnd)
               || (day == 0)
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

        // Swapped in place, before the staleness check, so it doesn't reassign the whole set.
        if (state is not null && state.WeekendId == currentWeekendId && ReplaceScavChallenges(state))
        {
            SaveState(sessionId, state);
            logger.Info($"[WeekendDrops] Weekend Scav challenges replaced for {sessionId} (Scav challenges disabled)");
        }

        bool stale = false;
        if (state is not null && state.WeekendId == currentWeekendId)
        {
            bool poolChanged = state.Challenges.Any(c => _challengePool.All(d => d.Id != c.DefinitionId));
            // Against the stamped plan, not the set's raw totals, or this rerolls every load.
            // Count matters too: it can change at the same budget.
            bool planChanged = state.PlanBudget != _planBudget || state.PlanCount != _planCount;
            stale = poolChanged || planChanged;
        }

        if (state is null || state.WeekendId != currentWeekendId || stale)
        {
            if (stale) logger.Info($"[WeekendDrops] Reassigning weekend for {sessionId} - cached set was stale (pool or weekend plan changed)");
            state = new PlayerWeekendState { WeekendId = currentWeekendId };
            AssignChallenges(state);
            logger.Info($"[WeekendDrops] New weekend started for {sessionId} - assigned {state.Challenges.Count} challenges");

            SaveState(sessionId, state);
        }

        // Definition references aren't stored in JSON.
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

    // Never mutates, unlike GetOrCreateState. (0,0) when the profile has no state this weekend.
    public (int done, int total) GetWeeklyProgress(MongoId sessionId)
    {
        try
        {
            var path = StatePath(sessionId);
            if (!File.Exists(path)) return (0, 0);
            var state = JsonSerializer.Deserialize<PlayerWeekendState>(File.ReadAllText(path));
            if (state is null || state.WeekendId != GetCurrentWeekendId()) return (0, 0);
            return (state.Challenges.Count(c => c.Completed), state.Challenges.Count);
        }
        catch { return (0, 0); }
    }

    // Challenge selection

    private void AssignChallenges(PlayerWeekendState state)
    {
        var rng = new Random();
        // The effective plan, not the raw config values, so the set hits the budget.
        int n = _planCount;
        int budget = _planBudget;

        var chosen = PickByDifficultyBudget(rng, n, budget) ?? PickWeighted(rng, n);

        state.Challenges = chosen.Select(d => new ChallengeProgress
        {
            DefinitionId = d.Id,
            Target = d.Target,
            Definition = d
        }).ToList();

        // Lets the staleness check tell a real plan change from a set that simply couldn't
        // reach the target.
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

    // True only on a full n-challenge set, which makes it a sound witness that the budget
    // is reachable.
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

    // True only when the tier was earned, unclaimed, and the reward actually mailed.
    public bool ClaimTier(MongoId sessionId, int requiredChallenges)
    {
        if (!_config.Enabled || !IsWeekendActive()) return false;

        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return false;

        // RequiredChallenges is a difficulty-point threshold, not a challenge count, and
        // doubles as this tier's crate_pools key.
        var tier = _dropsConfig.Tiers.FirstOrDefault(t => t.RequiredChallenges == requiredChallenges);
        if (tier is null)
        {
            logger.Warning($"[WeekendDrops] Claim rejected - no tier requires {requiredChallenges} points");
            return false;
        }

        var state = GetOrCreateState(sessionId, profile);
        int completedPoints = CompletedDifficultyPoints(state);

        // Debug bypasses the completion gate, so the whole claim flow can be exercised.
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
        if (tier.GpReward > 0)
            gpBalance.Add(sessionId.ToString(), tier.GpReward);
        state.ClaimedTiers.Add(tier.RequiredChallenges);
        SaveState(sessionId, state);

        int totalPoints = state.Challenges.Sum(c => c.Definition?.Difficulty ?? 0);
        logger.Info($"[WeekendDrops] Player {sessionId} claimed '{tier.TierName}' ({completedPoints}/{totalPoints} difficulty points done)");
        return true;
    }

    // The metric tiers gate on.
    private static int CompletedDifficultyPoints(PlayerWeekendState state) =>
        state.Challenges.Where(c => c.Completed).Sum(c => c.Definition?.Difficulty ?? 0);

    // Debug helpers

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
                // So SurviveTimeCumulative resets too.
                state.SurvivalTimeBank = 0;
                break;
            case "reroll":
                // A fresh pick, unlike resetprogress, which keeps the same set.
                state.ClaimedTiers.Clear();
                state.SurvivalTimeBank = 0;
                AssignChallenges(state);
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

        // Drained even off-weekend: a gift is always announced.
        dto.PendingGifts = giftService.TakePending(sessionId.ToString());

        if (!active) return dto;

        // GetOrCreateState persists a freshly-assigned weekend itself. Reads must NOT write
        // here, or concurrent /state fetches collide on the file.
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

    // A malformed or half-written file falls back to default with a named log line, so one
    // bad save can't brick the mod at startup.
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
