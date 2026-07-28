namespace WeekendDrops.Services;

// Spots a Fika headless profile by its "headless_<id>" nickname, to keep it out of the team
// board and gift list.
public static class FikaProfiles
{
    public static bool IsHeadlessNickname(string nickname) =>
        !string.IsNullOrEmpty(nickname) &&
        nickname.StartsWith("headless_", System.StringComparison.OrdinalIgnoreCase);
}
