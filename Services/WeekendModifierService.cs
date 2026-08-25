using System.Text.Json;
using System.Text.Json.Serialization;
using SysPath = System.IO.Path;
using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;
using WeekendDrops.Models;

namespace WeekendDrops.Services;

[Injectable(InjectionType.Singleton)]
public class WeekendModifierService(ISptLogger<WeekendModifierService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private ModifiersConfig _modifiers = new();
    private ModConfig _config = new();

    private string _cachedFor = "";
    private WeekendModifier? _cached;

    public void LoadConfig(string configDir, ModConfig config)
    {
        _config = config;
        _cachedFor = "";

        var path = SysPath.Combine(configDir, "modifiers.json");
        try
        {
            _modifiers = File.Exists(path)
                ? JsonSerializer.Deserialize<ModifiersConfig>(File.ReadAllText(path), JsonOptions) ?? new ModifiersConfig()
                : new ModifiersConfig();
        }
        catch (Exception ex)
        {
            logger.Error($"[WeekendDrops] modifiers.json failed to load, weekend modifiers are off: {ex.Message}", null);
            _modifiers = new ModifiersConfig { Enabled = false };
        }

        if (!_modifiers.Enabled) return;

        var active = Active;
        logger.Debug(active is null
            ? "[WeekendDrops] Weekend modifier: none this weekend"
            : $"[WeekendDrops] Weekend modifier: '{active.Id}' ({active.Kind})");
    }

    public int GpPerKill => _modifiers.GpPerKill;
    public int MaxKillsPerRaid => _modifiers.MaxKillsPerRaid;

    public WeekendModifier? Active
    {
        get
        {
            if (!_modifiers.Enabled || !WeekendWindow.IsActive(_config)) return null;

            var id = WeekendWindow.CurrentId(_config);
            if (_cachedFor == id) return _cached;

            _cached = Roll(id);
            _cachedFor = id;
            return _cached;
        }
    }

    private WeekendModifier? Roll(string weekendId)
    {
        var pool = _modifiers.Modifiers.Where(Valid).ToList();
        if (pool.Count == 0) return null;

        int none = Math.Max(0, _modifiers.NoneWeight);
        int total = none + pool.Sum(m => Math.Max(1, m.Weight));
        if (total <= 0) return null;

        int roll = WeekendWindow.StableHash(weekendId) % total;
        if (roll < none) return null;

        roll -= none;
        foreach (var m in pool)
        {
            roll -= Math.Max(1, m.Weight);
            if (roll < 0) return m;
        }
        return pool[^1];
    }

    private static bool Valid(WeekendModifier m) =>
        m.Kind != WeekendModifierKind.WeaponClass || !string.IsNullOrEmpty(m.WeapClass);

    public int RateFor(WeekendModifier m) => Math.Max(0, m.GpPerKill ?? _modifiers.GpPerKill);
    public int CapFor(WeekendModifier m) => Math.Max(0, m.MaxKillsPerRaid ?? _modifiers.MaxKillsPerRaid);

    public int ScaleGp(int amount)
    {
        if (amount <= 0) return amount;
        if (Active is not { Kind: WeekendModifierKind.GpMultiplier } m) return amount;
        return (int)Math.Round(amount * Math.Max(1.0, m.Multiplier));
    }

    public double XpMultiplier =>
        Active is { Kind: WeekendModifierKind.XpMultiplier } m ? Math.Max(1.0, m.Multiplier) : 1.0;

    public WeekendModifierDto? ToDto()
    {
        var m = Active;
        if (m is null) return null;

        return new WeekendModifierDto
        {
            Id = m.Id,
            Kind = m.Kind.ToString(),
            WeapClass = m.WeapClass,
            Multiplier = m.Multiplier,
            Name = m.Name,
            Description = m.Description,
            GpPerKill = RateFor(m),
            MaxKillsPerRaid = CapFor(m),
            MinDistanceMeters = m.MinDistanceMeters
        };
    }
}
