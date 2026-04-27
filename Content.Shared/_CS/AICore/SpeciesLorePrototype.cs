using Robust.Shared.Prototypes;

namespace Content.Shared._CS.AICore;

[Prototype("speciesLore")]
public sealed class SpeciesLorePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;
    [DataField] public string Lore = string.Empty;
    [DataField] public string Description = string.Empty;
}
