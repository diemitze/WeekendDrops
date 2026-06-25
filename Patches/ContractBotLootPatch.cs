using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Utils;
using WeekendDrops.Services;

namespace WeekendDrops.Patches;

// Forces a contract's bonusItems onto every bot that contract spawns (matched by role).
// Reuses the generator's own AddLootFromPool so placement, container space, and parenting
// are handled exactly like normal bot loot. Scoped to the active contract's spawn roles,
// so unrelated bots are untouched.
public static class ContractBotLootPatch
{
    private static ContractService? _contracts;
    private static ISptLogger<WeekendDropsLoader>? _logger;
    private static MethodInfo? _addLoot;
    private static bool _applied;

    // Roomy containers to try; AddLootFromPool places the item wherever it fits.
    private static readonly EquipmentSlots[] CandidateSlots =
    {
        EquipmentSlots.Backpack, EquipmentSlots.TacticalVest, EquipmentSlots.Pockets
    };

    public static void Apply(ContractService contracts, ISptLogger<WeekendDropsLoader> logger)
    {
        _contracts = contracts;
        _logger = logger;
        if (_applied) return;

        var target = AccessTools.Method(typeof(BotLootGenerator), nameof(BotLootGenerator.GenerateLoot));
        _addLoot   = AccessTools.Method(typeof(BotLootGenerator), "AddLootFromPool");
        if (target is null || _addLoot is null)
        {
            logger.Error("[WeekendDrops] Could not find BotLootGenerator methods - contract bot loot disabled");
            return;
        }

        var postfix = typeof(ContractBotLootPatch)
            .GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic);

        new Harmony("com.20fpsguy.WeekendDrops.ContractBotLoot")
            .Patch(target, postfix: new HarmonyMethod(postfix));

        _applied = true;
    }

    // GenerateLoot(MongoId botId, MongoId sessionId, BotType botJsonTemplate,
    //              BotGenerationDetails botGenerationDetails, BotBaseInventory botInventory)
    private static void Postfix(MongoId botId, MongoId sessionId,
        BotGenerationDetails botGenerationDetails, BotBaseInventory botInventory, BotLootGenerator __instance)
    {
        if (_contracts is null || _addLoot is null || botGenerationDetails is null || botInventory is null) return;

        var items = _contracts.BonusItemsForRole(sessionId, botGenerationDetails.RoleLowercase);
        if (items.Count == 0) return;

        foreach (var tpl in items)
        {
            try
            {
                var pool  = new Dictionary<MongoId, double> { { new MongoId(tpl), 1 } };
                var slots = new HashSet<EquipmentSlots>(CandidateSlots);
                // (botId, pool, slots, count, inventory, role, spawnLimits=null, valueLimit=0=none, isPmc=false)
                _addLoot.Invoke(__instance, new object?[]
                {
                    botId, pool, slots, 1.0, botInventory, botGenerationDetails.RoleLowercase, null, 0.0, false
                });
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[WeekendDrops] Contract bot loot: failed to add {tpl}: {ex.Message}");
            }
        }
    }
}
