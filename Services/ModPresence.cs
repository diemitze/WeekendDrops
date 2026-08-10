namespace WeekendDrops.Services;

internal static class ModPresence
{
    public static bool LootNetInstalled => WdPaths.SiblingModExists("LootNetServer");

    public static bool ContentBackportInstalled => WdPaths.SiblingModExists("WTT-ContentBackport");
}
