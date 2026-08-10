using System.Globalization;
using WeekendDrops.Models;

namespace WeekendDrops.Services;

public static class WeekendWindow
{
    public static bool IsActive(ModConfig config)
    {
        if (config.DebugMode) return true;

        var now = DateTime.Now;
        var day = (int)now.DayOfWeek;
        var hour = now.Hour;

        bool afterStart = day > config.WeekendStartDay
            || (day == config.WeekendStartDay && hour >= config.WeekendStartHour);

        bool beforeEnd = day < config.WeekendEndDay
            || (day == config.WeekendEndDay && hour < config.WeekendEndHour);

        return afterStart && (day != 0 || beforeEnd)
               || (day == 0)
               || (day == config.WeekendEndDay && hour < config.WeekendEndHour);
    }

    public static string CurrentId(ModConfig config)
    {
        var now = DateTime.Now;
        int daysSinceStart = (((int)now.DayOfWeek - config.WeekendStartDay) % 7 + 7) % 7;
        var anchor = now.Date.AddDays(-daysSinceStart);
        return anchor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string ScheduleText(ModConfig config)
    {
        var culture = CultureInfo.CurrentCulture;
        string Day(int d) => culture.DateTimeFormat.AbbreviatedDayNames[((d % 7) + 7) % 7];
        string Time(int h) => new TimeOnly(((h % 24) + 24) % 24, 0).ToString("t", culture);
        return $"{Day(config.WeekendStartDay)} {Time(config.WeekendStartHour)} to " +
               $"{Day(config.WeekendEndDay)} {Time(config.WeekendEndHour)}";
    }

    public static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (var c in s) h = (h ^ c) * 16777619u;

            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;

            return (int)(h & 0x7fffffff);
        }
    }
}
