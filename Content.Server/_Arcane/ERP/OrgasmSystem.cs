using System.Numerics;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Forensics;
using Content.Shared.Interaction;
using Content.Shared._Arcane.ERP;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Dataset;
using Content.Shared.Humanoid;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Arcane.ERP;

public sealed class OrgasmSystem : EntitySystem
{
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ForensicsSystem _forensics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private static readonly EntProtoId HeartsProto = "EffectHearts";
    private static readonly EntProtoId CumWallProto = "EffectCumWall";
    private const float EjaculationDistance = 0.6f;
    private const float EjaculationBlockedDistance = 0.1f;
    private const float EjaculationWallCheckExtraRange = 0.1f;
    private const float EjaculationTargetRange = 0.8f;
    private const float EjaculationForwardDot = 0.6f;

    private static readonly EntProtoId[] SemenPuddleProtos =
    [
        "PuddleSemen1", "PuddleSemen2", "PuddleSemen3", "PuddleSemen4",
    ];

    private static readonly EntProtoId[] FemCumPuddleProtos =
    [
        "PuddleFemCum1", "PuddleFemCum2", "PuddleFemCum3", "PuddleFemCum4",
    ];
    private static readonly ProtoId<LocalizedDatasetPrototype> OrgasmMessagesDataset = "OrgasmMessages";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArousalComponent, ArousalOrgasmEvent>(OnOrgasm);
    }

    private void OnOrgasm(Entity<ArousalComponent> ent, ref ArousalOrgasmEvent args)
    {
        if (!TryComp<HumanoidAppearanceComponent>(ent, out var humanoid))
            return;

        DoOrgasmEffects(ent, humanoid);
    }

    public void DoOrgasmEffects(EntityUid uid, HumanoidAppearanceComponent? humanoid = null)
    {
        Resolve(uid, ref humanoid, false);

        Spawn(HeartsProto, _transform.GetMapCoordinates(uid));
        PlayOrgasmSound(uid, humanoid?.Gender ?? Gender.Female);

        if (_prototype.TryIndex(OrgasmMessagesDataset, out var dataset))
            _chat.TrySendInGameICMessage(uid, Loc.GetString(_random.Pick(dataset.Values)), InGameICChatType.Emote, false);

        _popup.PopupEntity(Loc.GetString("orgasm-popup-self"), uid, uid, PopupType.MediumCaution);

        if (humanoid != null)
            SpawnEjaculation(uid, humanoid.Sex);

        var weakness = EnsureComp<OrgasmWeaknessComponent>(uid);
        weakness.ExpiresAt = _timing.CurTime + weakness.WeaknessDuration;
        Dirty(uid, weakness);
    }

    // TODO: move overlay logic to Content.Shared for prediction
    private void SpawnEjaculation(EntityUid uid, Sex sex)
    {
        if (sex is Sex.Unsexed)
            return;

        var puddleProtos = sex is Sex.Female ? FemCumPuddleProtos : SemenPuddleProtos;

        var xform = Transform(uid);
        var (sourcePos, sourceRot) = _transform.GetWorldPositionRotation(xform);
        var sourceMap = _transform.ToMapCoordinates(xform.Coordinates);
        var forward = sourceRot.ToWorldVec();
        var forwardMap = new MapCoordinates(sourcePos + forward * EjaculationDistance, sourceMap.MapId);

        var wallBlocked = !_interaction.InRangeUnobstructed(uid, forwardMap, EjaculationDistance + EjaculationWallCheckExtraRange);
        var coords = wallBlocked
            ? new MapCoordinates(sourcePos + forward * EjaculationBlockedDistance, sourceMap.MapId)
            : forwardMap;

        if (wallBlocked)
            Spawn(CumWallProto, forwardMap);

        var puddle = Spawn(_random.Pick(puddleProtos), coords);
        _forensics.TransferDna(puddle, uid, false);

        var dnaData = _bloodstream.GetEntityBloodData(uid);
        if (dnaData.Count > 0 && _solutionContainer.TryGetSolution(puddle, "puddle", out _, out var puddleSolution))
        {
            foreach (var reagent in puddleSolution.Contents)
            {
                reagent.Reagent.EnsureReagentData().AddRange(dnaData);
            }
        }

        foreach (var target in _lookup.GetEntitiesInRange<HumanoidAppearanceComponent>(sourceMap, EjaculationDistance + EjaculationTargetRange))
        {
            if (target.Owner == uid)
                continue;

            var toTarget = _transform.GetWorldPosition(target.Owner) - sourcePos;
            if (toTarget == Vector2.Zero || Vector2.Dot(toTarget.Normalized(), forward) < EjaculationForwardDot)
                continue;

            if (!_interaction.InRangeUnobstructed(uid, target.Owner, EjaculationDistance + EjaculationTargetRange))
                continue;

            AddCumOverlay(target.Owner);
        }
    }

    public void AddCumOverlay(EntityUid uid)
    {
        var overlay = EnsureComp<CumOverlayComponent>(uid);
        overlay.Count++;
        Dirty(uid, overlay);
    }

    private void PlayOrgasmSound(EntityUid uid, Gender gender)
    {
        var collection = ErpAudio.OrgasmSounds.GetValueOrDefault(gender, ErpAudio.OrgasmSounds[Gender.Female]);

        var audioParams = new AudioParams
        {
            Variation = 0.1f,
            MaxDistance = 6f,
            Volume = 3f,
        };

        _audio.PlayPvs(new SoundCollectionSpecifier(collection), uid, audioParams);
    }
}
