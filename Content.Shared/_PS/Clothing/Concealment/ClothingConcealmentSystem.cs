using System.Linq;
using Content.Shared._PS.Implants.Backpack;
using Content.Shared.Actions;
using Content.Shared.Clothing;
using Content.Shared.Implants.Components;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Clothing.Concealment;

public sealed class ClothingConcealmentSystem : EntitySystem
{
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedItemSystem _itemSystem = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;

    private static readonly ProtoId<TagPrototype> ConcealmentImplantTag = "ConcealmentImplant";

    public override void Initialize()
    {
        base.Initialize();
        //SubscribeLocalEvent<ClothingConcealmentComponent, EquipmentVisualsUpdatedEvent>(OnUpdateVisuals);
        SubscribeLocalEvent<ClothingConcealmentComponent, GetEquipmentVisualsEvent>(GetEquipmentVisuals);
        SubscribeLocalEvent<ClothingConcealmentComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ClothingConcealmentComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ClothingConcealmentComponent, ToggleConcealmentEvent>(OnToggle);
    }
    private void OnMapInit(EntityUid uid, ClothingConcealmentComponent comp, MapInitEvent args)
    {
        _actionContainer.EnsureAction(uid, ref comp.ToggleActionEntity, comp.ToggleAction);
    }

    private void OnGetActions(EntityUid uid, ClothingConcealmentComponent comp, GetItemActionsEvent args)
    {
        if (comp.ToggleActionEntity == null)
            return;

        if ((args.SlotFlags & comp.RequiredFlags) == 0)
            return;

        if (args.Actions.Contains(comp.ToggleActionEntity.Value))
            return;

        // if (!TryComp<ImplantedComponent>(args.User, out var implanted))
        //     return;
        //
        // var implants = implanted.ImplantContainer.ContainedEntities;
        //
        // if (!implants.Any(implant => _tag.HasTag(implant, ConcealmentImplantTag)))
        //     return;

        args.AddAction(ref comp.ToggleActionEntity, comp.ToggleAction);
        Dirty(uid, comp);
    }

    private void GetEquipmentVisuals(EntityUid uid, ClothingConcealmentComponent comp, GetEquipmentVisualsEvent ev)
    {
        if (!comp.Concealed)
            return;

        ev.Layers.Clear();
    }

    private void OnToggle(EntityUid uid, ClothingConcealmentComponent comp, ToggleConcealmentEvent args)
    {
        if (args.Handled)
            return;

        if (comp.ToggleActionEntity == null)
            return;

        comp.Concealed = !comp.Concealed;
        if (comp.ToggleActionEntity is { } action)
            _actionsSystem.SetToggled(action, comp.Concealed);

        Dirty(uid, comp);

        // TODO: Remove debug popup.
        _popupSystem.PopupEntity($"Concealed is now: {comp.Concealed}", uid, PopupType.Small, true);

        _itemSystem.VisualsChanged(uid);
        args.Handled = true;
    }
}

public sealed partial class ToggleConcealmentEvent : InstantActionEvent;
