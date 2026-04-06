using Content.Shared._PS.Clothing.Concealment;
using Robust.Shared.GameStates;

namespace Content.Shared._PS.Implants;

[RegisterComponent, NetworkedComponent, Access(typeof(ClothingConcealmentSystem))]
public sealed partial class ClothingConcealmentImplantComponent : Component;
