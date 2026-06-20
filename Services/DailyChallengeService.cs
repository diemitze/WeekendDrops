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
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using WeekendDrops.Models;

namespace WeekendDrops.Services;

[Injectable(InjectionType.Singleton)]
public class DailyChallengeService(
    ProfileHelper profileHelper,
    MailSendService mailSendService,
    GpBalanceService gpBalance,
    ISptLogger<DailyChallengeService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private ModConfig _config = new();
    private List<DailyChallengeDefinition> _pool = [];        // effective pool
    private List<DailyChallengeDefinition> _allDaily = [];    // full pool as loaded
    private bool _lootNetActive;                              // LootNET bridge present
    private bool _scavDisabled;                               // client F12 toggle: drop Scav-run dailies
    private List<ShopItemDefinition> _shopItems = [];

    // itemId -> UTC time it becomes buyable again (in-memory; single-player).
    private readonly Dictionary<string, DateTime> _restockUntil = [];

    // Original stock per item, captured at load, used by the global restock refill.
    private readonly Dictionary<string, int> _maxStock = [];

    // UTC time of the next global stock refill.
    private DateTime _nextGlobalRestock = DateTime.MinValue;

    private readonly string _dataDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data");

    private readonly string _configDir = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "config");

    public void LoadConfig()
    {
        _config = LoadJson<ModConfig>(SysPath.Combine(_configDir, "config.json")) ?? new ModConfig();

        // See WeekendChallengeService: debugUseRealChallenges keeps the debug
        // conveniences but loads the real daily pool for balance-testing.
        var useDebugPool = _config.DebugMode && !_config.DebugUseRealChallenges;
        var dailyFile = useDebugPool ? "daily_challenges_debug.json" : "daily_challenges.json";
        _allDaily = LoadJson<List<DailyChallengeDefinition>>(
            SysPath.Combine(_configDir, dailyFile)) ?? [];

        // Loot-value dailies need the LootNET bridge; seed from config, flipped on at
        // runtime by the client signal via SetLootNetActive.
        _lootNetActive = _config.IncludeLootNet;
        ApplyDailyPool();

        _shopItems = LoadJson<List<ShopItemDefinition>>(
            SysPath.Combine(_configDir, "shop.json")) ?? [];

        Directory.CreateDirectory(_dataDir);

        _maxStock.Clear();
        foreach (var s in _shopItems)
            _maxStock[s.Id] = s.Stock;

        LoadRestockState();
        LoadGlobalRestockState();
        LoadShopStock();

        if (_config.DebugMode)
            logger.Warning($"[WeekendDrops] Daily DEBUG MODE - using {dailyFile}");
    }

    // Effective daily pool: drop loot-value dailies unless the LootNET bridge is
    // active, and drop Scav-run dailies when the player has Scav challenges off.
    private void ApplyDailyPool() =>
        _pool = _allDaily
            .Where(c => _lootNetActive || !c.RequiresLootNet)
            .Where(c => ScavEnabled || !ChallengeMetrics.IsScavOnly(c.Type))
            .ToList();

    // Scav-run dailies are in the pool only when enabled in config AND the client
    // hasn't toggled them off (F12). Either source can suppress them.
    private bool ScavEnabled => _config.EnableScavChallenges && !_scavDisabled;

    // Flipped on (sticky) when a client state request carries the LootNET bridge tag.
    public void SetLootNetActive()
    {
        if (_lootNetActive) return;
        _lootNetActive = true;
        ApplyDailyPool();
    }

    // Flipped on (sticky) when a client state request carries the no-Scav toggle tag.
    // Takes full effect on restart, mirroring the LootNET bridge.
    public void SetScavChallengesDisabled()
    {
        if (_scavDisabled) return;
        _scavDisabled = true;
        ApplyDailyPool();
    }

    // ── Raid end ──────────────────────────────────────────────────────────────

   
    public int ApplyRaidResult(MongoId sessionId, RaidResultRequest r)
    {
        if (_pool.Count == 0) return 0;

        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return 0;

        var state = GetOrCreateDailyState(sessionId, profile);
        WireDefinitions(state);

        if (!string.IsNullOrEmpty(r.RaidId) && state.LastRaidId == r.RaidId) return 0;
        state.LastRaidId = r.RaidId;

        if (r.Survived) state.SurvivalTimeBank += r.SurvivedSeconds;
        else            state.SurvivalTimeBank = 0;

        int totalKills = r.ScavKills + r.PmcKills + r.BossKills;
        int gpEarned = 0;

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

                // Single-raid spikes credited only if this raid hits the target.
                case ChallengeType.KillPMCsSingleRaid:   if (r.PmcKills  >= cp.Target) cp.Current = cp.Target; break;
                case ChallengeType.KillScavsSingleRaid:  if (r.ScavKills >= cp.Target) cp.Current = cp.Target; break;

                // Scav-run quests only while running as a Scav.
                case ChallengeType.ScavExtract:   if (r.IsScavRaid && r.Survived) cp.Current += 1; break;
                case ChallengeType.ScavRaidsDone: if (r.IsScavRaid)               cp.Current += 1; break;
                case ChallengeType.ScavKills:     if (r.IsScavRaid)               cp.Current += totalKills; break;
                case ChallengeType.ScavExtractFromLocation:
                    if (r.IsScavRaid && r.Survived && !string.IsNullOrEmpty(cp.Definition.TargetLocation)
                        && LocationUtil.Matches(r.Location, cp.Definition.TargetLocation))
                        cp.Current += 1;
                    break;

                // Loot-value quests (LootNET) survived extracts only.
                case ChallengeType.ExtractWithLootValue: if (r.Survived && r.LootValue >= cp.Target) cp.Current = cp.Target; break;
                case ChallengeType.LootValueCumulative:  if (r.Survived) cp.Current += r.LootValue; break;
            }


            if (cp.Completed) gpEarned += cp.Definition?.GpReward ?? 0;
        }

        SaveDailyState(sessionId, state);
        logger.Info($"[WeekendDrops] Daily raid result applied (survived={r.Survived}, gpEarned={gpEarned}) - " +
            string.Join(", ", state.Challenges.Select(c => $"{c.Definition?.Type}:{c.Current}/{c.Definition?.Target ?? c.Target}")));
        return gpEarned;
    }

    // ── Client state ──────────────────────────────────────────────────────────

    public DailyStateDto GetDailyState(MongoId sessionId)
    {
        CheckGlobalRestock();

        var profile = profileHelper.GetPmcProfile(sessionId);

        List<DailyChallengeProgress> challenges = [];
        bool bonusClaimed = false;
        if (profile != null)
        {
            var state = GetOrCreateDailyState(sessionId, profile);
            WireDefinitions(state);
            challenges = state.Challenges;
            bonusClaimed = state.BonusClaimed;
        }

        return new DailyStateDto
        {
            Challenges = challenges.Select(cp => new DailyChallengeDto
            {
                Id            = cp.DefinitionId,
                Type          = cp.Definition?.Type.ToString() ?? "",
                Description   = cp.Definition?.Description ?? cp.DefinitionId,
                Current       = cp.Current,
                Target        = cp.Definition?.Target ?? cp.Target,
                Completed     = cp.Completed,
                GpReward      = cp.Definition?.GpReward ?? 0,
                RewardClaimed = cp.RewardClaimed
            }).ToList(),
            ShopItems = _shopItems.Select(s => new ShopItemDto
            {
                Id          = s.Id,
                Name        = s.Name,
                Description = s.Description,
                GpCost      = s.GpCost,
                Stock       = s.Stock,
                TemplateId  = s.TemplateId,
                Contents    = s.Contents?.Select(c => new ShopContentDto
                {
                    TemplateId = c.TemplateId,
                    Count      = c.Count
                }).ToList() ?? [],
                RestockSeconds = _restockUntil.TryGetValue(s.Id, out var u) && u > DateTime.UtcNow
                    ? (u - DateTime.UtcNow).TotalSeconds
                    : 0
            }).ToList(),
            NextResetSeconds = SecondsUntilMidnight(),
            GlobalRestockSeconds = _nextGlobalRestock > DateTime.UtcNow
                ? (_nextGlobalRestock - DateTime.UtcNow).TotalSeconds
                : 0,
            DailyBonusGp = BonusGp(challenges),
            DailyBonusClaimed = bonusClaimed
        };
    }

    // Complete-all bonus = 50% of the day's total GP, rounded. Must match the
    // value the client shows on the bonus card.
    private static int BonusGp(IEnumerable<DailyChallengeProgress> challenges) =>
        (int)Math.Round(challenges.Sum(c => c.Definition?.GpReward ?? 0) * 0.5,
            MidpointRounding.AwayFromZero);

    // ── Claim complete-all daily bonus ────────────────────────────────────────

    public string ClaimDailyBonus(MongoId sessionId)
    {
        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return "profile_not_found";

        var state = GetOrCreateDailyState(sessionId, profile);
        WireDefinitions(state);

        if (state.Challenges.Count == 0)            return "no_challenges";
        if (!state.Challenges.All(c => c.Completed)) return "not_completed";
        if (state.BonusClaimed)                      return "already_claimed";

        int reward = BonusGp(state.Challenges);
        if (reward <= 0) return "no_reward";

        gpBalance.Add(sessionId.ToString(), reward);
        state.BonusClaimed = true;
        SaveDailyState(sessionId, state);

        logger.Info($"[WeekendDrops] Daily complete-all bonus: +{reward} GP credited for {sessionId} (balance {gpBalance.Get(sessionId.ToString())})");
        return "ok";
    }

    // ── Claim daily GP reward ─────────────────────────────────────────────────

    public string ClaimDailyReward(MongoId sessionId, string challengeId)
    {
        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return "profile_not_found";

        var state = GetOrCreateDailyState(sessionId, profile);
        WireDefinitions(state);

        var cp = state.Challenges.FirstOrDefault(c => c.DefinitionId == challengeId);
        if (cp is null)      return "challenge_not_found";
        if (!cp.Completed)   return "not_completed";
        if (cp.RewardClaimed) return "already_claimed";

        int reward = cp.Definition?.GpReward ?? 0;
        if (reward <= 0) return "no_reward";

        // GP is a virtual balance now - credit it directly instead of mailing coins.
        gpBalance.Add(sessionId.ToString(), reward);

        cp.RewardClaimed = true;
        SaveDailyState(sessionId, state);

        logger.Info($"[WeekendDrops] Daily reward: +{reward} GP credited for '{challengeId}' by {sessionId} (balance {gpBalance.Get(sessionId.ToString())})");
        return "ok";
    }

    // ── Buy shop item ─────────────────────────────────────────────────────────

    public string BuyShopItem(MongoId sessionId, string itemId)
    {
        var shopItem = _shopItems.FirstOrDefault(s => s.Id == itemId);
        if (shopItem is null)    return "item_not_found";
        if (shopItem.Stock == 0) return "out_of_stock";

        if (_restockUntil.TryGetValue(itemId, out var until) && DateTime.UtcNow < until)
            return "restocking";


        if (!gpBalance.TrySpend(sessionId.ToString(), shopItem.GpCost))
            return "insufficient_gp";

        mailSendService.SendSystemMessageToPlayer(
            sessionId,
            $"GP Shop Purchase: {shopItem.Name}",
            BuildShopRewardItems(shopItem),
            (long)TimeSpan.FromHours(_config.DropExpiryHours).TotalSeconds
        );

        if (shopItem.Stock > 0) { shopItem.Stock--; SaveShopStock(); }

        double restockHours = shopItem.RestockHours ?? _config.ShopRestockHours;
        if (restockHours > 0)
        {
            _restockUntil[itemId] = DateTime.UtcNow.AddHours(restockHours);
            SaveRestockState();
        }

        logger.Info($"[WeekendDrops] Shop purchase: {shopItem.Name} (-{shopItem.GpCost} GP, " +
                    $"balance {gpBalance.Get(sessionId.ToString())}). Restock in {restockHours}h");
        return "ok";
    }

    // ── Daily state management ────────────────────────────────────────────────


    private readonly object _fileLock = new();

    private PlayerDailyState GetOrCreateDailyState(MongoId sessionId, PmcData profile)
    {
        var path = DailyStatePath(sessionId);
        PlayerDailyState? state;

        lock (_fileLock)
            state = File.Exists(path) ? LoadJson<PlayerDailyState>(path) : null;

        var todayId = GetCurrentDailyId();

        // Scav disabled: swap any Scav-run dailies in the current set for PMC quests
        // in place (even completed ones), keeping the rest of the set and its progress.
        // Runs before the staleness check so it doesn't trigger a full reassign.
        if (state is not null && state.DailyId == todayId && ReplaceScavChallenges(state))
        {
            SaveDailyState(sessionId, state);
            logger.Info($"[WeekendDrops] Daily Scav challenges replaced for {sessionId} (Scav challenges disabled)");
        }

        bool stale = state is not null
            && state.Challenges.Any(c => _pool.All(d => d.Id != c.DefinitionId));

        if (state is null || state.DailyId != todayId || stale)
        {
            state = new PlayerDailyState { DailyId = todayId };
            AssignDailyChallenges(state);
            SaveDailyState(sessionId, state);
            logger.Info($"[WeekendDrops] Daily reset for {sessionId} - assigned {state.Challenges.Count} challenges");
        }

        return state;
    }

    // Replace every Scav-run challenge in the set with another quest, in place. The
    // replacement comes from a group not already in the set (preferring a PMC quest
    // when the PMC slot is free), keeping the one-per-group variety of a fresh set.
    // Other challenges and their progress are untouched; the replacement starts fresh
    // at 0/target, so a previously-completed Scav challenge is genuinely re-tasked.
    // Returns true if anything was swapped.
    private bool ReplaceScavChallenges(PlayerDailyState state)
    {
        if (!_scavDisabled) return false;

        var rng     = new Random();
        var usedIds = state.Challenges.Select(c => c.DefinitionId).ToHashSet();
        // Groups occupied by the non-Scav challenges we're keeping.
        var usedGroups = state.Challenges
            .Select(c => _allDaily.FirstOrDefault(d => d.Id == c.DefinitionId))
            .Where(d => d is not null && !ChallengeMetrics.IsScavOnly(d.Type))
            .Select(d => ChallengeMetrics.Group(d!.Type))
            .ToHashSet();

        bool changed = false;

        foreach (var cp in state.Challenges)
        {
            var def = _allDaily.FirstOrDefault(d => d.Id == cp.DefinitionId);
            if (def is null || !ChallengeMetrics.IsScavOnly(def.Type)) continue;

            // Candidates sit in a group not already used. Prefer PMC; otherwise any
            // unused group. Fall back to any unused id only if the pool is too small.
            var fresh = _pool.Where(d => !usedIds.Contains(d.Id)
                                      && !usedGroups.Contains(ChallengeMetrics.Group(d.Type))).ToList();
            var pmc   = fresh.Where(d => ChallengeMetrics.Group(d.Type) == "pmc").ToList();
            var pickPool = pmc.Count > 0 ? pmc : fresh;
            if (pickPool.Count == 0)
                pickPool = _pool.Where(d => !usedIds.Contains(d.Id)).ToList();
            if (pickPool.Count == 0) continue;   // can't replace cleanly; stale check reassigns

            var pick = pickPool[rng.Next(pickPool.Count)];

            usedIds.Remove(cp.DefinitionId);
            cp.DefinitionId  = pick.Id;
            cp.Target        = pick.Target;
            cp.Current       = 0;
            cp.RewardClaimed = false;
            cp.Definition    = pick;
            usedIds.Add(pick.Id);
            usedGroups.Add(ChallengeMetrics.Group(pick.Type));
            changed = true;
        }

        return changed;
    }

    private void SaveDailyState(MongoId sessionId, PlayerDailyState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        lock (_fileLock)
            File.WriteAllText(DailyStatePath(sessionId), json);
    }

    private string DailyStatePath(MongoId sessionId) =>
        SysPath.Combine(_dataDir, $"{sessionId}_daily.json");

    private void WireDefinitions(PlayerDailyState state)
    {
        foreach (var cp in state.Challenges)
            cp.Definition = _pool.FirstOrDefault(d => d.Id == cp.DefinitionId);
    }

    private void AssignDailyChallenges(PlayerDailyState state)
    {
        var rng = new Random();

        var byGroup = _pool
            .OrderBy(_ => rng.Next())
            .GroupBy(d => ChallengeMetrics.Group(d.Type))
            .Select(g => g.First());
        var selected = byGroup
            .Concat(_pool.OrderBy(_ => rng.Next()))   // top-up pool if <5 groups
            .DistinctBy(d => d.Id)
            .Take(Math.Min(5, _pool.Count))
            .ToList();

        state.Challenges = selected.Select(d => new DailyChallengeProgress
        {
            DefinitionId = d.Id,
            Target       = d.Target,
            Definition   = d
        }).ToList();
    }

    // ── Debug actions for the daily set (gated by debugMode, like the weekend ones) ──
    public bool DebugAction(MongoId sessionId, string action)
    {
        if (!_config.DebugMode)
        {
            logger.Warning("[WeekendDrops] Daily debug action ignored - debugMode is off");
            return false;
        }

        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return false;

        var state = GetOrCreateDailyState(sessionId, profile);
        WireDefinitions(state);

        switch (action?.ToLowerInvariant())
        {
            case "completeone":
                var next = state.Challenges.FirstOrDefault(c => !c.Completed);
                if (next is not null) next.Current = next.Target;
                break;
            case "completeall":
                foreach (var c in state.Challenges) c.Current = c.Target;
                break;
            case "resetprogress":
                foreach (var c in state.Challenges) { c.Current = 0; c.RewardClaimed = false; }
                state.SurvivalTimeBank = 0;
                state.BonusClaimed = false;
                break;
            case "reroll":
                AssignDailyChallenges(state);   // fresh random pick; Current defaults to 0
                state.SurvivalTimeBank = 0;
                state.BonusClaimed = false;
                break;
            default:
                logger.Warning($"[WeekendDrops] Unknown daily debug action '{action}'");
                return false;
        }

        SaveDailyState(sessionId, state);
        logger.Info($"[WeekendDrops] Daily debug action '{action}' applied for {sessionId}");
        return true;
    }

    // ── Debug: reset today's daily progress ───────────────────────────────────


    public void ResetDailyProgress(MongoId sessionId)
    {
        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return;

        var state = GetOrCreateDailyState(sessionId, profile);
        WireDefinitions(state);

        foreach (var c in state.Challenges)
        {
            c.Current = 0;
            c.RewardClaimed = false;
        }
        state.SurvivalTimeBank = 0;
        state.BonusClaimed = false;
        SaveDailyState(sessionId, state);
        logger.Info($"[WeekendDrops] Debug: daily progress reset for {sessionId}");
    }


    public void RerollDaily(MongoId sessionId)
    {
        var profile = profileHelper.GetPmcProfile(sessionId);
        if (profile is null) return;

        var state = GetOrCreateDailyState(sessionId, profile);
        AssignDailyChallenges(state);
        state.SurvivalTimeBank = 0;
        state.BonusClaimed = false;
        SaveDailyState(sessionId, state);
        logger.Info($"[WeekendDrops] Debug: daily set rerolled for {sessionId}");
    }

    // ── Reward builders ───────────────────────────────────────────────────────

    private static List<Item> BuildShopRewardItems(ShopItemDefinition shop)
    {
        if (shop.Contents is { Count: > 0 })
            return shop.Contents
                .Where(c => !string.IsNullOrEmpty(c.TemplateId))
                .SelectMany(c => BuildRewardItems(c.TemplateId, c.Count))
                .ToList();

        return BuildRewardItems(shop.TemplateId, shop.Count);
    }

    private static List<Item> BuildRewardItems(string templateId, int count)
    {
        var item = new Item
        {
            Id       = new MongoId(),
            Template = new MongoId(templateId),
            ParentId = null,
            SlotId   = "main",
        };
        if (count > 1) { item.Upd ??= new(); item.Upd.StackObjectsCount = count; }
        return [item];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetCurrentDailyId() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    private static double SecondsUntilMidnight()
    {
        var now = DateTime.UtcNow;
        return (now.Date.AddDays(1) - now).TotalSeconds;
    }

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

    // ── Restock cooldown persistence ───────────────────────────────────────────

    private string RestockStatePath => SysPath.Combine(_dataDir, "shop_restock.json");

    private void LoadRestockState()
    {
        _restockUntil.Clear();
        var saved = LoadJson<Dictionary<string, DateTime>>(RestockStatePath);
        if (saved is null) return;

        // Drop entries whose cooldown already elapsed while the server was off.
        foreach (var kv in saved)
            if (kv.Value > DateTime.UtcNow)
                _restockUntil[kv.Key] = kv.Value;
    }

    private void SaveRestockState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_restockUntil, JsonOptions);
            File.WriteAllText(RestockStatePath, json);
        }
        catch (Exception ex)
        {
            logger.Warning($"[WeekendDrops] Failed to save restock state: {ex.Message}");
        }
    }

    // ── Global restock (periodic stock refill) ─────────────────────────────────

    private string GlobalRestockPath => SysPath.Combine(_dataDir, "shop_global_restock.json");

    private void LoadGlobalRestockState()
    {
        var saved = LoadJson<GlobalRestockState>(GlobalRestockPath);
        _nextGlobalRestock = saved?.NextRestock ?? DateTime.MinValue;
        CheckGlobalRestock();
    }

    private void CheckGlobalRestock()
    {
        if (_config.ShopGlobalRestockHours <= 0) return;

        // First run, or the scheduled time has passed → refill and reschedule.
        if (_nextGlobalRestock == DateTime.MinValue)
        {
            _nextGlobalRestock = DateTime.UtcNow.AddHours(_config.ShopGlobalRestockHours);
            SaveGlobalRestock();
            return;
        }

        if (DateTime.UtcNow < _nextGlobalRestock) return;

        foreach (var s in _shopItems)
            if (_maxStock.TryGetValue(s.Id, out var max))
                s.Stock = max;
        SaveShopStock();

        _nextGlobalRestock = DateTime.UtcNow.AddHours(_config.ShopGlobalRestockHours);
        SaveGlobalRestock();
        logger.Info("[WeekendDrops] Global shop restock - all stock refilled");
    }

    private void SaveGlobalRestock()
    {
        try
        {
            var json = JsonSerializer.Serialize(new GlobalRestockState { NextRestock = _nextGlobalRestock }, JsonOptions);
            File.WriteAllText(GlobalRestockPath, json);
        }
        catch (Exception ex)
        {
            logger.Warning($"[WeekendDrops] Failed to save global restock state: {ex.Message}");
        }
    }

    private sealed class GlobalRestockState
    {
        public DateTime NextRestock { get; set; }
    }

    // ── Live stock persistence ─────────────────────────────────────────────────


    private string ShopStockPath => SysPath.Combine(_dataDir, "shop_stock.json");

    private void LoadShopStock()
    {
        var saved = LoadJson<Dictionary<string, int>>(ShopStockPath);
        if (saved is null) return;

        foreach (var s in _shopItems)
        {
            if (!saved.TryGetValue(s.Id, out var stock)) continue;
            // Unlimited (-1) stays unlimited; otherwise restore the saved count,
            // clamped to the configured max (in case the max was lowered in config).
            if (s.Stock < 0) continue;
            var max = _maxStock.TryGetValue(s.Id, out var m) ? m : s.Stock;
            s.Stock = stock < 0 ? 0 : Math.Min(stock, max);
        }
    }

    private void SaveShopStock()
    {
        try
        {
            var map = _shopItems.ToDictionary(s => s.Id, s => s.Stock);
            File.WriteAllText(ShopStockPath, JsonSerializer.Serialize(map, JsonOptions));
        }
        catch (Exception ex)
        {
            logger.Warning($"[WeekendDrops] Failed to save shop stock: {ex.Message}");
        }
    }
}
