using Content.Client._Arcane.ERP.Preferences;
using Content.Client.Lobby;
using Content.Shared._Arcane.ERP.Organs;
using Content.Shared._Arcane.ERP.OrgansAppearance;
using Content.Shared._Arcane.ERP.Preferences;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared.Humanoid;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Arcane.ERP.OrgansAppearance;

public sealed class ErpOrganVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly ClientErpOrganPreferencesManager _erpPrefs = default!;
    [Dependency] private readonly IClientPreferencesManager _prefs = default!;

    private enum OrganLayer : byte
    {
        Penis,
        Vagina,
        Breasts,
        Testicles,
        Anus,
        Butt,
    }

    private static readonly Dictionary<string, OrganLayer> SlotToLayer = new()
    {
        [ErpOrganSlots.Penis]     = OrganLayer.Penis,
        [ErpOrganSlots.Vagina]    = OrganLayer.Vagina,
        [ErpOrganSlots.Breasts]   = OrganLayer.Breasts,
        [ErpOrganSlots.Testicles] = OrganLayer.Testicles,
        [ErpOrganSlots.Anus]      = OrganLayer.Anus,
        [ErpOrganSlots.Butt]      = OrganLayer.Butt,
    };

    private static readonly Dictionary<string, Sex[]> SlotSexFilter = new()
    {
        [ErpOrganSlots.Penis]     = [Sex.Male, Sex.Futanari],
        [ErpOrganSlots.Testicles] = [Sex.Male, Sex.Futanari],
        [ErpOrganSlots.Vagina]    = [Sex.Female, Sex.Futanari],
        [ErpOrganSlots.Breasts]   = [Sex.Female, Sex.Futanari],
    };

    private static readonly Dictionary<string, HumanoidVisualLayers> OrganCoverageLayer = new()
    {
        [ErpOrganSlots.Penis]     = HumanoidVisualLayers.Groin,
        [ErpOrganSlots.Vagina]    = HumanoidVisualLayers.Groin,
        [ErpOrganSlots.Testicles] = HumanoidVisualLayers.Groin,
        [ErpOrganSlots.Anus]      = HumanoidVisualLayers.Groin,
        [ErpOrganSlots.Butt]      = HumanoidVisualLayers.Groin,
        [ErpOrganSlots.Breasts]   = HumanoidVisualLayers.Chest,
    };

    private static readonly Dictionary<string, string> OrganRsiPath = new()
    {
        [ErpOrganSlots.Penis] = "/Textures/_Arcane/ERP/Mobs/penis_onmob.rsi",
        [ErpOrganSlots.Vagina] = "/Textures/_Arcane/ERP/Mobs/vagina_onmob.rsi",
        [ErpOrganSlots.Breasts] = "/Textures/_Arcane/ERP/Mobs/breasts_onmob.rsi",
        [ErpOrganSlots.Testicles] = "/Textures/_Arcane/ERP/Mobs/testicles_onmob.rsi",
        [ErpOrganSlots.Butt] = "/Textures/_Arcane/ERP/Mobs/butt_onmob.rsi",
        [ErpOrganSlots.Anus] = "/Textures/_Arcane/ERP/Mobs/anus_onmob.rsi",
    };

    private ISawmill _log = default!;

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("erp.visuals.cl");

        SubscribeLocalEvent<ErpOrganVisualsComponent, AfterAutoHandleStateEvent>(OnOrganState);
        SubscribeLocalEvent<ErpOrganVisualsComponent, ComponentShutdown>(OnOrganShutdown);

        // Clothing equipped/unequipped → HiddenLayers changed → update visibility
        SubscribeLocalEvent<HumanoidAppearanceComponent, HumanoidVisualStateUpdatedEvent>(OnHumanoidState);

        // Editor preview: client-side dummy entity, no server state
        SubscribeLocalEvent<HumanoidAppearanceComponent, ProfileLoadFinishedEvent>(OnPreviewProfileLoaded);
    }

    public void RefreshPreview(EntityUid uid, ErpOrganPreferences prefs)
    {
        if (!IsClientSide(uid))
            return;

        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        var humanoid = CompOrNull<HumanoidAppearanceComponent>(uid);
        var visuals = EnsureComp<ErpOrganVisualsComponent>(uid);
        visuals.Organs = FilterOrgansBySex(prefs.Organs, humanoid?.Sex ?? Sex.Male);

        ApplyOrganLayers((uid, visuals), humanoid, sprite);
    }

    private void OnPreviewProfileLoaded(Entity<HumanoidAppearanceComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        if (!IsClientSide(ent))
            return;

        if (!HasComp<EroticOrgansComponent>(ent))
            return;

        var slot = _prefs.Preferences?.SelectedCharacterIndex ?? 0;
        var organPrefs = _erpPrefs.GetSlot(slot);

        var visuals = EnsureComp<ErpOrganVisualsComponent>(ent);
        visuals.Organs = FilterOrgansBySex(organPrefs.Organs, ent.Comp.Sex);

        if (TryComp<SpriteComponent>(ent, out var sprite))
            ApplyOrganLayers((ent, visuals), ent.Comp, sprite);
    }

    private static Dictionary<string, ErpOrganConfig> FilterOrgansBySex(
        Dictionary<string, ErpOrganConfig> organs, Sex sex)
    {
        var result = new Dictionary<string, ErpOrganConfig>();
        foreach (var (slotId, cfg) in organs)
        {
            if (SlotSexFilter.TryGetValue(slotId, out var allowed) && Array.IndexOf(allowed, sex) < 0)
                continue;
            result[slotId] = cfg;
        }
        return result;
    }

    private void OnOrganState(Entity<ErpOrganVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _log.Debug($"OnOrganState {ent}, organs={ent.Comp.Organs.Count}");
        if (!TryComp<SpriteComponent>(ent, out var sprite))
        {
            _log.Debug($"{ent} — no SpriteComponent");
            return;
        }

        ApplyOrganLayers(ent, CompOrNull<HumanoidAppearanceComponent>(ent), sprite);
    }

    private void OnHumanoidState(Entity<HumanoidAppearanceComponent> ent, ref HumanoidVisualStateUpdatedEvent args)
    {
        if (!HasComp<ErpOrganVisualsComponent>(ent))
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        foreach (var slotId in ErpOrganSlots.All)
        {
            if (!SlotToLayer.TryGetValue(slotId, out var layerKey))
                continue;

            if (!_sprite.LayerMapTryGet((ent, sprite), layerKey, out var index, false))
                continue;

            _sprite.LayerSetVisible((ent, sprite), index, IsOrganVisible(slotId, ent.Comp));
        }
    }

    private void OnOrganShutdown(Entity<ErpOrganVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        foreach (var layerKey in Enum.GetValues<OrganLayer>())
            _sprite.RemoveLayer((ent, sprite), layerKey, logMissing: false);
    }

    private void ApplyOrganLayers(Entity<ErpOrganVisualsComponent> ent, HumanoidAppearanceComponent? humanoid, SpriteComponent sprite)
    {
        foreach (var slotId in ErpOrganSlots.All)
        {
            if (!SlotToLayer.TryGetValue(slotId, out var layerKey))
                continue;
            if (!OrganRsiPath.TryGetValue(slotId, out var rsiPath))
                continue;

            if (!ent.Comp.Organs.TryGetValue(slotId, out var cfg))
            {
                // Organ not present on this character — hide layer if it was added previously
                if (_sprite.LayerMapTryGet((ent, sprite), layerKey, out var hiddenIdx, false))
                    _sprite.LayerSetVisible((ent, sprite), hiddenIdx, false);
                continue;
            }

            var stateName = BuildStateName(slotId, cfg);
            var visible = IsOrganVisible(slotId, humanoid);
            _log.Debug($"layer {slotId} state={stateName} visible={visible}");

            if (!_sprite.LayerMapTryGet((ent, sprite), layerKey, out var index, false))
                index = _sprite.LayerMapReserve((ent, sprite), layerKey);

            _sprite.LayerSetRsi((ent, sprite), index, new ResPath(rsiPath), stateName);
            _sprite.LayerSetColor((ent, sprite), index, cfg.Color ?? humanoid?.SkinColor ?? Color.FromHex("#C0967F"));
            _sprite.LayerSetScale((ent, sprite), index, BuildOrganScale(slotId, cfg));
            _sprite.LayerSetVisible((ent, sprite), index, visible);
        }
    }

    private static bool IsOrganVisible(string slotId, HumanoidAppearanceComponent? humanoid)
    {
        if (humanoid == null)
            return true;

        if (!OrganCoverageLayer.TryGetValue(slotId, out var coverageLayer))
            return true;

        return !humanoid.HiddenLayers.ContainsKey(coverageLayer)
            && !humanoid.PermanentlyHidden.Contains(coverageLayer);
    }

    private static string BuildStateName(string slotId, ErpOrganConfig cfg)
    {
        switch (slotId)
        {
            case ErpOrganSlots.Breasts:
                var bVariant = cfg.Variant is "human" or "pair" or "" ? "pair" : cfg.Variant;
                var szLetter = cfg.Size >= 1 && cfg.Size <= 8
                    ? ((char) ('a' + cfg.Size - 1)).ToString()
                    : "a";
                return $"breasts_{bVariant}_{szLetter}_0_FRONT";
            case ErpOrganSlots.Butt:
                return $"butt_pair_{Math.Clamp(cfg.Size, 1, 5)}_0_FRONT";
            case ErpOrganSlots.Testicles:
                return "testicles_single_2_0_FRONT";
            case ErpOrganSlots.Anus:
                var aVariant = cfg.Variant is "human" or "" ? "donut" : cfg.Variant;
                return $"anus_{aVariant}_3_0_FRONT";
            case ErpOrganSlots.Vagina:
                return $"vagina_{cfg.Variant}_1_0_FRONT";
            default:
                return $"{slotId}_{cfg.Variant}_3_0_FRONT";
        }
    }

    private static Vector2 BuildOrganScale(string slotId, ErpOrganConfig cfg)
    {
        switch (slotId)
        {
            case ErpOrganSlots.Penis:
                return new Vector2(
                    1f + (cfg.Size - 3) * 0.04f,
                    0.4f + cfg.Size * 0.2f);
            case ErpOrganSlots.Testicles:
                var ts = 0.7f + (cfg.Size - 1) * 0.15f;
                return new Vector2(ts, ts);
            case ErpOrganSlots.Anus:
                var as_ = 1f + (cfg.Size - 3) * 0.1f;
                return new Vector2(as_, as_);
            default:
                return Vector2.One;
        }
    }
}
