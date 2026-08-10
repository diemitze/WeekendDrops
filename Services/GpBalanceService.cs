using System.Text.Json;
using SysPath = System.IO.Path;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace WeekendDrops.Services;

[Injectable(InjectionType.Singleton)]
public class GpBalanceService
{
    private readonly string _file = WdPaths.Data("gp_balances.json");

    private readonly string _weeklyFile = WdPaths.Data("gp_weekly_earned.json");

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

    public int GetWeeklyEarned(string sessionId)
    {
        lock (_lock) return _weeklyEarned.TryGetValue(sessionId, out var v) ? v : 0;
    }

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
        catch {  }
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
        catch {  }
    }

    private class WeeklyEarnedStore
    {
        public string Period { get; set; } = "";
        public Dictionary<string, int> Earned { get; set; } = new();
    }
}
