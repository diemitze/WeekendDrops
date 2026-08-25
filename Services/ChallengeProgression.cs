using WeekendDrops.Models;

namespace WeekendDrops.Services;

/// How one raid moves one challenge along. Weekly and daily share this so a new
/// ChallengeType only ever has to be handled in one place.
public static class ChallengeProgression
{
    /// Returns the challenge's new Current. Types not listed here are driven from
    /// somewhere else (or not at all) and are left untouched.
    public static int Advance(
        IChallengeDefinition def,
        int current,
        int target,
        RaidResultRequest r,
        float survivalTimeBank,
        int totalKills)
    {
        switch (def.Type)
        {
            case ChallengeType.KillScavs:               return current + r.ScavKills;
            case ChallengeType.KillPMCs:                return current + r.PmcKills;
            case ChallengeType.KillBoss:
                return string.IsNullOrEmpty(def.TargetBoss)
                    ? current + r.BossKills
                    : current + r.BossRoles.Count(role => BossUtil.Matches(role, def.TargetBoss));
            case ChallengeType.KillCultists:            return current + r.CultistKills;
            case ChallengeType.KillPriest:              return current + r.PriestKills;
            case ChallengeType.KillRaiders:             return current + r.RaiderKills;
            case ChallengeType.KillRogues:              return current + r.RogueKills;
            case ChallengeType.MeleeKills:              return current + r.MeleeKills;
            case ChallengeType.KillsCumulative:         return current + totalKills;
            case ChallengeType.KillHeadshots:           return current + r.Headshots;
            case ChallengeType.GrenadeKills:            return current + r.GrenadeKills;
            case ChallengeType.KillLegs:                return current + r.LegKills;
            case ChallengeType.KillArms:                return current + r.ArmKills;
            case ChallengeType.KillStomach:             return current + r.StomachKills;
            case ChallengeType.KillsSuppressed:         return current + r.SuppressedKills;
            case ChallengeType.KillsWithOptic:          return current + r.OpticKills;
            case ChallengeType.KillsIronSights:         return current + r.IronSightKills;

            // One-raid feats: hit the number in a single raid or it does not count.
            case ChallengeType.KillsSingleRaid:         return Feat(current, target, totalKills   >= target);
            case ChallengeType.KillPMCsSingleRaid:      return Feat(current, target, r.PmcKills   >= target);
            case ChallengeType.KillScavsSingleRaid:     return Feat(current, target, r.ScavKills  >= target);
            case ChallengeType.KillHeadshotsSingleRaid: return Feat(current, target, r.Headshots  >= target);
            case ChallengeType.SurviveTimeSingleRaid:   return Feat(current, target, r.Survived && r.SurvivedSeconds >= target);
            case ChallengeType.ExtractWithLootValue:    return Feat(current, target, r.Survived && r.LootValue >= target);
            case ChallengeType.ScavKillsSingleRaid:     return Feat(current, target, r.IsScavRaid && totalKills >= target);

            case ChallengeType.SurviveTimeCumulative:   return (int)survivalTimeBank;
            case ChallengeType.ExtractSuccessfully:     return current + (r.Survived ? 1 : 0);
            case ChallengeType.RaidsDone:               return current + (r.IsScavRaid ? 0 : 1);
            case ChallengeType.LootValueCumulative:     return current + (r.Survived ? r.LootValue : 0);

            case ChallengeType.ScavExtract:             return current + (r.IsScavRaid && r.Survived ? 1 : 0);
            case ChallengeType.ScavRaidsDone:           return current + (r.IsScavRaid ? 1 : 0);
            case ChallengeType.ScavKills:               return current + (r.IsScavRaid ? totalKills : 0);

            case ChallengeType.ExtractFromLocation:
                return current + (r.Survived && AtLocation(r, def.TargetLocation) ? 1 : 0);

            case ChallengeType.ScavExtractFromLocation:
                return current + (r.IsScavRaid && r.Survived && AtLocation(r, def.TargetLocation) ? 1 : 0);

            case ChallengeType.KillsAtDistance:
                return current + r.KillDistances.Count(d => d >= def.MinDistanceMeters);

            case ChallengeType.KillsAtDistanceSingleRaid:
                return Feat(current, target, r.KillDistances.Count(d => d >= def.MinDistanceMeters) >= target);

            case ChallengeType.KillsWithWeaponClass:
                return current + WeaponClassKills(r, def.TargetWeaponClass);

            default:
                return current;
        }
    }

    private static int Feat(int current, int target, bool achieved) => achieved ? target : current;

    private static bool AtLocation(RaidResultRequest r, string? targetLocation) =>
        !string.IsNullOrEmpty(targetLocation) && LocationUtil.Matches(r.Location, targetLocation);

    private static int WeaponClassKills(RaidResultRequest r, string? weapClass)
    {
        if (string.IsNullOrEmpty(weapClass)) return 0;
        return r.WeaponClassKills
            .Where(kv => string.Equals(kv.Key, weapClass, StringComparison.OrdinalIgnoreCase))
            .Sum(kv => kv.Value);
    }
}
