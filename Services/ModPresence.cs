using System;
using System.IO;
using SysPath = System.IO.Path;

namespace WeekendDrops.Services;

// Detects sibling SPT mods by their install folder, so LootNET-gated content can enable
// itself without the player flipping a config flag.
internal static class ModPresence
{
    private static readonly string ModsDir =
        SysPath.Combine(AppContext.BaseDirectory, "user", "mods");

    // Windows paths are case-insensitive, so the canonical folder name is enough.
    public static bool LootNetInstalled => Directory.Exists(SysPath.Combine(ModsDir, "LootNetServer"));
}
