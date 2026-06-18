namespace WeekendDrops.Services;


public static class LocationUtil
{
   
    private static string Canonical(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        switch (s)
        {
            case "bigmap":
            case "customs":            return "customs";

            case "factory4_day":
            case "factory4_night":
            case "factory":            return "factory";

            case "woods":              return "woods";
            case "shoreline":          return "shoreline";
            case "interchange":        return "interchange";

            case "rezervbase":
            case "reserve":            return "reserve";

            case "lighthouse":         return "lighthouse";

            case "tarkovstreets":
            case "streets":
            case "streetsoftarkov":    return "streets";

            case "laboratory":
            case "lab":
            case "thelab":             return "lab";

            case "sandbox":
            case "sandbox_high":
            case "groundzero":         return "groundzero";

            case "labyrinth":          return "labyrinth";

            default:                   return s;
        }
    }

   
    public static bool Matches(string reported, string? target)
    {
        if (string.IsNullOrEmpty(target)) return true;
        return Canonical(reported) == Canonical(target);
    }
}
