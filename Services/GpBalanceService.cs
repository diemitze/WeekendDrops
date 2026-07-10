using System.Text.Json;
using SysPath = System.IO.Path;
using SPTarkov.DI.Annotations;

namespace WeekendDrops.Services;

[Injectable(InjectionType.Singleton)]
public class GpBalanceService
{
    private readonly string _file = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data", "gp_balances.json");

    // "Earned this weekend" - a per-profile tally for the Fika team board, stamped with a weekend
    // period so it resets each cycle. Only Add() (challenges/contracts/deposits) feeds it; gift
    // transfers are excluded so friends can't sling GP back and forth to farm the board.
    private readonly string _weeklyFile = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data", "gp_weekly_earned.json");

    private readonly object _lock = new();
    private Dictionary<string, int> _balances = new();
    private Dictionary<string, int> _weeklyEarned = new();
    private string _weeklyPeriod = "";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GpBalanceService() { Load(); LoadWeekly(); }

    public int Get(string sessionId)
    {
        lock (_lock) return _balances.TryGetValue(sessionId, out var v) ? v : 0;
    }

    public void Add(string sessionId, int amount)
    {
        if (amount <= 0) return;
        lock (_lock)
        {
            _balances[sessionId] = (_balances.TryGetValue(sessionId, out var v) ? v : 0) + amount;
            _weeklyEarned[sessionId] = (_weeklyEarned.TryGetValue(sessionId, out var w) ? w : 0) + amount;
            Save();
            SaveWeekly();
        }
    }

    // GP a profile has earned in the current weekend period (0 if none / not this period).
    public int GetWeeklyEarned(string sessionId)
    {
        lock (_lock) return _weeklyEarned.TryGetValue(sessionId, out var v) ? v : 0;
    }

    // Reset the whole "earned this weekend" tally when the weekend period advances. The
    // period id is global (same for every profile), so one stamp guards the whole dict.
    // Idempotent - safe to call on every squad-board build.
    public void RollWeeklyPeriod(string currentWeekendId)
    {
        if (string.IsNullOrEmpty(currentWeekendId)) return;
        lock (_lock)
        {
            if (_weeklyPeriod == currentWeekendId) return;
            _weeklyPeriod = currentWeekendId;
            _weeklyEarned.Clear();
            SaveWeekly();
        }
    }

    public bool TrySpend(string sessionId, int amount)
    {
        if (amount <= 0) return true;
        lock (_lock)
        {
            int cur = _balances.TryGetValue(sessionId, out var v) ? v : 0;
            if (cur < amount) return false;
            _balances[sessionId] = cur - amount;
            Save();
            return true;
        }
    }

    // Atomic gift: debit fromId and credit toId under one lock so a race can't dupe/lose coins.
    // Returns false (touching nothing) if the sender can't cover it. Target already validated.
    public bool TryTransfer(string fromId, string toId, int amount)
    {
        if (amount <= 0 || string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId) || fromId == toId)
            return false;
        lock (_lock)
        {
            int from = _balances.TryGetValue(fromId, out var f) ? f : 0;
            if (from < amount) return false;
            _balances[fromId] = from - amount;
            _balances[toId] = (_balances.TryGetValue(toId, out var t) ? t : 0) + amount;
            Save();
            return true;
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_file))
                _balances = JsonSerializer.Deserialize<Dictionary<string, int>>(
                    File.ReadAllText(_file)) ?? new();
        }
        catch { _balances = new(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SysPath.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_balances, JsonOptions));
        }
        catch { /* best-effort; balance still lives in memory this session */ }
    }

    private void LoadWeekly()
    {
        try
        {
            if (!File.Exists(_weeklyFile)) return;
            var w = JsonSerializer.Deserialize<WeeklyEarnedStore>(File.ReadAllText(_weeklyFile));
            if (w is null) return;
            _weeklyPeriod  = w.Period ?? "";
            _weeklyEarned  = w.Earned ?? new();
        }
        catch { _weeklyEarned = new(); _weeklyPeriod = ""; }
    }

    private void SaveWeekly()
    {
        try
        {
            Directory.CreateDirectory(SysPath.GetDirectoryName(_weeklyFile)!);
            File.WriteAllText(_weeklyFile, JsonSerializer.Serialize(
                new WeeklyEarnedStore { Period = _weeklyPeriod, Earned = _weeklyEarned }, JsonOptions));
        }
        catch { /* best-effort; the tally still lives in memory this session */ }
    }

    private class WeeklyEarnedStore
    {
        public string Period { get; set; } = "";
        public Dictionary<string, int> Earned { get; set; } = new();
    }
}
