using Content.Server._Arcane.ERP;
using Content.Server.Chat.Systems;
using Content.Server.Interaction;
using Content.Shared._Arcane.ERP;
using Content.Shared._Arcane.ErpPanel;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Arcane.ErpPanel;

public sealed partial class ErpPanelSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _ticking = default!;
    [Dependency] private readonly ArousalSystem _arousal = default!;
    [Dependency] private readonly ChatSystem _chat = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ErpPanelOwnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ErpPanelOwnerComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);

        SubscribeLocalEvent<ErpPanelOwnerComponent, BoundUIClosedEvent>(OnBoundUIClosedEvent);

        Subs.BuiEvents<ErpPanelOwnerComponent>(ErpPanelKey.Key, subs =>
        {
            subs.Event<ErpPanelSendMessage>(OnSendMessage);
        });
    }

    private void OnMapInit(Entity<ErpPanelOwnerComponent> entity, ref MapInitEvent args)
    {
        var interfaceData = new InterfaceData(
            clientType: "Content.Client._Arcane.ErpPanel.ErpPanelWindowBUI"
        );

        _ui.SetUi(entity.Owner, ErpPanelKey.Key, interfaceData);
    }

    private void OnGetVerbs(EntityUid uid, ErpPanelOwnerComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!TryComp<ErpPanelOwnerComponent>(args.User, out var userPanel))
            return;

        AlternativeVerb verb = new()
        {
            Act = () => {
                TryOpenPanel(args.User, args.Target);
                userPanel.Target = args.Target;
            },
            Text = Loc.GetString("erp-panel-open-verb"),
            Icon = new SpriteSpecifier.Rsi(new("Mobs/Silicon/station_ai.rsi"), "default"),
            Disabled = !_interaction.InRangeAndAccessible(args.User, args.Target),
            Priority = 2
        };

        args.Verbs.Add(verb);
    }

    private void OnBoundUIClosedEvent(Entity<ErpPanelOwnerComponent> entity, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not ErpPanelKey.Key)
            return;

        entity.Comp.Target = null;
    }

    private void OnSendMessage(Entity<ErpPanelOwnerComponent> entity, ref ErpPanelSendMessage args)
    {
        var user = args.Actor;
        var target = entity.Comp.Target;
        if (target == null || user != entity.Owner)
            return;

        ProccessInteraction(user, target.Value, args.Interaction, args.CustomArousal, args.CustomMoaning);
    }

    public void ProccessInteraction(EntityUid user, EntityUid target, string interactionId, float customArousal, float customMoaning)
    {
        if (!_prototype.TryIndex<PanelInteractionPrototype>(interactionId, out var interaction))
            return;

        if (!IsValidInteraction(user, target, interaction))
            return;

        if (!TryComp<ErpPanelOwnerComponent>(user, out var userPanel))
            return;

        if (!CheckRequirements(user, target, interaction))
            return;

        _arousal.AddArousal(user, interaction.UserArouse * customArousal / 100);
        _arousal.AddArousal(target, interaction.TargetArouse * customArousal / 100);

        userPanel.Cooldowns[interaction.ID] = _ticking.CurTime;
        Dirty(user, userPanel);

        var message = _random.Pick(interaction.Messages)
            .Replace("$target", Identity.Name(target, EntityManager, user));

        _chat.TrySendInGameICMessage(user, message, InGameICChatType.Emote, false);
    }

    private void TryOpenPanel(EntityUid user, EntityUid target)
    {
        if (!IsValidUI(user, target))
            return;

        _ui.TryOpenUi(user, ErpPanelKey.Key, user);

        var state = new ErpPanelBuiState(GetNetEntity(user), GetNetEntity(target));
        _ui.SetUiState(user, ErpPanelKey.Key, state);
    }

    private bool IsValidUI(EntityUid user, EntityUid target)
    {
        if (!HasComp<ErpPanelOwnerComponent>(user) || !HasComp<ErpPanelOwnerComponent>(target))
            return false;

        if (TryComp<ErpStatusComponent>(target, out var targetStatus) && targetStatus.Preference == ErpPreference.No)
            return false;

        return true;
    }

    private bool IsValidInteraction(EntityUid user, EntityUid target, PanelInteractionPrototype interaction)
    {
        if (!_interaction.InRangeAndAccessible(user, target, interaction.Range))
            return false;

        if (interaction.Messages.Count == 0)
            return false;

        if (!TryComp<ErpPanelOwnerComponent>(user, out var userPanel))
            return false;

        if (!HasComp<ErpPanelOwnerComponent>(target))
            return false;

        if (userPanel.Cooldowns.TryGetValue(interaction.ID, out var lastUse) && lastUse + interaction.Cooldown > _ticking.CurTime)
            return false;

        return true;
    }

    private bool CheckRequirements(EntityUid user, EntityUid target, PanelInteractionPrototype interaction)
    {
        var passed = true;

        if (interaction.UserRequirements != null)
        {
            foreach (var requirement in interaction.UserRequirements)
            {
                if (!requirement.IsAvailable(user, EntityManager))
                    passed = false;
            }
        }

        if (interaction.TargetRequirements != null)
        {
            foreach (var requirement in interaction.TargetRequirements)
            {
                if (!requirement.IsAvailable(target, EntityManager))
                    passed = false;
            }
        }

        return passed;
    }
}
