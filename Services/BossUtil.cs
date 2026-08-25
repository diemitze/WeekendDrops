namespace WeekendDrops.Services;

public static class BossUtil
{
    /// Boss challenges are authored with friendly names; the client reports the raw
    /// WildSpawnType. Both collapse to the same key here.
    private static string Canonical(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("boss")) s = s.Substring(4);

        switch (s)
        {
            case "bully":
            case "reshala":     return "reshala";

            case "kojaniy":
            case "shturman":    return "shturman";

            case "gluhar":
            case "glukhar":     return "glukhar";

            case "killa":       return "killa";
            case "sanitar":     return "sanitar";
            case "tagilla":     return "tagilla";
            case "zryachiy":    return "zryachiy";
            case "boar":
            case "kaban":       return "kaban";
            case "kolontay":    return "kolontay";
            case "partisan":    return "partisan";

            case "knight":
            case "goons":       return "knight";

            case "sectantpriest":
            case "priest":      return "priest";

            default:            return s;
        }
    }

    public static bool Matches(string reported, string? target)
    {
        if (string.IsNullOrEmpty(target)) return true;
        return Canonical(reported) == Canonical(target);
    }
}
