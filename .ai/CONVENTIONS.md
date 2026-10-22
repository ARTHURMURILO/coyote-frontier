# CONVENTIONS (observed)

> Only patterns actually present in the code. Conflicts/inconsistencies are listed explicitly at the end.

## Naming

| Kind | Convention | Example |
|---|---|---|
| Component class | `<Thing>Component`, data-only | `DrinkComponent`, `ShuttleDeedComponent` |
| System class | `<Thing>System`, usually `sealed partial`, one per side | `SharedHandsSystem`, `HandsSystem` |
| Shared base | `Shared<Thing>System` abstract in `Content.Shared` | `SharedConsentSystem` |
| Prototype | `[Prototype("kind")]` + YAML `id` | `[Prototype("gamePreset")]`, `id: NFAdventure` |
| Data fields | PascalCase property with `[DataField("camelCase")]` (tag optional) | `[DataField] public float Price` |
| Proto references in C# | `ProtoId<T>` / `EntProtoId` | `ProtoId<JobPrototype>`, `EntProtoId` |
| Network message | `Msg<Thing>` in `Content.Shared`, `NetMessage` subclass | `MsgChatMessage` |
| Network event | `<Thing>Event`/`<Thing>Message`, `[Serializable, NetSerializable]`, `EntityEventArgs` | `RoundEndMessageEvent` |
| Fork folder | `_<Prefix>` under project/resource root | `Content.Server/_NF`, `Resources/Prototypes/_CS` |
| Fork namespace | `Content.<Side>._<Prefix>.<Area>` | `Content.Server._NF.Bank` |
| YAML prototype IDs | upstream name or `<PREFIX><Name>` for fork content | `NF...`, `CS...`, `PSGenitalBreasts0` |
| Locale keys | kebab-case, unprefixed, scoped per folder | `nf-adventure-title` |
| CVars | dotted lowercase, fork prefixes | `nf14.*`, `conveyor.*`, `consent.*` |
| Admin commands | `[AdminCommand(AdminFlags.X)]` attributes | `[AdminCommand(AdminFlags.Adminchat)]` |
| Tests | folder mirrors feature; `[Test]` methods | `Content.IntegrationTests/Tests/Preferences/...` |

## ECS patterns

- Components hold **data only**; behavior belongs to systems. No logic in `[DataDefinition]` components
  beyond simple helpers.
- `[RegisterComponent]` for all components; `[NetworkedComponent]` + `[AutoGenerateComponentState]`
  for replicated ones; `[AutoNetworkedField]` per replicated field; manual `ComponentGetState`/
  `ComponentHandleState` is the exception (e.g. `SharedDoAfterSystem`, gun battery/revolver, vending).
- `[DataField]` for everything serialized; `serverOnly: true` for server-only fields.
- `[Access(typeof(XSystem))]` on component APIs to constrain callers (analyzer RA0002).
- `SharedXSystem` abstract base + `XSystem : SharedXSystem` per side; partials split big systems
  (`GameTicker.*`, `ShuttleSystem.*`, `ChatSystem.*`).
- System updates ordered with `UpdatesBefore/UpdatesAfter` in `Initialize()`, not attributes.
- `EntityQueryEnumerator<T>` for hot loops (skips paused entities); `AllEntityQueryEnumerator<T>` when
  paused entities matter.
- `ProtoId<T>`/`EntProtoId` instead of raw strings; `Loc.GetString` for user text.

## File / folder organization

- Feature-first folders named after the gameplay concept, not layer (`Content.Server/Atmos/`,
  `Content.Shared/Chemistry/`), each typically `Components/`, `EntitySystems/`/`Systems/`, `Events/`,
  `Prototypes/`, `UI/`.
- Shared/server/client triangle: the same feature appears in up to three projects with the same folder
  name; only files that differ per side are duplicated.
- Fork additions go in `_<Prefix>` at each project's root + `Resources/{Prototypes,Locale,Textures,Maps}/_<Prefix>`.
- Maps live in `Resources/Maps/_<Prefix>/...`; ship prototypes reference map paths.
- Client UI is XAML (`*.xaml` + `*.xaml.cs`) with `[GenerateTypedNameReferences]` partial classes and
  `RobustXamlLoader.Load(this)` in the constructor.

## Logging / errors

- `private ISawmill _sawmill = default!;` resolved from `ILogManager` (systems get `Log` automatically).
- `Logger.GetSawmill("name")`, `_sawmill.Error/Warning/Info/Debug`.
- `DebugTools.Assert(...)` for programmer errors (DEBUG-only); `Robust.Shared.Utility.DebugTools`.
- Nullable reference types are enabled and `nullable` warnings are errors in content projects.
- Exceptions are avoided for expected failure; attempt/handled event pattern is preferred
  (`*AttemptEvent.Cancelled`, `HandledEntityEventArgs.Handled`).
- `#if EXCEPTION_TOLERANCE` for release robustness (engine config).

## Dependency injection

- `[Dependency] private readonly X _x = default!;` on systems/managers; never assign manually
  (analyzer RA0025).
- Managers registered in `ServerContentIoC.Register` / `ClientContentIoC.Register` /
  `SharedContentIoC.Register`; resolved via `IoCManager.Resolve<T>()`.
- Entity systems resolved via `IEntitySystemManager.GetEntitySystem<T>()` / `EntityManager.System<T>()`.
- `IDynamicTypeFactory` for runtime instantiation instead of `Activator.CreateInstance` (sandbox).

## Event usage

- Subscribe only in `Initialize()`; explicit `SubscribeLocalEvent/SubscribeNetworkEvent/SubscribeAllEvent`.
- Directed vs broadcast distinction is mandatory knowledge; see `.ai/EVENTS.md`.
- `QueueLocalEvent` for deferred mutation; `RaisePredictiveEvent` for predicted client→server calls.
- Ordering via `before:`/`after:` arrays; same system+event must use consistent ordering.
- Attempt events are cancellable; `Handled` events short-circuit (e.g. `PlayerBeforeSpawnEvent`).
- Relay events for inventory: `IInventoryRelayEvent` + `InventoryRelayedEvent<T>`.

## Networking patterns

- Net messages declared once in `Content.Shared`; register with `INetManager.RegisterNetMessage<T>(handler)`.
- `[Serializable, NetSerializable]` required; complex fields need `[DataField]`.
- Prefer component state replication (`AutoGenerateComponentState`) over per-action messages.
- Entity references in state use `NetEntity` (`GetNetEntity`/`EnsureEntity`); positions use `NetCoordinates`.
- Filters: `Filter.Pvs(...)`, `Filter.Broadcast()`, session-specific sends for private data.
- `SendOnlyToOwner` / `SessionSpecific` components for owner-only data (Pilot, Alerts, etc.).
- BUI pattern: `UserInterfaceComponent` + `ActivatableUIComponent` in YAML; state via `SetUiState`;
  `BuiPredictionState` for input prediction.

## Configuration

- CVar definitions grouped as partial `CCVars` class files in `Content.Shared/CCVar/` (`CCVars.Game.cs`,
  `CCVars.Chat.cs`, `CCVars.Admin.cs`, ...); fork CVars in `_NF/CCVar/NFCCVars.cs`, `_CS/CCVar/CSCCVars.cs`.
- Flags used intentionally: `CVar.SERVERONLY`, `REPLICATED`, `ARCHIVE`, `CONFIDENTIAL`, `CHEAT`.
- Runtime changes through `Subs.CVar(...)` or `OnValueChanged`; admin CVar control via `[CVarControl]`.
- Config presets as TOML under `Resources/ConfigPresets/`.

## Testing

- `Content.Tests`: unit tests with `ContentUnitTest : RobustUnitTest`; IOcc registration per test project;
  parallelizable at fixture level.
- `Content.IntegrationTests`: `PoolManager`/`TestPair` pool real server+client instances; test CVars in
  `PoolManager.Cvars.cs` (`database.sync=true`, `nftest` preset, `GameMap=Empty`); `[TestPrototypes]`
  attributes register test-only prototypes; `PoolSettings` controls reuse/dirtiness.
- Serialization and prototype round-trip tests (`PrototypeTests`), static-field validation,
  DB model drift test.
- Tests marked `[Ignore]`/`[Explicit]` exist for Frontier-specific exclusions.

## Content data conventions

- All user-visible strings in `.ftl`; prototype `name`/`description` are localization IDs.
- Sprites/audio referenced with rooted VFS paths (`/Textures/...`, `/Audio/...`).
- Prototype inheritance via `parent:`; abstractions with `abstract: true`; fork overrides through
  `Resources/IgnoredPrototypes/ignoredPrototypes.yml`.
- Entity prototype IDs are global; migrations preferred over renaming/removing (`migration.yml`, `nf_migration.yml`).
- `.rsi` sprite folder = metadata JSON + PNGs per state.

## Inconsistencies / conflicting conventions (observed — do not treat as rules)

1. **Upstream patching**: fork changes are made in-place in upstream files with `// Frontier` /
   `// Coyote` comments instead of new files. 649 non-modular files carry `// Frontier` (3,111 occurrences).
2. **Namespace collisions are deliberate** in some partial extensions
   (`GameTicker.NFSpawning.cs` → `namespace Content.Server.GameTicking; // Intentionally colliding namespaces`)
   but accidental in others (`AutoSuitSensorOffSystem` declares `Content.Server._NF.Medical.SuitSensors`,
   `RandomBlueprintComponent` declares `Content.Server._NF.Stacks.Components`, `_Floof/ModifyUndies` uses
   `Content.Server.FloofStation.ModifyUndies`).
3. **Empty/dead folders**: `_Offbrand` has textures but no C#/YAML; `_PS` has empty `Interactions/UI` dirs;
   `_WF`, `_Corvax`, `_White` have no locale dirs.
4. **Stale csproj `Compile Remove` lists**: ~80 entries in `Content.Server.csproj`/`Content.Shared.csproj`
   target files that no longer exist (merge leftovers). No current modular code is excluded.
5. **Prefix case**: `_White` code folder vs `_white` locale folder.
6. **Typo folder**: `Content.Server/_NF/Mech/Equipment/EntitySystems.cs/`.
7. **Locale styles**: most keys kebab-case; `_CS` includes non-standard files (`ass.txt`) and custom
   emote YAML (`emotes_but_cooler.yml`).
8. Some upstream files report names that no longer exist (e.g. no `MobHumanCoyote`; coyote species mob is
   `MobAnthromorph`).
