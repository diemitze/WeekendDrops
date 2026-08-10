using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using WeekendDrops.Models;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace WeekendDrops.Services;

[Injectable(InjectionType.Singleton)]
public class SquadService(
    ProfileHelper profileHelper,
    GpBalanceService gpBalance,
    WeekendChallengeService challengeService)
{
    public SquadStateDto GetSquad(string sessionId)
    {
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
            if (string.IsNullOrWhiteSpace(nick)) continue;
            if (FikaProfiles.IsHeadlessNickname(nick)) continue;

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

        dto.Rows.Sort((a, b) =>
        {
            int byEarned = b.GpEarnedWeekend.CompareTo(a.GpEarnedWeekend);
            return byEarned != 0 ? byEarned : b.GpBalance.CompareTo(a.GpBalance);
        });

        return dto;
    }
}
