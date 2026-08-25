using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;

namespace WeekendDrops.Patches;

/// Sorts a reward pool into the groups a crate recipe draws from. Base-class ids are
/// spelled out rather than taken from BaseClasses so the mapping is readable next to
/// the recipe it feeds.
public static class CrateGroups
{
    public const string Weapon = "weapon";
    public const string Gear   = "gear";
    public const string Ammo   = "ammo";
    public const string Meds   = "meds";
    public const string Barter = "barter";
    public const string Other  = "other";

    private static readonly (string Group, string[] BaseClasses)[] Map =
    {
        (Weapon, ["5422acb9af1c889c16000029"]),
        (Ammo,   ["543be5cb4bdc2deb348b4568", "5485a8684bdc2da71d8b4567"]),
        (Meds,   ["5448f39d4bdc2d0a728b4568", "5448f3a14bdc2d27728b4569", "5448f3a64bdc2d60728b456a",
                  "57864c8c245977548867e7f1", "5448f3ac4bdc2dce718b4569"]),
        (Gear,   ["5448e54d4bdc2dcc718b4568", "5448e5284bdc2dcb718b4567", "5a341c4086f77401f2541505",
                  "5448e53e4bdc2d60728b4567", "644120aa86ffbe10ee032b6f", "5645bcb74bdc2ded0b8b4578",
                  "5a341c4686f77469e155819e", "5448e5724bdc2ddf718b4568", "57bef4c42459772e8d35a53b"]),
        (Barter, ["5448eb774bdc2d0a728b4567", "5448e8d04bdc2ddf718b4569", "5448e8d64bdc2dce718b4568",
                  "543be6674bdc2df1348b4569", "543be5e94bdc2df1348b4568", "5795f317245977243854e041",
                  "5448ecbe4bdc2d60728b4568", "5447e0e74bdc2d3c308b4567"]),
    };

    public static string Of(ItemHelper itemHelper, MongoId tpl)
    {
        foreach (var (group, bases) in Map)
            foreach (var bc in bases)
                if (itemHelper.IsOfBaseclass(tpl, new MongoId(bc)))
                    return group;

        return Other;
    }
}
