using System.Linq;
using Content.Shared.Actions;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Mindshield.Components;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared._PS.Clothing;

public abstract class SharedConcealableClothingSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ConcealableClothingComponent, GetItemActionsEvent>(GetActions);
        SubscribeLocalEvent<ConcealableClothingComponent, ToggleClothingConcealmentEvent>(OnToggle);
    }

    private void OnToggle(EntityUid uid, ConcealableClothingComponent component, ToggleClothingConcealmentEvent args)
    {
        var user = args.Performer;

        // User must have implant
        if (!HasConcealableImplant(user, component))
            return;

        args.Handled = true;
        component.IsConcealed = !component.IsConcealed;
        _actions.SetToggled(component.ToggleActionEntity, component.IsConcealed);
        Dirty(uid, component);
        _item.VisualsChanged(uid);

        // Popup
        var state = component.IsConcealed ? "hidden" : "shown";
        var itemName = Identity.Entity(uid, EntityManager);
        var userName = Identity.Entity(user, EntityManager);
        var selfText = Loc.GetString($"actions-concealment-{state}-self", ("item", itemName));
        var othersText = Loc.GetString($"actions-concealment-{state}-others", ("user", userName), ("item", userName));
        _popup.PopupPredicted(selfText, othersText, user, user);

        // Spawn Sparks
        if (!_timing.IsFirstTimePredicted)
            return;

        var coord = Transform(uid).Coordinates;
        PredictedSpawnAtPosition("BlueFlashEffect", coord);
    }

    private void GetActions(EntityUid uid, ConcealableClothingComponent component, GetItemActionsEvent args)
    {
        if (HasConcealableImplant(args.User, component))
            args.AddAction(ref component.ToggleActionEntity, component.ToggleAction);
    }

    /// <summary>
    /// Re-evaluates the concealment actions of the given entity's currently equipped clothing.
    /// </summary>
    /// <remarks>
    /// Item actions are only granted when an item gets equipped, so changing the entity's
    /// concealment implants (e.g. injecting the implant while already wearing a backpack) would
    /// otherwise leave the already equipped clothing without its toggle action until it is
    /// unequipped and equipped again.
    /// </remarks>
    protected void RefreshConcealmentActions(EntityUid user)
    {
        if (TerminatingOrDeleted(user))
            return;

        if (!_inventory.TryGetContainerSlotEnumerator(user, out var slots))
            return;

        while (slots.NextItem(out var item))
        {
            if (!TryComp<ConcealableClothingComponent>(item, out var clothing))
                continue;

            if (HasConcealableImplant(user, clothing))
                _actions.AddAction(user, ref clothing.ToggleActionEntity, clothing.ToggleAction, item);
            else if (clothing.ToggleActionEntity is { } action)
                _actions.RemoveProvidedAction(user, item, action);
        }
    }

    private bool HasConcealableImplant(EntityUid user, ConcealableClothingComponent component)
    {
        if (!component.RequireImplant)
            return true;

        if (!TryComp<ConcealableClothingUserComponent>(user, out var userComponent))
            return false;

        // universal implant
        if (userComponent.Categories.Contains("*"))
            return true;

        return string.IsNullOrEmpty(component.Category) || userComponent.Categories.Contains(component.Category);
    }
}

public sealed partial class ToggleClothingConcealmentEvent : InstantActionEvent;
