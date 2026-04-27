using Robust.Shared.Prototypes;

namespace Content.Shared._CS.AICore;

/// <summary>
///     Prototype for species lore entries.
///     ID must match the species ID from SpeciesPrototype.
///     Description is a short summary; Lore is the in-depth text
///     fed to the LLM in the system prompt.
/// </summary>
[Prototype("speciesLore")]
public sealed class SpeciesLorePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;
    [DataField] public string Lore = string.Empty;
    [DataField] public string Description = string.Empty;
}
