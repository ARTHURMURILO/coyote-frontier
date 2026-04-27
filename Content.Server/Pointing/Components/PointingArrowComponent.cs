using Content.Server.Pointing.EntitySystems;
using Content.Shared.Pointing.Components;

namespace Content.Server.Pointing.Components
{
    /// <summary>
    ///     Arrow spawned when an entity points at something.
    ///     Access restricted to PointingSystem only — CoyoteAI core
    ///     points via <see cref="PointingSystem.TryPointEntity"/>,
    ///     which is a public method on PointingSystem itself, so the
    ///     component access level does not need to be widened.
    /// </summary>
    [RegisterComponent]
    [Access(typeof(PointingSystem))]
    public sealed partial class PointingArrowComponent : SharedPointingArrowComponent
    {
        /// <summary>
        ///     Whether or not this arrow will convert into a
        ///     <see cref="RoguePointingArrowComponent"/> when its duration runs out.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("rogue")]
        public bool Rogue;
    }
}
