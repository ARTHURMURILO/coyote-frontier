using Content.Shared._CS.AICore;
using Robust.Shared.Prototypes;

namespace Content.Server._CS.AICore;

/// <summary>
///     Provides lore descriptions for species via SpeciesLorePrototype.
///     Used by the prompt builder to include species context in the system prompt.
/// </summary>
public sealed class SpeciesLoreSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public string GetSpeciesDescription(string speciesId)
    {
        if (_prototype.TryIndex<SpeciesLorePrototype>(speciesId, out var lore))
            return lore.Description;
        return speciesId;
    }

    public string GetSpeciesLore(string speciesId)
    {
        if (_prototype.TryIndex<SpeciesLorePrototype>(speciesId, out var lore))
            return lore.Lore;
        return "A species found aboard the station.";
    }

    public string BuildLoreBlock(HashSet<string> speciesIds)
    {
        var lines = new List<string>();
        foreach (var id in speciesIds)
        {
            if (!_prototype.TryIndex<SpeciesLorePrototype>(id, out var lore))
                continue;
            lines.Add($"- {id}: {lore.Description}. {lore.Lore}");
        }
        return string.Join("\n", lines);
    }
}
