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

        // WeekendDrops crates only; this generator runs for every vanilla / third-party
        // RandomLootContainer too.
        if (!WdCrateRegistry.IsOurs(__0)) return;

        foreach (var group in __result)
        {
            if (group.Count == 0) continue;
            var root = group[0];

            var template = _itemHelper.GetItem(root.Template).Value;
            if (template is null) continue;

            // The generator emits a single loose round; fatten it to a full stack.
            if (template.Parent == AmmoParentId)
            {
                var max = template.Properties?.StackMaxSize ?? 1;
                if (max <= 1) continue;

                root.Upd ??= new Upd();
                root.Upd.StackObjectsCount = max;
                continue;
            }

            // The generator emits the bare box with no preset, so it arrives empty.
            if (template.Parent == AmmoBoxParentId)
            {
                // AddCartridgesToAmmoBox calls First(), so a template with no cartridge slot
                // (or a box that already has children) has to be skipped.
                if (group.Count > 1) continue;
                if (template.Properties?.StackSlots?.Any() != true) continue;
                _itemHelper.AddCartridgesToAmmoBox(group, template);
            }
        }
    }
}
