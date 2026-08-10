using SPTarkov.Server.Core.Models.Spt.Config;

namespace WeekendDrops.Patches;

public static class WdCrateRegistry
{
    private static readonly HashSet<RewardDetails> Ours =
        new(ReferenceEqualityComparer.Instance);

    public static void Register(RewardDetails details)
    {
        if (details != null) Ours.Add(details);
    }

    public static bool IsOurs(RewardDetails details) => details != null && Ours.Contains(details);
}
