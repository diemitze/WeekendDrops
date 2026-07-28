using System.Text.Json;
using SysPath = System.IO.Path;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using WeekendDrops.Models;

namespace WeekendDrops.Services;

// Pure server-side balance transfer, no stash edits (PvE would clobber those). Credited at
// send time; the recipient's toast is queued and drained on their next /state poll.
[Injectable(InjectionType.Singleton)]
public class GpGiftService
{
    private readonly ProfileHelper _profileHelper;
    private readonly GpBalanceService _gpBalance;

    private readonly string _file = SysPath.Combine(
        AppContext.BaseDirectory, "user", "mods", "WeekendDrops", "data", "gp_gifts.json");

    private readonly object _lock = new();
    // recipient sessionId -> queued gifts.
    private Dictionary<string, List<PendingGift>> _pending = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GpGiftService(ProfileHelper profileHelper, GpBalanceService gpBalance)
    {
        _profileHelper = profileHelper;
        _gpBalance = gpBalance;
        Load();
    }

    // Excludes the sender and any fresh or headless account. Balances are never exposed.
    public List<GiftFriendDto> ListFriends(string sessionId)
    {
        var result = new List<GiftFriendDto>();
        Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Profile.SptProfile> profiles;
        try { profiles = _profileHelper.GetProfiles(); }
        catch { return result; }

        foreach (var id in profiles.Keys)
        {
            var idStr = id.ToString();
            if (idStr == sessionId) continue;

            string? nick;
            try { nick = _profileHelper.GetPmcProfile(id)?.Info?.Nickname; }
            catch { nick = null; }
            if (string.IsNullOrWhiteSpace(nick)) continue;
            if (FikaProfiles.IsHeadlessNickname(nick)) continue; // never gift to the headless host

            result.Add(new GiftFriendDto { Id = idStr, Nickname = nick });
        }

        result.Sort((a, b) => string.Compare(a.Nickname, b.Nickname, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    // Never trusts the client for target or amount. Returns ok, bad_amount, bad_target
    // or insufficient_gp.
    public string SendGift(string fromId, string toId, int amount)
    {
        if (amount <= 0) return "bad_amount";
        if (string.IsNullOrEmpty(toId) || toId == fromId) return "bad_target";

        bool targetExists;
        try { targetExists = _profileHelper.GetPmcProfile(new MongoId(toId)) is not null; }
        catch { targetExists = false; }
        if (!targetExists) return "bad_target";

        if (!_gpBalance.TryTransfer(fromId, toId, amount)) return "insufficient_gp";

        string? fromNick;
        try { fromNick = _profileHelper.GetPmcProfile(new MongoId(fromId))?.Info?.Nickname; }
        catch { fromNick = null; }

        Enqueue(toId, new PendingGift
        {
            FromNickname = string.IsNullOrWhiteSpace(fromNick) ? "A friend" : fromNick,
            Amount = amount
        });
        return "ok";
    }

    // Gifts waiting for this recipient, cleared on read so the next /state poll shows each once.
    public List<ReceivedGiftDto> TakePending(string sessionId)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(sessionId, out var list) || list.Count == 0)
                return [];

            var taken = list
                .Select(g => new ReceivedGiftDto { FromNickname = g.FromNickname, Amount = g.Amount })
                .ToList();
            _pending.Remove(sessionId);
            Save();
            return taken;
        }
    }

    private void Enqueue(string toId, PendingGift gift)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(toId, out var list))
                _pending[toId] = list = new List<PendingGift>();
            list.Add(gift);
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_file))
                _pending = JsonSerializer.Deserialize<Dictionary<string, List<PendingGift>>>(
                    File.ReadAllText(_file)) ?? new();
        }
        catch { _pending = new(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SysPath.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_pending, JsonOptions));
        }
        catch { /* best-effort; the queue still lives in memory this session */ }
    }

    private class PendingGift
    {
        public string FromNickname { get; set; } = "";
        public int Amount { get; set; }
    }
}
