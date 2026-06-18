using System.Text.Json;
using SysPath = System.IO.Path;
using SPTarkov.DI.Annotations;

namespace WeekendDrops.Services;

[Injectable(InjectionType.Singleton)]
public class GpBalanceService
{
    private readonly string _file = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data", "gp_balances.json");

    private readonly object _lock = new();
    private Dictionary<string, int> _balances = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GpBalanceService() => Load();

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
            Save();
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
}
