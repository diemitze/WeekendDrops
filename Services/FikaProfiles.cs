namespace WeekendDrops.Services;

public static class FikaProfiles
{
    public static bool IsHeadlessNickname(string nickname) =>
        !string.IsNullOrEmpty(nickname) &&
        nickname.StartsWith("headless_", System.StringComparison.OrdinalIgnoreCase);
}
