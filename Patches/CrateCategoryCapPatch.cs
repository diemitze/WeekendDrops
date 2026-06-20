using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using WeekendDrops;

namespace WeekendDrops.Patches;

// Caps bulky wearables to one per crate. The loot generator picks each reward item
// independently from the weighted pool, so an Equipment crate could roll 3 backpacks
// (or 3 rigs, etc.). We hook PickRewardItem: the first item of a capped category is
// kept, but a second one is re-rolled to a category the crate isn't already using.
// Re-rolling at the tpl stage (not the result stage) lets SPT expand presets and the
// weapon-kit / ammo postfixes run on the replacement exactly as if it were picked.
public static class CrateCategoryCapPatch
{
    private static ItemHelper? _itemHelper;
    private static bool _applied;
    private static readonly Random Rng = new();

    // One item max per crate from each of these base classes (HEADWEAR also covers
    // helmets). VEST covers both armored and tactical rigs.
    private static readonly (MongoId Bc, string Key)[] Capped =
    {
        (BaseClasses.BACKPACK, "backpack"),
        (BaseClasses.ARMOR,    "armor"),
        (BaseClasses.VEST,     "rig"),
        (BaseClasses.HEADWEAR, "headwear"),
    };

    // Per-crate state. Loot for one crate is generated synchronously on one thread
    // (GetRandomLootContainerLoot loops PickRewardItem), so ThreadStatic is the right
    // scope and keeps concurrent crate generations from clobbering each other.
    [ThreadStatic] private static RewardDetails? _activeDetails;
    [ThreadStatic] private static HashSet<string>? _usedCategories;

    public static void Apply(ItemHelper itemHelper, ISptLogger<WeekendDropsLoader> logger)
    {
        _itemHelper = itemHelper;
        if (_applied) return;

        var gen = AccessTools.Method(
            typeof(LootGenerator), nameof(LootGenerator.GetRandomLootContainerLoot));
        var pick = AccessTools.Method(typeof(LootGenerator), "PickRewardItem");
        if (gen is null || pick is null)
        {
            logger.Error("[WeekendDrops] Could not find LootGenerator reward methods - crate category cap disabled");
            return;
        }

        var harmony = new Harmony("com.20fpsguy.WeekendDrops.CategoryCap");
        harmony.Patch(gen,
            prefix:    new HarmonyMethod(Method(nameof(GenPrefix))),
            finalizer: new HarmonyMethod(Method(nameof(GenFinalizer))));
        harmony.Patch(pick, postfix: new HarmonyMethod(Method(nameof(PickPostfix))));

        _applied = true;
    }

    private static MethodInfo Method(string name) =>
        typeof(CrateCategoryCapPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    // Open a per-crate tracking scope only for our crates.
    private static void GenPrefix(RewardDetails __0)
    {
        if (WdCrateRegistry.IsOurs(__0))
        {
            _activeDetails  = __0;
            _usedCategories = [];
        }
        else
        {
            _activeDetails  = null;
            _usedCategories = null;
        }
    }

    private static void GenFinalizer()
    {
        _activeDetails  = null;
        _usedCategories = null;
    }

    // ref the picked tpl so a capped-category duplicate gets swapped before the
    // generator expands it into items.
    private static void PickPostfix(RewardDetails __0, ref MongoId __result)
    {
        if (_itemHelper is null || _usedCategories is null || !ReferenceEquals(__0, _activeDetails))
            return;

        var key = CategoryKey(__result);
        if (key is null) return;                  // not a capped category, leave it
        if (_usedCategories.Add(key)) return;     // first of this category, allow it

        // Duplicate capped category: re-roll to a tpl whose category is still free.
        var replacement = RepickAvoidingUsed(__0);
        if (replacement is null) return;          // nothing better available, keep it

        __result = replacement.Value;
        var newKey = CategoryKey(__result);
        if (newKey is not null) _usedCategories.Add(newKey);
    }

    // First capped base class this tpl belongs to, or null if it's not a capped item.
    private static string? CategoryKey(MongoId tpl)
    {
        foreach (var (bc, key) in Capped)
            if (_itemHelper!.IsOfBaseclass(tpl, bc)) return key;
        return null;
    }

    // Weighted pick from the crate's own pool, skipping any tpl whose capped category
    // is already used this crate. Returns null when nothing suitable remains.
    private static MongoId? RepickAvoidingUsed(RewardDetails details)
    {
        var pool = details.RewardTplPool;
        if (pool is null || pool.Count == 0) return null;

        var allowed = new List<KeyValuePair<MongoId, double>>();
        double total = 0;
        foreach (var kv in pool)
        {
            if (kv.Value <= 0) continue;
            var key = CategoryKey(kv.Key);
            if (key is not null && _usedCategories!.Contains(key)) continue;
            allowed.Add(kv);
            total += kv.Value;
        }
        if (allowed.Count == 0 || total <= 0) return null;

        double r = Rng.NextDouble() * total;
        foreach (var kv in allowed)
        {
            r -= kv.Value;
            if (r <= 0) return kv.Key;
        }
        return allowed[^1].Key;
    }
}
