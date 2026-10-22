using Content.Shared._PS.Clothing;
using Content.Shared.Implants;
using Content.Shared.Inventory.Events;
using Robust.Shared.Containers;

namespace Content.Server._PS.Clothing;

public sealed class ServerConcealableClothingSystem : SharedConcealableClothingSystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ConcealableClothingImplantComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<ConcealableClothingImplantComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnRemoved(Entity<ConcealableClothingImplantComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        var user = args.Container.Owner;
        if (!TryComp<ConcealableClothingUserComponent>(user, out var comp))
            return;

        var category = ent.Comp.Category;

        comp.Categories.Remove(string.IsNullOrEmpty(category) ? "*" : category);

        if (comp.Categories.Count == 0)
            RemCompDeferred<ConcealableClothingUserComponent>(user);
        else
            Dirty(user, comp);

        RefreshConcealmentActions(user);
    }

    private void OnImplanted(EntityUid uid, ConcealableClothingImplantComponent component, ImplantImplantedEvent args)
    {
        if (args.Implanted == null)
            return;

        var user = args.Implanted.Value;
        var userComponent = EnsureComp<ConcealableClothingUserComponent>(user);
        var category = component.Category;

        userComponent.Categories.Add(string.IsNullOrEmpty(category) ? "*" : category);

        Dirty(user, userComponent);
        RefreshConcealmentActions(user);
    }
}
