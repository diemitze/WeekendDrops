using System.Reflection;
using SysPath = System.IO.Path;

namespace WeekendDrops.Services;

public static class WdPaths
{
    public static string ModDir { get; } = ResolveModDir();

    public static string ConfigDir { get; } = SysPath.Combine(ModDir, "config");

    public static string DataDir { get; } = SysPath.Combine(ModDir, "data");

    public static string ModsDir { get; } = SysPath.GetDirectoryName(ModDir)
                                            ?? SysPath.Combine(AppContext.BaseDirectory, "user", "mods");

    public static string Config(string file) => SysPath.Combine(ConfigDir, file);

    public static string Data(string file) => SysPath.Combine(DataDir, file);

    public static bool SiblingModExists(string folderName)
    {
        try
        {
            if (!Directory.Exists(ModsDir)) return false;
            return Directory.EnumerateDirectories(ModsDir).Any(d =>
                string.Equals(SysPath.GetFileName(d), folderName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static string ResolveModDir()
    {
        var asmDir = SysPath.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (string.IsNullOrEmpty(asmDir))
            return SysPath.Combine(AppContext.BaseDirectory, "user", "mods", "WeekendDrops");

        var dir = new DirectoryInfo(asmDir);
        for (int i = 0; i < 3 && dir is not null; i++)
        {
            if (Directory.Exists(SysPath.Combine(dir.FullName, "config"))) return dir.FullName;
            dir = dir.Parent;
        }

        return asmDir;
    }
}
