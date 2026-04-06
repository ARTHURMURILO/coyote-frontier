using Content.Shared.Inventory;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Implants.Backpack;
/// <summary>
/// A component added to a clothing piece that allows it's visibility to be overriden.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ClothingConcealmentComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId ToggleAction = "ActionToggleClothingConcealment";

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleActionEntity;

    [DataField, AutoNetworkedField]
    public bool Concealed = false;

    [DataField, AutoNetworkedField]
    public SlotFlags RequiredFlags = SlotFlags.BACK;
}
