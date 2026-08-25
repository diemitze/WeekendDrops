using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace WeekendDrops.Patches;

/// One reward slot, resolved to a group at open time. Chance is rolled per crate, so a
/// 0.45 weapon slot means 45% of crates hold a gun, not 45% of tiers.
public class CrateSlotPlan
{
    public string Group { get; init; } = "";
    public double Chance { get; init; } = 1.0;
    public string? Fallback { get; init; }
}

/// What a crate is made of, resolved once at load so no classification happens per drop.
public class CrateRecipe
{
    public List<CrateSlotPlan> Slots { get; init; } = [];

    /// Pool split by group, so a slot fills without rescanning the whole pool.
    public Dictionary<string, List<KeyValuePair<MongoId, double>>> Groups { get; init; } = new();

    public string ModTier { get; init; } = "";

    /// Rolls the chance-gated slots for one crate opening.
    public List<string> Roll(Random rng)
    {
        var rolled = new List<string>(Slots.Count);
        foreach (var slot in Slots)
        {
            var group = slot.Chance < 1.0 && rng.NextDouble() > slot.Chance
                      ? slot.Fallback ?? slot.Group
                      : slot.Group;

            if (!Groups.TryGetValue(group, out var have) || have.Count == 0)
                group = slot.Fallback is not null && Groups.ContainsKey(slot.Fallback) ? slot.Fallback : "";

            rolled.Add(group);
        }
        return rolled;
    }
}

public static class WdCrateRegistry
{
    private static readonly Dictionary<RewardDetails, CrateRecipe> Ours =
        new(ReferenceEqualityComparer.Instance);

    public static void Register(RewardDetails details, CrateRecipe? recipe = null)
    {
        if (details != null) Ours[details] = recipe ?? new CrateRecipe();
    }

    public static bool IsOurs(RewardDetails details) => details != null && Ours.ContainsKey(details);

    public static CrateRecipe? RecipeFor(RewardDetails details) =>
        details != null && Ours.TryGetValue(details, out var r) ? r : null;
}
