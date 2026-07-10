using System;
using System.IO;
using SysPath = System.IO.Path;

namespace WeekendDrops.Services;

// Cheap server-side detection of sibling SPT mods by their install folder under
// user/mods. Used so LootNET-gated content can enable itself automatically when the
// LootNET server mod is installed, without the player having to flip a config flag.
internal static class ModPresence
{
    private static readonly string ModsDir =
        SysPath.Combine(AppContext.BaseDirectory, "user", "mods");

    // True when LootNET's server mod (package name "LootNetServer") is installed.
    // Windows paths are case-insensitive, so the canonical folder name is enough.
    public static bool LootNetInstalled => Directory.Exists(SysPath.Combine(ModsDir, "LootNetServer"));
}
