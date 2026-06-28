using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._Arcane.ERP.Preferences;
using Content.Shared.CCVar;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Arcane.ERP.Preferences;

public sealed class ErpOrganPreferencesManager : IPostInjectInit
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    private readonly ISawmill _log = Logger.GetSawmill("erp.prefs");

    private readonly Dictionary<NetUserId, Dictionary<int, ErpOrganPreferences>> _cache = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true };

    public void PostInject()
    {
        _net.RegisterNetMessage<MsgErpOrganPreferences>();
        _net.RegisterNetMessage<MsgUpdateErpOrganPreferences>(HandleUpdate);

        _userDb.AddOnLoadPlayer(OnPlayerConnected);
        _userDb.AddOnFinishLoad(OnFinishLoad);
        _userDb.AddOnPlayerDisconnect(OnPlayerDisconnected);
    }

    private async Task OnPlayerConnected(ICommonSession session, CancellationToken cancel)
    {
        var slots = new Dictionary<int, ErpOrganPreferences>();
        _cache[session.UserId] = slots;

        var maxSlots = _cfg.GetCVar(CCVars.GameMaxCharacterSlots);
        for (var slot = 0; slot < maxSlots; slot++)
        {
            cancel.ThrowIfCancellationRequested();
            var json = await _db.GetErpOrganPreferencesAsync(session.UserId, slot);
            slots[slot] = Normalize(Deserialize(json), session.UserId, slot);
        }
    }

    private void OnFinishLoad(ICommonSession session)
    {
        if (!_cache.TryGetValue(session.UserId, out var slots))
            return;

        foreach (var slot in slots.Keys)
            SendToClient(session, slot);
    }

    private void OnPlayerDisconnected(ICommonSession session)
    {
        _cache.Remove(session.UserId);
    }

    public void SendToClient(ICommonSession session, int slot)
    {
        if (!_cache.TryGetValue(session.UserId, out var slots))
            return;

        var prefs = slots.TryGetValue(slot, out var p) ? p : ErpOrganPreferences.Default();
        var msg = new MsgErpOrganPreferences { Slot = slot, Preferences = prefs };
        _net.ServerSendMessage(msg, session.Channel);
    }

    public ErpOrganPreferences? GetCached(NetUserId userId, int slot)
    {
        if (_cache.TryGetValue(userId, out var slots) && slots.TryGetValue(slot, out var prefs))
            return prefs;
        return null;
    }

    private void HandleUpdate(MsgUpdateErpOrganPreferences msg)
    {
        if (!_players.TryGetSessionByChannel(msg.MsgChannel, out var session))
            return;

        var maxSlots = _cfg.GetCVar(CCVars.GameMaxCharacterSlots);
        var slot = msg.Slot;
        if (slot < 0 || slot >= maxSlots)
            return;

        var prefs = Normalize(msg.Preferences, session.UserId, slot);

        if (!_cache.TryGetValue(session.UserId, out var slots))
            _cache[session.UserId] = slots = new Dictionary<int, ErpOrganPreferences>();

        slots[slot] = prefs;

        SaveAsync(session.UserId, slot, prefs);
    }

    private ErpOrganPreferences Normalize(ErpOrganPreferences? input, NetUserId userId, int slot)
    {
        var (species, sex) = GetSlotSpeciesAndSex(userId, slot);
        if (species == null)
            return ErpOrganPreferencesNormalizer.Normalize(input);

        var definitions = ErpOrganEditorDefinitions.GetForSpecies(species, sex, _prototype, _componentFactory);
        return ErpOrganPreferencesNormalizer.Normalize(input, definitions);
    }

    private (string? Species, Sex Sex) GetSlotSpeciesAndSex(NetUserId userId, int slot)
    {
        var prefs = _prefs.GetPreferencesOrNull(userId);
        if (prefs?.Characters.TryGetValue(slot, out var profile) == true &&
            profile is HumanoidCharacterProfile humanoid)
        {
            return (humanoid.Species, humanoid.Sex);
        }

        return (null, Sex.Male);
    }

    private async void SaveAsync(NetUserId userId, int slot, ErpOrganPreferences prefs)
    {
        try
        {
            var json = Serialize(prefs);
            await _db.SaveErpOrganPreferencesAsync(userId, slot, json);
        }
        catch (Exception e)
        {
            _log.Error($"Failed to save ERP organ preferences for {userId} slot {slot}: {e}");
        }
    }

    private static string Serialize(ErpOrganPreferences prefs)
        => JsonSerializer.Serialize(prefs, JsonOpts);

    private static ErpOrganPreferences Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ErpOrganPreferences.Default();

        try
        {
            return JsonSerializer.Deserialize<ErpOrganPreferences>(json, JsonOpts)
                   ?? ErpOrganPreferences.Default();
        }
        catch
        {
            return ErpOrganPreferences.Default();
        }
    }
}
