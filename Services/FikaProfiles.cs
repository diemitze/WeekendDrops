namespace WeekendDrops.Services;

// Spots a Fika headless/dedicated profile by its "headless_<id>" nickname prefix (the reliable
// signal). Used to keep the headless out of the team board and gift list; it isn't a real player.
public static class FikaProfiles
{
    public static bool IsHeadlessNickname(string nickname) =>
        !string.IsNullOrEmpty(nickname) &&
        nickname.StartsWith("headless_", System.StringComparison.OrdinalIgnoreCase);
}
