# System: Player, Character & Jobs

## Purpose

Player sessions, minds, character profiles, appearance/species/markings, jobs, loadouts, ghost roles,
and antag selection.

## Architecture (text diagram)

```
[engine] ICommonSession (AttachedEntity, UserId, Data)
   └─ ContentPlayerData (Content.Shared/Players/ContentPlayerData.cs:13)  ← UserId, Name, Mind, flags
[nullspace] Mind entity (MindComponent)
   ├─ OwnedEntity → body (MindContainerComponent.Mind)
   ├─ VisitingEntity → ghost
   ├─ MindRoles: list of role entities (JobRoleComponent / *RoleComponent)
   └─ UserId, CharacterName, Objectives
[body] MobAnthromorph / MobHuman / species mob
   ├─ HumanoidAppearanceComponent (species, markings, height/width, leg style)
   ├─ BodyComponent (parts/organs)
   └─ ActorComponent (session link)
```

Key files:
- `Content.Shared/Players/ContentPlayerData.cs`, `PlayerDataExt.cs`, `SharedPlayerSystem.cs`
- `Content.Shared/Mind/SharedMindSystem.cs:22`, `Content.Server/Mind/MindSystem.cs:20`
- `Content.Shared/Roles/SharedRoleSystem.cs:20`, `Content.Server/Roles/RoleSystem.cs:9`
- `Content.Server/Database/UserDbDataManager.cs:19` (per-user load orchestration)

## Character profiles

- `HumanoidCharacterProfile` (`Content.Shared/Preferences/HumanoidCharacterProfile.cs:28`) is the only
  `ICharacterProfile` implementation. Fields: name/flavor text, species/custom species name, age/sex/gender,
  `BankBalance` (NF, default 50,000), height/width, appearance, spawn priority, job priorities,
  antag preferences, traits, hidden emote categories, loadouts.
- Validation `EnsureValid` (`:604`) enforces species round-start, age/sex clamps, name rules, flavor text
  sanitization/limit, bank clamp, height/width species clamps, trait budgets, loadout validity.
- Client editor: `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs` (antag UI commented out,
  "Frontier: no antags"), preview via `LobbyUIController.LoadProfileEntity` using `DollPrototype`.
- Transport: `MsgUpdateCharacter`/`MsgSelectCharacter`/`MsgDeleteCharacter`; server
  `ServerPreferencesManager` (`:24`) validates + saves; DB `Profile` row + children.
- Import/export: `HumanoidProfileExport` YAML via `SharedHumanoidAppearanceSystem.ToDataNode/FromStream`
  (includes consent freetext/toggles).
- Max character slots: `CCVars.GameMaxCharacterSlots` default 30.

## Species / appearance / markings

How-to: add a playable species `.ai/guides/adding-species.md`.

- `SpeciesPrototype` (`Content.Shared/Humanoid/Prototypes/SpeciesPrototype.cs:9`): `RoundStart`, `Kind`,
  sprite set, marking points, mob prototype, doll prototype, sexes/ages, min/avg/max height/width,
  `DefaultLegStyle` (`_CS`), `AllowDigilegDisplacement`, `ForcedMarkingColor` (NF).
- `HumanoidAppearanceComponent` (`:16`): marking set, base sprite layers, species/skin/eye,
  undergarments, `BaseHeight/BaseWidth`, current `Height/Width`, `LegStyle`.
- `SharedHumanoidAppearanceSystem.LoadProfile` (`:573-730`) applies species → sex → eye → leg style →
  skin → markings (forced colors) → hair → height/width scale; `SetSpecies`, `SetHeight/SetWidth`,
  `SetScale`.
- Markings: `MarkingManager` filters by `speciesRestriction` **or** `kindAllowance` ∩ `SpeciesPrototype.Kind`
  (`_CS` addition, `MarkingPrototype.KindAllowance:24`); `MarkingPointsPrototype`; `HumanoidVisualLayers`
  includes `Genital`; `_PS` genital/breast markings gated by `kindAllowance`.
- Species resources: `Resources/Prototypes/{Species,Body}`, fork files under `_CS` (`Anthromorph`),
  `_NF` (Goblin/Sheleg), `_DV`, `_DEN`, `_EE`, `_EinsteinEngines`, `_Starlight`, `Nyanotrasen`, `_PS`.
- Height/width: `HeightAdjustSystem` (`Content.Shared/HeightAdjust/HeightAdjustSystem.cs:13`),
  used by `_CS` size manipulation.

## Jobs / roles / loadouts

- `JobPrototype` (`Content.Shared/Roles/JobPrototype.cs:15`): playtime tracker, requirements (+ Frontier
  alternate sets), starting gear, job entity, weight, icon, whitelist flag, guide, RPI multiplier (`_CS`).
- `DepartmentPrototype` groups jobs in the editor.
- `JobRequirement` implementations (playtime/role/department/age/species/traits) checked by
  `JobRequirementsManager` client-side and server-side at spawn.
- Assignment: `StationJobsSystem.AssignJobs` (`Roundstart.cs:59`), `PickBestAvailableJobWithPriority`
  (`:427`), overflow jobs.
- Roles on mind: `SharedRoleSystem.MindAddJobRole` spawns a `MindRoleJob` entity; `MindRoleComponent`
  records role type/subtype/antag flags.
- Loadouts: `RoleLoadout`/`LoadoutGroupPrototype`/`LoadoutPrototype` (+ NF price, encryption keys,
  implants, cartridges); applied during `StationSpawningSystem.SpawnPlayerMob` with bank-affordability
  checks (`:177-280`); base `LoadoutSystem` applies loadouts for entities with `LoadoutComponent` on MapInit.
- Job whitelists: `JobWhitelistManager` (global `ContentPlayerData.Whitelisted` or per-job DB set);
  ghost-role whitelists too (`_NF/GhostRoleWhitelistSystem`).

## Spawn pipeline

```
GameTicker.SpawnPlayers → RulePlayerSpawningEvent → AssignJobs → per player:
  PlayerBeforeSpawnEvent → job pick → mind.CreateMind/SetUserId
  → StationSpawningSystem.SpawnPlayerCharacterOnStation (PlayerSpawningEvent)
      → SpawnPlayerMob: spawn species prototype, LoadProfile, loadout + StartingGear, implants, ID/PDA
  → mind.TransferTo(mob) → JobTrackingComponent (NF) → MindAddJobRole → PlayerSpawnCompleteEvent
```

## Ghosts / observers

- `GhostSystem` (`Content.Server/Ghost/GhostSystem.cs:48`): ghost attempt, `SpawnGhost`, return-to-body,
  warp/EUI.
- `GhostRoleSystem` (`Content.Server/Ghost/Roles/GhostRoleSystem.cs:42`): register/unregister, raffles,
  request/takeover, mind creation; `GhostRoleComponent`/`GhostRolePrototype`; whitelist support.
- Client: `GhostUIController`, `GhostRolesWindow`; observer mob `MobObserver`.

## Antag pipeline

- `AntagSelectionSystem` (`Content.Server/Antag/AntagSelectionSystem.cs:40`) reacts to
  `RulePlayerSpawningEvent`/`RulePlayerJobsAssignedEvent`/spawn events; `MakeAntag` (`:372`) spawns or
  converts, equips rig loadouts, adds mind roles, sends briefing.
- `AntagPrototype`/`AntagSelectionDefinition`; preference-based preselection; fallback roles.
- Live usage: NF pirates (`Resources/Prototypes/_NF/Roles/Antags/nfpirate.yml`,
  `_NF/Pirate/Systems/NFPirateSystem.cs:28`). Upstream antag rules are disabled by empty presets.

## Persistence mapping

| Profile data | DB destination |
|---|---|
| name, flavor, age, sex/gender, species, height/width, leg style, colors, bank | `Profile` columns |
| markings | `Profile.Markings` JSON (jsonb on PG) |
| jobs/antags/traits | `Job`/`Antag`/`Trait` child rows |
| loadouts | `ProfileRoleLoadout` → `ProfileLoadoutGroup` → `ProfileLoadout` |
| consent freetext | `Profile.CharacterConsentFreetext` |
| account consent | `ConsentSettings`/`ConsentToggle` |
| playtime/whitelists | `PlayTime`, `JobWhitelist`/`RoleWhitelist` |

## Dependencies

Engine sessions/player manager, `IServerDbManager`, `ServerPreferencesManager`, consent, mind/roles,
body/humanoid systems, station spawning/jobs, ghost system, NF bank (loadout affordability, bank balance).

## Depended on by

GameTicker spawning, chat (speech/mind), admin tools (whitelists/notes), consent checks, integration tests.

## Unknowns

- Whether any live config uses antag preferences (UI is disabled).
- Reachability of some upstream role/objective components under NF presets.
