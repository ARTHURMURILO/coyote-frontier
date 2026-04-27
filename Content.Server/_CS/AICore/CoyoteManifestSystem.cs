using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._CS.AICore;

public sealed class CoyoteManifestSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private CrewManifest _currentManifest = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        RebuildManifest();
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        RebuildManifest();
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        RebuildManifest();
    }

    private void RebuildManifest()
    {
        _currentManifest = new CrewManifest();
        foreach (var session in _playerManager.Sessions)
        {
            var ent = session.AttachedEntity;

            if ((!ent.HasValue || !Exists(ent.Value)) &&
                _mind.TryGetMind(session.UserId, out var mind) &&
                mind.Value.Comp.OwnedEntity.HasValue)
            {
                ent = mind.Value.Comp.OwnedEntity.Value;
            }

            if (!ent.HasValue || !Exists(ent.Value))
                continue;

            var entry = BuildCrewEntry(ent.Value);
            if (entry != null)
                _currentManifest.Entries.Add(entry);
        }
    }

    private CrewEntry? BuildCrewEntry(EntityUid uid)
    {
        var name = Name(uid);
        if (string.IsNullOrEmpty(name))
            return null;

        var species = "Unknown";
        var age = 30;
        var job = "Unknown";

        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            species = humanoid.Species;
            age = humanoid.Age;
        }

        if (_mind.TryGetMind(uid, out var mindId, out var mindComp) &&
            _roles.MindHasRole<JobRoleComponent>((mindId, mindComp), out var role))
        {
            if (role.Value.Comp1.JobPrototype.HasValue)
            {
                var jobProto = _prototype.Index(role.Value.Comp1.JobPrototype.Value);
                job = jobProto.LocalizedName;
            }
        }

        return new CrewEntry
        {
            Name = name,
            Species = species,
            Job = job,
            Age = age
        };
    }

    public CrewManifest GetCrewManifest()
    {
        return _currentManifest;
    }

    public HashSet<string> GetSpeciesOnStation()
    {
        var species = new HashSet<string>();
        foreach (var entry in _currentManifest.Entries)
        {
            if (!string.IsNullOrEmpty(entry.Species))
                species.Add(entry.Species);
        }
        return species;
    }
}
