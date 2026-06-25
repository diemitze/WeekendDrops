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

public static class WeaponKitPatch
{
    private static ItemHelper? _itemHelper;
    private static bool _applied;
    private static readonly Random Rng = new();

    private static readonly (string Prefix, double Chance)[] TargetSlots =
    {
        ("mod_scope",    0.55), // optic - the biggest visual/feel upgrade
        ("mod_muzzle",   0.45), // suppressor / brake / flash hider
        ("mod_foregrip", 0.50), // vertical / angled foregrip
        ("mod_tactical", 0.40), // flashlight / laser
    };

    public static void Apply(ItemHelper itemHelper, ISptLogger<WeekendDropsLoader> logger)
    {
        _itemHelper = itemHelper;
        if (_applied) return;

        var target = AccessTools.Method(
            typeof(LootGenerator),
            nameof(LootGenerator.GetRandomLootContainerLoot));
        if (target is null)
        {
            logger.Error("[WeekendDrops] Could not find LootGenerator.GetRandomLootContainerLoot - weapon kitting disabled");
            return;
        }

        var postfix = typeof(WeaponKitPatch)
            .GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic);

        new Harmony("com.20fpsguy.WeekendDrops.WeaponKit")
            .Patch(target, postfix: new HarmonyMethod(postfix));

        _applied = true;
    }

    private static void Postfix(RewardDetails __0, List<List<Item>> __result)
    {
        if (_itemHelper is null || __result is null) return;

        // Only kit weapons from OUR crates - this generator runs for every vanilla /
        // third-party RandomLootContainer too.
        if (!WdCrateRegistry.IsOurs(__0)) return;

        foreach (var group in __result)
        {
            if (group.Count == 0) continue;
            var root = group[0];

            if (!_itemHelper.IsOfBaseclass(root.Template, BaseClasses.WEAPON)) continue;

            KitWeapon(group);
        }
    }

    private static void KitWeapon(List<Item> group)
    {

        var hosts = group.ToList();

        foreach (var (prefix, chance) in TargetSlots)
        {
            if (Rng.NextDouble() > chance) continue;
            TryFillSlotType(group, hosts, prefix);
        }
    }

    private static void TryFillSlotType(List<Item> group, List<Item> hosts, string prefix)
    {
        foreach (var host in hosts)
        {
            var hostDb = _itemHelper!.GetItem(host.Template).Value;
            var slots = hostDb?.Properties?.Slots;
            if (slots is null) continue;

            foreach (var slot in slots)
            {
                var slotName = slot.Name;
                if (string.IsNullOrEmpty(slotName) || !slotName.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                // Already occupied by the preset? Leave it.
                var hostId = host.Id.ToString();
                if (group.Any(i => i.ParentId == hostId && i.SlotId == slotName))
                    continue;

                var modTpl = PickModForSlot(slot, prefix);
                if (modTpl is null) continue;

                group.Add(new Item
                {
                    Id = new MongoId(),
                    Template = modTpl.Value,
                    ParentId = host.Id,
                    SlotId = slotName,
                });
                return; // one fill per slot-type
            }
        }
    }

    private static MongoId? PickModForSlot(Slot slot, string prefix)
    {
        var filter = slot.Properties?.Filters?.FirstOrDefault()?.Filter;
        if (filter is null || filter.Count == 0) return null;

        var candidates = new List<MongoId>();
        var suppressors = new List<MongoId>();

        foreach (var tpl in filter)
        {
            var db = _itemHelper!.GetItem(tpl).Value;
            if (db?.Properties is null) continue;

            // Reject anything that itself needs sub-mods to be a valid item.
            var needsChildren = db.Properties.Slots?.Any(s => s.Required == true) ?? false;
            if (needsChildren) continue;

            candidates.Add(tpl);
            if (db.Parent == BaseClasses.SILENCER) suppressors.Add(tpl);
        }

        if (candidates.Count == 0) return null;

        var pickFrom = prefix == "mod_muzzle" && suppressors.Count > 0 ? suppressors : candidates;
        return pickFrom[Rng.Next(pickFrom.Count)];
    }
}
