# HAZARDS

> Things a future AI agent can easily get wrong. None of these are bugs to "fix" as part of documentation.

## 1. Heavily patched upstream code (highest risk)

The fork edits upstream files in place and marks lines with `// Frontier`, `// Coyote`, `// Floofstation`,
`// _CS`, etc. Observed:

- 649 non-modular `.cs` files contain `// Frontier` comments; 3,111 total occurrences.
- 70 files contain `// Coyote`; 19 contain `// _CS`; 32 `// Floof`; 51 `// DeltaV`.
- Reference points: `GameTicker.cs:113` (`NFInitialize()`), `GameTicker.RoundFlow.cs:443,734`,
  `Content.Server/Salvage/SalvageSystem.Expeditions.cs` (NF CVars + `// _CS` comments),
  `Content.Server/Lathe/LatheSystem.cs:45` ("Coyote partial"), `Content.Shared/Preferences/HumanoidCharacterProfile.cs`
  (Frontier bank balance, height/width), `Content.Server/Examination/...`, `Content.Shared/Inventory/...`.

Consequences:
- Never assume a file is "untouched upstream" just because of its path.
- Merging a new upstream version will conflict widely; keep the marker comments intact so patches are
  identifiable.
- Grep for markers before editing any core file.

## 2. Deliberate namespace collisions for partial classes

Fork code extends upstream classes from `_NF` folders using the upstream namespace, e.g.
`Content.Server/_NF/GameTicking/GameTicker.NFSpawning.cs` declares `namespace Content.Server.GameTicking;`
with a comment "Intentionally colliding namespaces". Compile errors like "partial class already has
member" usually mean a duplicate hook in an unexpected folder. Always `grep -rn "partial class GameTicker"`.

## 3. Plan mode / write restrictions do not exist anymore — but the engine is off-limits

`RobustToolbox/` is a submodule and the maintainer context says nobody but Wizden modifies the engine.
If a fix seems to require engine changes, document it as an engine dependency instead.

## 4. Engine APIs that differ from upstream documentation

This engine revision (v267.3.0, commit `feb9e1db6`) differs from commonly documented SS14 engine APIs:

- **No `[EventHandler]` attribute** — explicit `Subscribe*` calls only.
- **No `[DependsOn]`** system attribute — use `UpdatesBefore/UpdatesAfter` in `Initialize`.
- **No `GetPVSInterest`** anywhere; PVS is controlled by `SessionSpecific`, `SendOnlyToOwner`,
  `PvsOverrideSystem`, `ExpandPvsEvent`, filters.
- **No `PrototypeReloadEvent`** — use `PrototypesReloadedEventArgs`.
- **No `EntityDeletedEvent`** bus event; only the C# `EntityManager.EntityDeleted` event.
- **No `MetaFlagChangedEvent`**; flags changes do not raise.
- **No `RoundEndedEvent`**; use `RoundEndMessageEvent` + `GameRunLevelChangedEvent`.
- **No `RoundIdle`**; run levels are `PreRoundLobby`, `InRound`, `PostRound`.
- Map format is **7** (`EntitySerializer.MapFormatVersion`).
- `EntityQueryEnumerator<T>` **skips paused entities**; use `AllEntityQueryEnumerator<T>` to include them.

## 5. Broadcast vs directed events

`RaiseLocalEvent<T>(msg)` never calls `SubscribeLocalEvent<TComp,TEvent>` handlers. Many upstream code
paths rely on this subtlety. If a handler "doesn't fire", check the raise overload and whether the event
is `[ComponentEvent]` (only reachable via component lifecycle).

## 6. Subscription lock

Subscribe only in `Initialize()`. Subscribing later throws "Subscription locked." after startup.
Use `Subs` helpers for CVar/C# event handlers so they auto-unsubscribe on shutdown.

## 7. Dormant systems that look alive

| System | Reality |
|---|---|
| Chat V2 repository/moderation | `ChatRepositorySystem.Add` has no callers; censor classes only used in tests |
| Profanity censor | not wired to live chat paths |
| Upstream game presets | all `rules: []` — Traitor/Nukeops/Wizard/etc. are inert by default |
| Upstream antag preference UI | commented out in the character editor (`HumanoidProfileEditor.xaml.cs:886-936` "Frontier: no antags") |
| Upstream cargo | live map uses `NFBaseStationCargo`/`NFCargoSystem`; upstream cargo only applies to maps using `BaseStationCargo` |
| `_Offbrand` module | empty code/prototype/locale directories; textures only |
| Chat announcement "system" | no `AnnouncementSystem`; announcement logic is in `ChatSystem`/`ChatManager` |
| `ahelp` console command | does not exist; AHelp is opened via menu bar; `AdminAhelpCommand` is an echo passthrough |
| `floof.consent_rules` CVar | never read |

## 8. Consent system semantics

- `ConsentSystem.HasConsent` is **server-only meaningful**: `SharedConsentSystem.HasConsent` returns
  `false` on the client (`SharedConsentSystem.cs:74-77`).
- Mindless/NPC entities consent to **everything** (`ConsentSystem.cs:54`); unknown/disconnected users
  consent to nothing. This asymmetry is easy to misuse.
- Consent toggles `Hypno` and `NoClone` have **no enforcement callers**.
- `CharacterInfoSystem.HandleCharacterInfoRequest` returns name/job/flavor text/consent freetext for any
  requested entity with no range or consent check — do not add systems assuming this is private.
- Stripping/clothing-removal/undies-strip paths do **not** check consent; only the small set of systems
  listed in `.ai/systems/consent-and-erp.md` does.

## 9. Configuration defaults differ from upstream

- `game.defaultpreset = "nfpirate"` (`CCVars.Game.cs:36`), `game.map = "Frontier"` (`:72`),
  `game.map_pool = "NFMapPool"` (`:91`), `shuttle.auto_call_time` = 4320 min.
- Fallback preset is `"Traitor,Extended"` even though those presets have no rules (they will "succeed"
  trivially and produce an empty round).
- `game.destination_file` triggers a one-shot data dump and shuts the server down (`EntryPoint.cs:136-147`).
- DEBUG builds enable fake network lag/loss (`Content.Shared/Entry/EntryPoint.cs:50-55`).

## 10. Runtime leniency vs CI strictness

The prototype loader logs and skips per-file errors. A broken YAML may only appear as a missing entity
at runtime. `Content.YAMLLinter` (CI) is the authoritative check; run it after content changes.

## 11. DB integrity constraints

- `LogType` numeric values are persisted; the enum header comments warn not to renumber them.
- Migrations exist twice (SQLite + Postgres). `add-migration.sh` adds both; adding only one breaks a
  provider. There is no Postgres integration test in-repo.
- `database.sqlite_dbpath` default is `preferences.db`; integration tests use in-memory SQLite.
- Admin logs are dropped past thresholds (`adminlogs.drop_threshold`), no file fallback — increasing
  volume can silently lose data.

## 12. State/global mutable traps

- `IoCManager` is per-thread; systems/managers are singletons that live for the process, not per round.
  Round-scoped state must be reset on `RoundRestartCleanupEvent` (many systems do this; follow the pattern).
- `ServerPreferencesManager` caches profiles per user; `ServerConsentManager` caches consent including
  guests. Memory is never explicitly cleared for disconnected users except through manager logic.
- `GameTicker` is a single large partial class with many `[Dependency]` fields; adding cross-concern
  logic to it increases coupling. Prefer a system + rule.
- Prototype reloads mutate live entities via engine `PrototypeReloadSystem`; handlers must be idempotent.

## 13. Stray repository artifacts

- `identifier.sqlite` — 0-byte tracked file at repo root, referenced nowhere. Do not build tooling around it.
- `ToggleableClothingExample.md` — loose design doc at repo root.
- `Resources/manifest.yml` window title is `Palm3 Station` (placeholder/joke) while the splash logo is `_PS`.
- `bors.toml` requires a "Build & Test Release" status that no workflow in this fork produces.
- `_NF/Mech/Equipment/EntitySystems.cs/` is a directory with a `.cs` suffix (typo).

## 14. Client assumptions

- Client sandboxing defaults **on** (engine), so client assemblies are IL-verified; do not use
  arbitrary BCL APIs there.
- The engine main viewport is disabled; content uses `MainViewport`/`ScalingViewport`.
- `MsgCharacterInfo.cs` looks like a message but contains `EntityEventArgs`; don't register it as a NetMessage.
- No forked `ClientContentIoC` registrations or client states exist in `_NF`/`_CS`; integration is via
  partial classes, overlays, key functions, locale.
- `TitleWindowManager`, `MappingManager` and XAML hot reload are TOOLS-only or dev-only paths; don't rely
  on them in Release.

## 15. Build/version hazards

- `Directory.Packages.props` imports engine package versions; changing package versions must be coordinated
  with the engine submodule revision.
- `RELEASE`/`FULL_RELEASE`/`TOOLS`/`DEBUG` change behavior (config presets, migration validation, fake lag,
  gun sanity asserts, EF design factories). A release-only failure may not reproduce in Debug.
- `RUN_THIS.py` installs git hooks that update submodules; bypassing it can leave `RobustToolbox` stale
  relative to `RobustToolbox` binaries in `bin/`.

## 16. Content-authoring hazards (from the procedural guides)

- **Dungeon rooms are copied by prototype ID only** — atlas entity DataFields (locker/vending contents,
  custom names) are lost (`Content.Server/Procedural/DungeonSystem.Rooms.cs:176-199`).
- **Room-size mismatches silently produce empty dungeon slots** (`DungeonJob.DunGenPrefab.cs:184-208`).
- **Salvage-magnet asteroid configs are commented out** (`Resources/Prototypes/Procedural/Magnet/asteroid.yml`)
  while `Content.Shared/Salvage/SharedSalvageSystem.Magnet.cs:24-30` still references them — asteroid
  magnet offers would fail `_proto.Index()`; only debris offers work.
- **Bluespace event YAML uses `!type:DungeonSpawnGroup`** while the only implementation is
  `BluespaceDungeonSpawnGroup`; it resolves only via loose suffix matching. Use the unambiguous type in
  new content.
- **`randomHumanoidSettings` has no loadout/fixed-profile fields in this fork** — use a static mob plus
  `HumanoidAppearance.initial` for preset looks (`.ai/guides/adding-humanoid-npc.md`).
- **`RandomHumanoidSpawner` markers self-delete on MapInit** (`RandomHumanoidSystem.cs:31`) — one-shot.
- **`ProtectedGrid.KillHostileMobs`** deletes `NanoTrasen`-hostile NPCs on protected grids.
- **Species Head/Chest base sprites require sex-morph variants** (`HeadMale`/`HeadFemale`/`TorsoMale`/
  `TorsoFemale`) or they render wrong/invisible (`HumanoidVisualLayersExtension.cs:18-24`).
- **`GameRuleComponent` has no `MaxTime`/`EndDelay`** in this revision — use `MaxTimeRestartRuleComponent`.
- **`endgamerule` takes a NetEntity**, not a prototype ID.
- **Admin permission cache is built once** in `AdminManager.Initialize()`; commands registered later
  (PostInitialize or runtime) become server-console-only for players.
- **Classic admin commands require a parameterless constructor** and class-level `[AdminCommand]`
  (method-level only works for `RegisterCommand` callbacks).
- **Engine `savegrid`/`savemap`/`loadgrid`/`loadmap` are server-console-only** (no `[AdminCommand]`),
  and all saves write to `data/`, never `Resources/`.
- **`savemap` refuses initialized maps** without `force`; loaded post-init entities never re-run
  `MapInitEvent` (`EntityDeserializer.SetMapInitLifestage`).
- **NF drydock double-inits** loaded grids (manual `MapInitEvent` after `TryLoadGrid` already ran map
  init) — do not copy that pattern blindly.
- **`MarkingPoints.Required` is effectively unused**, and `SpeciesPrototype.Descriptor`/`GuideBookIcon`
  are dead fields.
