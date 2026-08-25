using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Enums;
using WeekendDrops.Services;

namespace WeekendDrops.Patches;

/// Drives crate contents from the tier's recipe: reward slot N is filled from slot N's
/// group instead of the whole pool. Also keeps the old rule that a crate never holds two
/// of the same wearable category.
public static class CrateCompositionPatch
{
    private static ItemHelper? _itemHelper;
    private static bool _applied;
    private static readonly Random Rng = new();

    private static readonly (MongoId Bc, string Key)[] Capped =
    {
        (BaseClasses.BACKPACK, "backpack"),
        (BaseClasses.ARMOR,    "armor"),
        (BaseClasses.VEST,     "rig"),
        (BaseClasses.HEADWEAR, "headwear"),
    };

    [ThreadStatic] private static RewardDetails? _activeDetails;
    [ThreadStatic] private static HashSet<string>? _usedCategories;
    [ThreadStatic] private static CrateRecipe? _recipe;
    [ThreadStatic] private static List<string>? _rolledSlots;
    [ThreadStatic] private static int _drawIndex;

    public static void Apply(ItemHelper itemHelper, ISptLogger<WeekendDropsLoader> logger)
    {
        _itemHelper = itemHelper;
        if (_applied) return;

        var gen = AccessTools.Method(
            typeof(LootGenerator), nameof(LootGenerator.GetRandomLootContainerLoot));
        var pick = AccessTools.Method(typeof(LootGenerator), "PickRewardItem");
        if (gen is null || pick is null)
        {
            logger.Error("[WeekendDrops] Could not find LootGenerator reward methods - crate composition disabled");
            return;
        }

        var harmony = new Harmony("com.20fpsguy.WeekendDrops.CrateComposition");
        harmony.Patch(gen,
            prefix:    new HarmonyMethod(Method(nameof(GenPrefix))),
            finalizer: new HarmonyMethod(Method(nameof(GenFinalizer))));
        harmony.Patch(pick, postfix: new HarmonyMethod(Method(nameof(PickPostfix))));

        _applied = true;
    }

    private static MethodInfo Method(string name) =>
        typeof(CrateCompositionPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void GenPrefix(RewardDetails __0)
    {
        var recipe = WdCrateRegistry.RecipeFor(__0);
        if (recipe is null)
        {
            Clear();
            return;
        }

        _activeDetails  = __0;
        _usedCategories = [];
        _recipe         = recipe;
        _rolledSlots    = recipe.Slots.Count > 0 ? recipe.Roll(Rng) : null;
        _drawIndex      = 0;
    }

    private static void GenFinalizer() => Clear();

    private static void Clear()
    {
        _activeDetails  = null;
        _usedCategories = null;
        _recipe         = null;
        _rolledSlots    = null;
        _drawIndex      = 0;
    }

    private static void PickPostfix(RewardDetails __0, ref MongoId __result)
    {
        if (_itemHelper is null || _usedCategories is null || !ReferenceEquals(__0, _activeDetails))
            return;

        int index = _drawIndex++;

        var group = _rolledSlots is not null && _rolledSlots.Count > index && _rolledSlots[index].Length > 0
                  ? _rolledSlots[index] : null;
        if (group is not null)
        {
            var fromGroup = PickFrom(_recipe!.Groups.TryGetValue(group, out var g) ? g : null);
            if (fromGroup is not null) __result = fromGroup.Value;
        }

        var key = CategoryKey(__result);
        if (key is null) return;
        if (_usedCategories.Add(key)) return;

        var replacement = RepickAvoidingUsed(__0, group);
        if (replacement is null) return;

        __result = replacement.Value;
        var newKey = CategoryKey(__result);
        if (newKey is not null) _usedCategories.Add(newKey);
    }

    private static MongoId? PickFrom(List<KeyValuePair<MongoId, double>>? entries)
    {
        if (entries is null || entries.Count == 0) return null;

        double total = 0;
        foreach (var kv in entries) if (kv.Value > 0) total += kv.Value;
        if (total <= 0) return null;

        double r = Rng.NextDouble() * total;
        foreach (var kv in entries)
        {
            if (kv.Value <= 0) continue;
            r -= kv.Value;
            if (r <= 0) return kv.Key;
        }
        return entries[^1].Key;
    }

    private static string? CategoryKey(MongoId tpl)
    {
        foreach (var (bc, key) in Capped)
            if (_itemHelper!.IsOfBaseclass(tpl, bc)) return key;
        return null;
    }

    /// Repick avoiding categories already in the crate, staying inside the slot's group
    /// when it has one so a duplicate helmet cannot turn into a second gun.
    private static MongoId? RepickAvoidingUsed(RewardDetails details, string? group)
    {
        IEnumerable<KeyValuePair<MongoId, double>>? source =
            group is not null && _recipe!.Groups.TryGetValue(group, out var g) ? g : details.RewardTplPool;
        if (source is null) return null;

        var allowed = new List<KeyValuePair<MongoId, double>>();
        foreach (var kv in source)
        {
            if (kv.Value <= 0) continue;
            var key = CategoryKey(kv.Key);
            if (key is not null && _usedCategories!.Contains(key)) continue;
            allowed.Add(kv);
        }
        return PickFrom(allowed);
    }
}
