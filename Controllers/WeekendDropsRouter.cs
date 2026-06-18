using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using WeekendDrops.Models;
using WeekendDrops.Services;

namespace WeekendDrops.Controllers;

[Injectable]
public class WeekendDropsRouter(JsonUtil jsonUtil, WeekendDropsCallback callback)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<EmptyRequestData>(
            "/weekenddrops/state",
            async (url, info, sessionId, output) => await callback.GetState(sessionId, url)
        ),
        new RouteAction<EmptyRequestData>(
            "/weekenddrops/dailystate",
            async (url, info, sessionId, output) => await callback.GetDailyState(sessionId, url)
        ),
        new RouteAction<StringIdRequest>(
            "/weekenddrops/claimdaily",
            async (url, info, sessionId, output) => await callback.ClaimDailyReward(sessionId, info.Id)
        ),
        new RouteAction<StringIdRequest>(
            "/weekenddrops/buyitem",
            async (url, info, sessionId, output) => await callback.BuyShopItem(sessionId, info.Id)
        ),
        new RouteAction<StringIdRequest>(
            "/weekenddrops/claimtier",
            async (url, info, sessionId, output) => await callback.ClaimTier(sessionId, info.Id)
        ),
        new RouteAction<StringIdRequest>(
            "/weekenddrops/depositgp",
            async (url, info, sessionId, output) => await callback.DepositGp(sessionId, info.Id)
        ),
        new RouteAction<StringIdRequest>(
            "/weekenddrops/debug",
            async (url, info, sessionId, output) => await callback.DebugAction(sessionId, info.Id)
        ),
        new RouteAction<RaidResultRequest>(
            "/weekenddrops/raidend",
            async (url, info, sessionId, output) => await callback.ReportRaidResult(sessionId, info)
        )
    ])
{ }

[Injectable]
public class WeekendDropsCallback(
    HttpResponseUtil httpResponseUtil,
    WeekendChallengeService challengeService,
    DailyChallengeService dailyService,
    GpBalanceService gpBalance)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private void DetectLootNetBridge(string url)
    {
        if (url == null || !url.Contains("lootnet=1")) return;
        challengeService.SetLootNetActive();
        dailyService.SetLootNetActive();
    }

    public ValueTask<string> GetState(MongoId sessionId, string url)
    {
        DetectLootNetBridge(url);
        var state = challengeService.GetClientState(sessionId);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> GetDailyState(MongoId sessionId, string url)
    {
        DetectLootNetBridge(url);
        var state = dailyService.GetDailyState(sessionId);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> ClaimDailyReward(MongoId sessionId, string challengeId)
    {
        var result = dailyService.ClaimDailyReward(sessionId, challengeId);
        var json = JsonSerializer.Serialize(new { result }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> BuyShopItem(MongoId sessionId, string itemId)
    {
        var result = dailyService.BuyShopItem(sessionId, itemId);
        var json = JsonSerializer.Serialize(new { result }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> ClaimTier(MongoId sessionId, string tierId)
    {
        bool result = int.TryParse(tierId, out int required)
            && challengeService.ClaimTier(sessionId, required);
        var json = JsonSerializer.Serialize(new { result }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }


    public ValueTask<string> DepositGp(MongoId sessionId, string countStr)
    {
        bool ok = int.TryParse(countStr, out int count) && count > 0;
        if (ok) gpBalance.Add(sessionId.ToString(), count);
        var json = JsonSerializer.Serialize(new { result = ok, deposited = ok ? count : 0 }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> DebugAction(MongoId sessionId, string action)
    {
        bool result;

        if (action != null && action.StartsWith("daily_", StringComparison.OrdinalIgnoreCase))
        {
            result = dailyService.DebugAction(sessionId, action.Substring("daily_".Length));
        }
        else
        {
            result = challengeService.DebugAction(sessionId, action);

            if (result && string.Equals(action, "resetprogress", StringComparison.OrdinalIgnoreCase))
                dailyService.ResetDailyProgress(sessionId);

            if (result && string.Equals(action, "reroll", StringComparison.OrdinalIgnoreCase))
                dailyService.RerollDaily(sessionId);
        }
        var json = JsonSerializer.Serialize(new { result }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> ReportRaidResult(MongoId sessionId, RaidResultRequest info)
    {

        int gpEarned = challengeService.ApplyRaidResult(sessionId, info)
                     + dailyService.ApplyRaidResult(sessionId, info);
        var json = JsonSerializer.Serialize(new { result = true, gpEarned }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }
}
