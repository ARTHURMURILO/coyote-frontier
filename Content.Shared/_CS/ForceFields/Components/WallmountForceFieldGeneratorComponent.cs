using Content.Shared.Physics;
using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using System.Threading;

namespace Content.Shared._CS.ForceFields.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class WallmountForceFieldGeneratorComponent : Component
{
    [DataField]
    public bool Enabled;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool IsConnected;

    [DataField("maxLength")]
    public float MaxLength = 16F;

    [DataField("collisionMask")]
    public int CollisionMask = (int) CollisionGroup.MachineMask;

    [DataField("damagePowerDrawPerDamage")]
    public float DamagePowerDrawPerDamage = 400f;

    [DataField("batteryChargeRate")]
    public float BatteryChargeRate = 300f;

    [DataField("batteryDrainConnected")]
    public float BatteryDrainConnected = 120f;

    /// <summary>
    /// APC power draw when idle (no fields connected). Much lower than active draw.
    /// </summary>
    [DataField]
    public float IdlePowerLoad = 500f;

    /// <summary>
    /// Delay before showing active blue glow after fields connect, to reduce visual flicker on unstable power.
    /// </summary>
    [DataField]
    public TimeSpan ActiveGlowDelay = TimeSpan.FromMilliseconds(300);

    [ViewVariables]
    public float PendingDamageDraw;

    [ViewVariables]
    public float BasePowerLoad;

    [ViewVariables]
    public Dictionary<Direction, (Entity<WallmountForceFieldGeneratorComponent>, List<EntityUid>)> Connections = new();

    [ViewVariables]
    public bool ActiveGlowVisible;

    [ViewVariables]
    public CancellationTokenSource? ActiveGlowDelayCancel;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("createdField", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string CreatedField = "WallmountLinkedForceField";

    [DataField]
    public ProtoId<SinkPortPrototype> OnPort = "On";

    [DataField]
    public ProtoId<SinkPortPrototype> OffPort = "Off";

    [DataField]
    public ProtoId<SinkPortPrototype> TogglePort = "Toggle";
}

[Serializable, NetSerializable]
public enum WallmountForceFieldGeneratorVisuals : byte
{
    OnLight,
    WarningLight,
}
