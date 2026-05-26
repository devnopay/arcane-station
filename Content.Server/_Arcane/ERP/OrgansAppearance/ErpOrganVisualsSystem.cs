using Content.Server._Arcane.ERP.Preferences;
using Content.Server.Preferences.Managers;
using Content.Shared._Arcane.ERP.Organs;
using Content.Shared._Arcane.ERP.OrgansAppearance;
using Content.Shared._Arcane.ERP.Preferences;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Player;

namespace Content.Server._Arcane.ERP.OrgansAppearance;

public sealed class ErpOrganVisualsSystem : EntitySystem
{
    [Dependency] private readonly ErpOrganPreferencesManager _erpPrefs = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;

    private ISawmill _log = default!;

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("erp.visuals");
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<EroticOrganComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(args.Entity) || !HasComp<EroticOrgansComponent>(args.Entity))
            return;

        var userId = args.Player.UserId;
        var slot = _prefs.GetPreferences(userId).SelectedCharacterIndex;
        var organPrefs = _erpPrefs.GetCached(userId, slot) ?? ErpOrganPreferences.Default();

        // Build visuals only for organs physically present on the body
        var organs = new Dictionary<string, ErpOrganConfig>();
        foreach (var (_, _, organComp) in _body.GetBodyOrganEntityComps<EroticOrganComponent>((args.Entity, null)))
        {
            var slotId = organComp.SlotId;
            if (string.IsNullOrEmpty(slotId))
                continue;

            organs[slotId] = organPrefs.Organs.TryGetValue(slotId, out var cfg) ? cfg : new ErpOrganConfig();
        }

        _log.Debug($"{args.Entity} — {organs.Count} organs present, {organPrefs.Organs.Count} prefs");

        var visuals = EnsureComp<ErpOrganVisualsComponent>(args.Entity);
        visuals.Organs = organs;
        Dirty(args.Entity, visuals);
    }

    private void OnOrganRemoved(Entity<EroticOrganComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (!TryComp<OrganComponent>(ent, out var organ))
            return;

        if (!TryComp<ErpOrganVisualsComponent>(args.OldBody, out var visuals))
            return;

        visuals.Organs.Remove(organ.SlotId);
        Dirty(args.OldBody, visuals);
    }
}
