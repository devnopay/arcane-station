using Content.Shared._Arcane.ERP.Organs;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._Arcane.ERP;

public sealed class EroticOrganSpawnSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    private static readonly (string Proto, string Slot)[] GroinCommon =
    [
        ("OrganAnus", "anus"),
    ];

    private static readonly (string Proto, string Slot)[] GroinMaleOrgans =
    [
        ("OrganPenis", "penis"),
        ("OrganTesticles", "testicles"),
    ];

    private static readonly (string Proto, string Slot)[] GroinFemaleOrgans =
    [
        ("OrganVagina", "vagina"),
        ("OrganUterus", "uterus"),
    ];

    private static readonly (string Proto, string Slot)[] ChestFemaleOrgans =
    [
        ("OrganBreasts", "breasts"),
    ];

    public override void Initialize()
    {
        base.Initialize();
        // Run after SharedBodySystem so body parts are already spawned when we look for them.
        SubscribeLocalEvent<HumanoidAppearanceComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedBodySystem)]);
        SubscribeLocalEvent<HumanoidAppearanceComponent, ProfileLoadFinishedEvent>(OnProfileLoaded);
        SubscribeLocalEvent<HumanoidAppearanceComponent, SexChangedEvent>(OnSexChanged);
    }

    private void OnMapInit(Entity<HumanoidAppearanceComponent> ent, ref MapInitEvent args)
    {
        SpawnEroticOrgans(ent, ent.Comp.Sex);
    }

    private void OnProfileLoaded(Entity<HumanoidAppearanceComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        RemoveEroticOrgans(ent);
        SpawnEroticOrgans(ent, ent.Comp.Sex);
    }

    private void OnSexChanged(Entity<HumanoidAppearanceComponent> ent, ref SexChangedEvent args)
    {
        RemoveEroticOrgans(ent);
        SpawnEroticOrgans(ent, args.NewSex);
    }

    private void SpawnEroticOrgans(EntityUid uid, Sex sex)
    {
        if (sex == Sex.Unsexed)
            return;

        var groin = GetBodyPartOfType(uid, BodyPartType.Groin);
        var chest = GetBodyPartOfType(uid, BodyPartType.Chest);

        if (groin.HasValue)
        {
            TrySpawnOrgans(uid, groin.Value, GroinCommon);

            if (sex is Sex.Male or Sex.Futanari)
                TrySpawnOrgans(uid, groin.Value, GroinMaleOrgans);

            if (sex is Sex.Female or Sex.Futanari)
                TrySpawnOrgans(uid, groin.Value, GroinFemaleOrgans);
        }

        if (chest.HasValue && sex is Sex.Female or Sex.Futanari)
            TrySpawnOrgans(uid, chest.Value, ChestFemaleOrgans);

        var ev = new EroticOrgansSpawnedEvent();
        RaiseLocalEvent(uid, ref ev);
    }

    private void RemoveEroticOrgans(EntityUid bodyUid)
    {
        var organs = _body.GetBodyOrganEntityComps<EroticOrganComponent>((bodyUid, null));
        foreach (var organ in organs)
        {
            _body.RemoveOrgan(organ.Owner, organ.Comp2);
            QueueDel(organ.Owner);
        }
    }

    private void TrySpawnOrgans(EntityUid bodyUid, EntityUid partUid, (string Proto, string Slot)[] organs)
    {
        foreach (var (proto, slot) in organs)
            TrySpawnOrgan(bodyUid, partUid, proto, slot);
    }

    private void TrySpawnOrgan(EntityUid bodyUid, EntityUid partUid, string protoId, string slotId)
    {
        if (!_proto.HasIndex<EntityPrototype>(protoId))
            return;

        _body.TryCreateOrganSlot(partUid, slotId, out _);

        var containerId = SharedBodySystem.GetOrganContainerId(slotId);
        if (_containers.TryGetContainer(partUid, containerId, out var container)
            && container.ContainedEntities.Count > 0)
            return;

        var organEnt = Spawn(protoId, Transform(partUid).Coordinates);
        if (!_body.InsertOrgan(partUid, organEnt, slotId))
            QueueDel(organEnt);
    }

    private EntityUid? GetBodyPartOfType(EntityUid bodyUid, BodyPartType partType)
    {
        foreach (var (partUid, _) in _body.GetBodyChildrenOfType(bodyUid, partType))
            return partUid;

        return null;
    }
}
