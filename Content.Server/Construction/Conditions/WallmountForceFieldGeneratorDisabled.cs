using Content.Shared._CS.ForceFields.Components;
using Content.Shared.Construction;
using Content.Shared.Examine;
using JetBrains.Annotations;

namespace Content.Server.Construction.Conditions;

[UsedImplicitly]
[DataDefinition]
public sealed partial class WallmountForceFieldGeneratorDisabled : IGraphCondition
{
    public bool Condition(EntityUid uid, IEntityManager entityManager)
    {
        return !entityManager.TryGetComponent<WallmountForceFieldGeneratorComponent>(uid, out var generator) || !generator.Enabled;
    }

    public bool DoExamine(ExaminedEvent args)
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();
        if (!entityManager.TryGetComponent<WallmountForceFieldGeneratorComponent>(args.Examined, out var generator) || !generator.Enabled)
            return false;

        args.PushMarkup(Loc.GetString("construction-examine-condition-wfg-disabled"));
        return true;
    }

    public IEnumerable<ConstructionGuideEntry> GenerateGuideEntry()
    {
        yield return new ConstructionGuideEntry
        {
            Localization = "construction-guide-condition-wfg-disabled"
        };
    }
}
