using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using WeekendDrops.Models;

namespace WeekendDrops.Services;

// Builds the Fika team board: one row per real profile with its GP earned this weekend.
// Pure server-side read. Excludes headless/fresh accounts, like GpGiftService.ListFriends.
[Injectable(InjectionType.Singleton)]
public class SquadService(
    ProfileHelper profileHelper,
    GpBalanceService gpBalance,
    WeekendChallengeService challengeService)
{
    public SquadStateDto GetSquad(string sessionId)
    {
        // Roll the "earned this weekend" tally if the period changed (idempotent).
        gpBalance.RollWeeklyPeriod(challengeService.GetCurrentWeekendId());

        var dto = new SquadStateDto();

        Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Profile.SptProfile> profiles;
        try { profiles = profileHelper.GetProfiles(); }
        catch { return dto; }

        foreach (var id in profiles.Keys)
        {
            var idStr = id.ToString();

            string? nick;
            try { nick = profileHelper.GetPmcProfile(id)?.Info?.Nickname; }
            catch { nick = null; }
            if (string.IsNullOrWhiteSpace(nick)) continue;       // fresh accounts
            if (FikaProfiles.IsHeadlessNickname(nick)) continue; // the Fika headless host

            var (done, total) = challengeService.GetWeeklyProgress(id);

            dto.Rows.Add(new SquadRowDto
            {
                Nickname        = nick,
                GpBalance       = gpBalance.Get(idStr),
                GpEarnedWeekend = gpBalance.GetWeeklyEarned(idStr),
                WeeklyDone      = done,
                WeeklyTotal     = total,
                IsYou           = idStr == sessionId,
            });
        }

        // Rivalry order: most GP earned this weekend first, balance breaks ties.
        dto.Rows.Sort((a, b) =>
        {
            int byEarned = b.GpEarnedWeekend.CompareTo(a.GpEarnedWeekend);
            return byEarned != 0 ? byEarned : b.GpBalance.CompareTo(a.GpBalance);
        });

        return dto;
    }
}
