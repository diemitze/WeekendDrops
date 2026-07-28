using SPTarkov.Server.Core.Models.Spt.Config;

namespace WeekendDrops.Patches;

// Scopes the loot postfixes to WeekendDrops' own crates. GetRandomLootContainerLoot fires for
// every vanilla/third-party RandomLootContainer too, which must be left alone.
public static class WdCrateRegistry
{
    // The exact instance stored in InventoryConfig.RandomLootContainers is the one handed to the
    // generator, so a reference-keyed set is enough and survives any DTO Equals override.
    private static readonly HashSet<RewardDetails> Ours =
        new(ReferenceEqualityComparer.Instance);

    public static void Register(RewardDetails details)
    {
        if (details != null) Ours.Add(details);
    }

    public static bool IsOurs(RewardDetails details) => details != null && Ours.Contains(details);
}
