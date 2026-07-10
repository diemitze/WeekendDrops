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
            "/weekenddrops/claimdailybonus",
            async (url, info, sessionId, output) => await callback.ClaimDailyBonus(sessionId)
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
        ),
        new RouteAction<EmptyRequestData>(
            "/weekenddrops/contracts",
            async (url, info, sessionId, output) => await callback.GetContractsState(sessionId)
        ),
        new RouteAction<StringIdRequest>(
            "/weekenddrops/acceptcontract",
            async (url, info, sessionId, output) => await callback.AcceptContract(sessionId, info.Id)
        ),
        new RouteAction<StringIdRequest>(
            "/weekenddrops/abandoncontract",
            async (url, info, sessionId, output) => await callback.AbandonContract(sessionId)
        ),
        new RouteAction<ContractResultRequest>(
            "/weekenddrops/contractresult",
            async (url, info, sessionId, output) => await callback.ReportContractResult(sessionId, info)
        ),
        new RouteAction<ClientFlagsRequest>(
            "/weekenddrops/clientflags",
            async (url, info, sessionId, output) => await callback.SetClientFlags(sessionId, info)
        ),
        new RouteAction<EmptyRequestData>(
            "/weekenddrops/friends",
            async (url, info, sessionId, output) => await callback.GetFriends(sessionId)
        ),
        new RouteAction<EmptyRequestData>(
            "/weekenddrops/squad",
            async (url, info, sessionId, output) => await callback.GetSquad(sessionId)
        ),
        new RouteAction<GiftRequest>(
            "/weekenddrops/giftgp",
            async (url, info, sessionId, output) => await callback.SendGift(sessionId, info)
        )
    ])
{ }

[Injectable]
public class WeekendDropsCallback(
    HttpResponseUtil httpResponseUtil,
    WeekendChallengeService challengeService,
    DailyChallengeService dailyService,
    ContractService contractService,
    GpBalanceService gpBalance,
    GpGiftService giftService,
    SquadService squadService)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Client pushes its BepInEx-side toggles (LootNET bridge, F12 no-Scav) to
    // /weekenddrops/clientflags in the body: SPT's router strips the query string, so a URL tag
    // never arrives. Both sticky for the server run.
    public ValueTask<string> SetClientFlags(MongoId sessionId, ClientFlagsRequest info)
    {
        if (info is { NoScav: true })
        {
            challengeService.SetScavChallengesDisabled();
            dailyService.SetScavChallengesDisabled();
        }

        if (info is { LootNet: true })
        {
            challengeService.SetLootNetActive();
            dailyService.SetLootNetActive();
        }

        var json = JsonSerializer.Serialize(new { result = true }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> GetState(MongoId sessionId, string url)
    {
        var state = challengeService.GetClientState(sessionId);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> GetDailyState(MongoId sessionId, string url)
    {
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

    public ValueTask<string> ClaimDailyBonus(MongoId sessionId)
    {
        var result = dailyService.ClaimDailyBonus(sessionId);
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

    // The Fika squad rivalry board: every real profile on the server with its GP standing.
    public ValueTask<string> GetSquad(MongoId sessionId)
    {
        var state = squadService.GetSquad(sessionId.ToString());
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    // The recipient picker: every other real profile on the server.
    public ValueTask<string> GetFriends(MongoId sessionId)
    {
        var state = new FriendsStateDto { Friends = giftService.ListFriends(sessionId.ToString()) };
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    // Gift GP to another profile. Server validates the target and the sender's balance;
    // returns an outcome code ("ok" / "bad_amount" / "bad_target" / "insufficient_gp").
    public ValueTask<string> SendGift(MongoId sessionId, GiftRequest info)
    {
        var result = giftService.SendGift(sessionId.ToString(), info?.ToId ?? "", info?.Amount ?? 0);
        var json = JsonSerializer.Serialize(new { result }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> DebugAction(MongoId sessionId, string action)
    {
        bool result;

        if (action != null && action.StartsWith("daily_", StringComparison.OrdinalIgnoreCase))
        {
            result = dailyService.DebugAction(sessionId, action.Substring("daily_".Length));
        }
        else if (action != null && action.StartsWith("contract_", StringComparison.OrdinalIgnoreCase))
        {
            result = contractService.DebugAction(sessionId, action.Substring("contract_".Length));
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

    // Contracts

    public ValueTask<string> GetContractsState(MongoId sessionId)
    {
        var state = contractService.GetContractsState(sessionId);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> AcceptContract(MongoId sessionId, string contractId)
    {
        var result = contractService.AcceptContract(sessionId, contractId);
        var json = JsonSerializer.Serialize(new { result }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> AbandonContract(MongoId sessionId)
    {
        var result = contractService.AbandonContract(sessionId);
        var json = JsonSerializer.Serialize(new { result }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }

    public ValueTask<string> ReportContractResult(MongoId sessionId, ContractResultRequest info)
    {
        var result = contractService.CompleteContract(sessionId, info);
        var json = JsonSerializer.Serialize(new { result }, JsonOptions);
        return new ValueTask<string>(httpResponseUtil.GetBody(json));
    }
}
