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

// The generator picks each reward independently, so an Equipment crate could roll 3 backpacks.
// Re-rolls a repeat at the tpl stage, so presets and later postfixes still run.
public static class CrateCategoryCapPatch
{
    private static ItemHelper? _itemHelper;
    private static bool _applied;
    private static readonly Random Rng = new();

    // One item max per crate from each. VEST covers both armored and tactical rigs.
    private static readonly (MongoId Bc, string Key)[] Capped =
    {
        (BaseClasses.BACKPACK, "backpack"),
        (BaseClasses.ARMOR,    "armor"),
        (BaseClasses.VEST,     "rig"),
        (BaseClasses.HEADWEAR, "headwear"),
    };

    // One crate's loot is generated synchronously on one thread, so ThreadStatic keeps
    // concurrent crate generations from clobbering each other.
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

    // Per-crate tracking scope, WeekendDrops crates only.
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

    // Swaps a capped-category duplicate before the generator expands it into items.
    private static void PickPostfix(RewardDetails __0, ref MongoId __result)
    {
        if (_itemHelper is null || _usedCategories is null || !ReferenceEquals(__0, _activeDetails))
            return;

        var key = CategoryKey(__result);
        if (key is null) return;                  // not a capped category, leave it
        if (_usedCategories.Add(key)) return;     // first of this category, allow it

        var replacement = RepickAvoidingUsed(__0);
        if (replacement is null) return;          // nothing better available, keep it

        __result = replacement.Value;
        var newKey = CategoryKey(__result);
        if (newKey is not null) _usedCategories.Add(newKey);
    }

    // Null when the tpl isn't a capped item.
    private static string? CategoryKey(MongoId tpl)
    {
        foreach (var (bc, key) in Capped)
            if (_itemHelper!.IsOfBaseclass(tpl, bc)) return key;
        return null;
    }

    // Weighted pick from the crate's own pool. Null when nothing suitable remains.
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
