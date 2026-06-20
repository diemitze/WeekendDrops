using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using WeekendDrops;

namespace WeekendDrops.Patches;


public static class LootContainerAmmoStackPatch
{
    private const string AmmoParentId = "5485a8684bdc2da71d8b4567";
    private const string AmmoBoxParentId = "543be5cb4bdc2deb348b4568";

    private static ItemHelper? _itemHelper;
    private static bool _applied;

    public static void Apply(ItemHelper itemHelper, ISptLogger<WeekendDropsLoader> logger)
    {
        _itemHelper = itemHelper;
        if (_applied) return;

        var target = AccessTools.Method(
            typeof(LootGenerator),
            nameof(LootGenerator.GetRandomLootContainerLoot));
        if (target is null)
        {
            logger.Error("[WeekendDrops] Could not find LootGenerator.GetRandomLootContainerLoot - ammo stacking disabled");
            return;
        }

        var postfix = typeof(LootContainerAmmoStackPatch)
            .GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic);

        new Harmony("com.20fpsguy.WeekendDrops.AmmoStack")
            .Patch(target, postfix: new HarmonyMethod(postfix));

        _applied = true;
    }

    private static void Postfix(RewardDetails __0, List<List<Item>> __result)
    {
        if (_itemHelper is null || __result is null) return;

        // Only fatten ammo stacks in OUR crates - this generator runs for every
        // vanilla / third-party RandomLootContainer too.
        if (!WdCrateRegistry.IsOurs(__0)) return;

        foreach (var group in __result)
        {
            if (group.Count == 0) continue;
            var root = group[0];

            var template = _itemHelper.GetItem(root.Template).Value;
            if (template is null) continue;

            // Loose ammo: fatten the single round up to a full stack.
            if (template.Parent == AmmoParentId)
            {
                var max = template.Properties?.StackMaxSize ?? 1;
                if (max <= 1) continue;

                root.Upd ??= new Upd();
                root.Upd.StackObjectsCount = max;
                continue;
            }

            // Ammo boxes: GetRandomLootContainerLoot emits only the bare box (no
            // preset), so it arrives empty. Fill it with its cartridges the same
            // way the static-loot path does.
            if (template.Parent == AmmoBoxParentId)
            {
                // Skip if it somehow already has cartridge children, or the template
                // has no cartridge slot to read (AddCartridgesToAmmoBox calls First()).
                if (group.Count > 1) continue;
                if (template.Properties?.StackSlots?.Any() != true) continue;
                _itemHelper.AddCartridgesToAmmoBox(group, template);
            }
        }
    }
}
