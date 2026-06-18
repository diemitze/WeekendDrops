using SPTarkov.Server.Core.Models.Spt.Config;

namespace WeekendDrops.Patches;

// Tracks the RewardDetails instances Weekend Drops registers for its OWN drop
// crates (the per-tier reward crates + the paid Arena crates). The loot postfixes
// use this to scope their effects to our crates only: LootGenerator.
// GetRandomLootContainerLoot fires for every vanilla / third-party
// RandomLootContainer as well, and we don't want to kit weapons or fatten ammo
// stacks in those.
public static class WdCrateRegistry
{
    // Reference identity: the exact RewardDetails instance we store in
    // InventoryConfig.RandomLootContainers is the one passed to the generator, so
    // a reference-keyed set is enough (and immune to any DTO Equals override).
    private static readonly HashSet<RewardDetails> Ours =
        new(ReferenceEqualityComparer.Instance);

    public static void Register(RewardDetails details)
    {
        if (details != null) Ours.Add(details);
    }

    public static bool IsOurs(RewardDetails details) => details != null && Ours.Contains(details);
}
