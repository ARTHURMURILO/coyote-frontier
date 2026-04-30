using Content.Server._CS.ForceFields.EntitySystems;

namespace Content.Server._CS.ForceFields.Components;

[RegisterComponent, Access(typeof(WallmountForceFieldGeneratorSystem))]
public sealed partial class WallmountLinkedForceFieldComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> Generators = new();
}
