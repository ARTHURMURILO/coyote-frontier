using Content.Shared._CS.ForceFields.Components;
using Content.Shared.Construction;
using Content.Shared.Examine;
using JetBrains.Annotations;

namespace Content.Server._CS.ForceFields.Conditions;

[UsedImplicitly]
[DataDefinition]
public sealed partial class WallmountForceFieldGeneratorDisabled : IGraphCondition
{
    public bool Condition(EntityUid uid, IEntityManager entityManager)
    {
        if (!entityManager.TryGetComponent<WallmountForceFieldGeneratorComponent>(uid, out var generator))
            return true;

        return !generator.Enabled;
    }

    public bool DoExamine(ExaminedEvent args)
    {
        var entity = args.Examined;
        if (!IoCManager.Resolve<IEntityManager>().TryGetComponent<WallmountForceFieldGeneratorComponent>(entity, out var generator))
            return false;

        if (generator.Enabled)
        {
            args.PushMarkup(Loc.GetString("construction-examine-condition-wfg-disabled"));
            return true;
        }

        return false;
    }

    public IEnumerable<ConstructionGuideEntry> GenerateGuideEntry()
    {
        yield return new ConstructionGuideEntry
        {
            Localization = "construction-guide-condition-wfg-disabled",
        };
    }
}
