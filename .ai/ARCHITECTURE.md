# ARCHITECTURE

> Derived from source inspection. Where behavior comes from the engine (RobustToolbox), it is marked as engine.
> Line numbers are valid for this checkout only.

## Layer model

```
┌────────────────────────────────────────────────────────────────────────────┐
│ RobustToolbox engine (submodule; frozen)                                   │
│  Program/ContentStart → ModLoader → GameServer / GameClient entry points   │
│  IoC/DI · CVars · VFS/ResourceManager · GameLoop/GameTiming                │
│  EntityManager + EntitySystemManager + EventBus + ComponentFactory         │
│  PrototypeManager · Serialization (YAML/binary) · NetManager · PVS         │
│  Map/Grid/Transform · Physics · Input · Audio · UserInterface (Clyde/XAML) │
└───────────────────────────────▲────────────────────────────────────────────┘
                                │ engine APIs
┌───────────────────────────────┴────────────────────────────────────────────┐
│ Content.Shared (GameShared)                                                │
│  Components + SharedXSystem bases · prototypes · net messages              │
│  shared events · preferences/mind/humanoid data models                     │
└───────────────▲──────────────────────────────────▲─────────────────────────┘
                │ referenced by both               │
┌───────────────┴──────────────┐   ┌───────────────┴──────────────────────────┐
│ Content.Server (GameServer)  │   │ Content.Client (GameClient)              │
│  authoritative systems       │   │  states · UI controllers · overlays      │
│  managers · GameTicker       │   │  prediction · input · audio · BUI clients│
│  DB (EF Core) · admin · EUIs │   │  lobby/character editor · map editor     │
└───────────────┬──────────────┘   └───────────────┬──────────────────────────┘
                │                                  │
┌───────────────┴──────────────────────────────────┴─────────────────────────┐
│ Resources/ (data-driven content)                                           │
│  Prototypes (4,107 YAML) · Maps · Locale (.ftl) · Textures/Audio (.rsi/.ogg)│
│  ConfigPresets (.toml) · Changelog · migration.yml/nf_migration.yml        │
└────────────────────────────────────────────────────────────────────────────┘
```

Key boundary rules observed in code:

- `Content.Shared` never references `Content.Server` or `Content.Client`.
- All content network message classes live in `Content.Shared`; server/client only register handlers.
- PVS/session visibility is server-authoritative; the client sees only replicated state.
- Map/prototype data flows one way: YAML → `PrototypeManager` → entity/component instances.

## Server bootstrap (observed)

1. Engine `Robust.Server/Program.cs` → `BaseServer.Start` (`RobustToolbox/Robust.Server/BaseServer.cs:166`).
2. Engine loads engine CVars + env + CLI + `server_config.toml` (`:195-205`); starts network (`:280`);
   mounts VFS (`:300-306`); `ModLoader.TryLoadModulesFrom("/Assemblies","Content.")` (`:316`).
3. Engine broadcasts `PreInit` → content `EntryPoint` base (`Content.Server/Entry/EntryPoint.cs:57`):
   - content config presets loaded (`:61-75`), ACZ provider, component auto-registration, prototype ignore
     lists, `ServerContentIoC.Register()`, testing callbacks, `IoCManager.BuildGraph()`,
     `factory.GenerateNetIds()`.
4. Engine broadcasts `Init`: entity manager/prototypes init; content `EntryPoint.Init` continues:
   managers resolved and `.Initialize()`d in a fixed order (`Content.Server/Entry/EntryPoint.cs:93-125`),
   including `IServerDbManager.Init()`, preferences, consent, connection manager, `MiniAuthManager`.
   When `game.destination_file` is set, server dumps chem/reaction JSON and shuts down (`:136-147`).
5. Engine broadcasts `PostInit` → content `EntryPoint.PostInit` (`:128-167`):
   sanitization/chat managers, `RecipeManager`, `IAdminManager`, `AfkManager`, `RulesManager`,
   Discord link init, EUI init, `GameMapManager`, `GameTicker.PostInitialize()` (`:161`),
   ban manager, connection `PostInit`, multi-server kick, CVar controls.
6. Engine main loop: `BaseServer.MainLoop` → `GameLoop.Run`; per tick: input/pending tasks →
   `ModUpdateLevel.PreEngine` → networked CVars → console+timer callbacks → async tasks →
   `EntityManager.TickUpdate` (systems `Update` in topological order, then queued events/deletions) →
   `PostEngine` → send game state; per frame: `FramePreEngine`/`FramePostEngine` updates
   (`Content.Server/Entry/EntryPoint.cs:169-189` handles `PostEngine` and `FramePostEngine`).
7. Shutdown: `Dispose` (`:191-199`) shuts down playtime, DB, `ServerApi`, Discord. Engine `Cleanup`
   shuts down entity systems/managers.

## Client bootstrap (observed)

1. `Content.Client/Program.cs` → `GameController` → content `EntryPoint.Init`
   (`Content.Client/Entry/EntryPoint.cs:81`): `ClientContentIoC.Register()`, BuildGraph, localization,
   component auto-registration, prototype ignore list, net IDs, manager init (admin, screenshot,
   fullscreen, changelog, viewport, ghost kick, disconnect info, job requirements, replay playback).
2. `PostInit` (`:158`): stylesheets, input contexts, parallax, global overlays, chat/consent/preferences/
   EUI/vote init, theme, run-level → state switching.
3. State selection `SwitchToDefaultState` (`:197`): replay bundle → launcher (`LauncherConnecting`) →
   `MainScreen`; in-game states come from `ClientGameTicker` network events
   (`LobbyState` → `GameplayState`).
4. Client runs `Update` at three mod levels: `FramePreEngine` (debug monitors), `PreEngine`
   (BUI pre-tick update), and engine state/prediction pipeline.

## What the engine owns vs content

| Concern | Engine | Content |
|---|---|---|
| Process/assembly bootstrap, sandbox, mod loader | ✔ | EntryPoint classes, IoC registrations |
| ECS storage, lifecycle, queries, event bus | ✔ | Components/systems/events |
| Prototype parsing/inheritance/validation | ✔ | All gameplay prototypes |
| YAML/binary serialization, component net state | ✔ | `[DataField]`, `[AutoGenerateComponentState]` usage |
| Network transport, PVS, prediction, replay framework | ✔ | NetMessages, filters, replay triggers |
| Maps/grids/transforms/physics | ✔ | Map prototypes, fixtures, shuttle logic |
| UI framework (Clyde/XAML/controls) | ✔ | Screens, controllers, XAML, stylesheets |
| CVar registry/replication | ✔ | `CCVars` definitions |
| Console command discovery/toolshed | ✔ | Commands implementation |
| Admin permissions hook | interface only | `AdminManager`, `IPermissionController`, EUIs |
| Game round, roles, DB, gameplay | ✘ | ✔ |

## ECS model (summary — details in `.ai/systems/engine-robust-toolbox.md`)

- Entities are `EntityUid` ints; network identity is `NetEntity`. `EntityManager` owns lifecycle.
- Components are data-only classes (`[RegisterComponent]`, `[DataField]`); behavior lives in
  `EntitySystem` subclasses. `ComponentFactory` auto-registers by reflection and assigns net IDs by
  name sort at startup.
- Systems subscribe explicitly in `Initialize()` (`SubscribeLocalEvent`, `SubscribeNetworkEvent`,
  `SubscribeAllEvent`); subscriptions are locked after startup.
- Shared systems are usually abstract bases (`SharedXSystem`) subclassed per side; some are run on both
  sides for prediction.
- Event dispatch distinguishes **directed** (component handler) vs **broadcast** (system) events;
  `RaiseLocalEvent<T>(msg)` does NOT reach `SubscribeLocalEvent<TComp,TEvent>` handlers. See `.ai/EVENTS.md`.

## Content loading

- Prototypes load from engine `/EnginePrototypes/` then content `/Prototypes/`
  (`RobustToolbox/Robust.Shared/Prototypes/PrototypeManager.YamlLoad.cs`).
- `Content.Shared/Entry/EntryPoint.cs:25-60` reads `Resources/IgnoredPrototypes/ignoredPrototypes.yml`
  and marks them abstract so upstream parents can be overridden/deleted by the fork.
- Prototype IDs referenced in C# via `ProtoId<T>` / `EntProtoId` (nullable strings validated by
  serializers; no compile-time constants generated).
- Runtime prototype load errors are logged and skipped per file; `Content.YAMLLinter` is the CI gate.

## Round architecture

- `GameTicker` (server; `Content.Server/GameTicking/GameTicker.cs:34`) is a partial class split across
  11 files (RoundFlow, Player, Spawning, GamePreset, GameRule, Lobby, Replays, CVars, StatusShell,
  LobbyBackground, NFSpawning extension).
- Run levels: `PreRoundLobby → InRound → PostRound` (no `RoundIdle`).
- Game presets are prototypes (`gamePresets`); each lists game rule prototypes. **Live presets are
  `NFAdventure`, `NFPirate`, `NFTest` (`Resources/Prototypes/_NF/game_presets.yml`); every upstream
  preset in `Resources/Prototypes/game_presets.yml` has `rules: []`.**
- Defaults: `game.defaultpreset = "nfpirate"`, `game.fallbackpreset = "Traitor,Extended"`,
  `game.map = "Frontier"`, `game.map_pool = "NFMapPool"`
  (`Content.Shared/CCVar/CCVars.Game.cs:36,48,72,91`).
- Round flow: `StartRound` → `LoadMaps` → `RoundStartingEvent` → `RoundStartAttemptEvent` →
  `RulePlayerSpawningEvent` → job assignment → per-player `SpawnPlayer` → `RulePlayerJobsAssignedEvent`
  → `RoundStartedEvent` (NF, server-only) → in-round updates → `EndRound` → `RoundEndTextAppendEvent`
  → `RoundRestartCleanupEvent` → back to lobby. See `.ai/systems/game-ticker.md`.
- Rules: `GameRuleSystem<T>` component-driven; `GameTicker.GameRule.cs` spawns/starts/stops rule
  entities; `GameRuleAddedEvent`, `GameRuleStartedEvent`, `GameRuleEndedEvent`.

## Fork module architecture

Modular forks are merged by folder/namespace prefix in all projects and `Resources`:

| Prefix | Origin | Significance |
|---|---|---|
| `_NF` | New Frontier (parent fork) | Largest; economy/bank, shipyard, sectors/POIs, salvage/expeditions, cryo, pirates, missions |
| `_CS` | Coyote | RPI economy, needs, scent, size, ERP flavor, custom species/jobs/ships |
| `_PS` | Palmtree (current maintainer) | Concealable clothing implant; genital markings prototypes |
| `_EE`, `_EinsteinEngines` | Einstein Engines | Carrying, supermatter, IPC/silicon |
| `_DV`, `_DeltaV` | Delta-V | Species/abilities, mail, NanoChat |
| `_Floof` | Floofstation | CustomExamine, ModifyUndies, consent-adjacent |
| `_Goobstation`, `_Corvax`, `_EstacaoPirata`, `_Mono`, `_Starlight`, `_WF`, `_White`, `_HL`, `_Offbrand`, `_CD`, `_Emberfall`, `_NC`, `_DEN`, `_AS`, `_Impstation`, `_Funkystation`, `_Shitmed` | many forks | Various content/feature imports; `_Offbrand` is empty shells |

Upstream files are patched in place and annotated with `// Frontier` (649 non-modular files,
3,111 occurrences), `// Coyote` (70 files), `// Floofstation`, etc. Some partial classes deliberately
use the upstream namespace to extend classes across folders
(e.g. `Content.Server/_NF/GameTicking/GameTicker.NFSpawning.cs` declares
`namespace Content.Server.GameTicking; // Intentionally colliding namespaces`).

## Data movement (one paragraph)

Player input → client `InputSystem` command → server `InputSystem` re-execution → gameplay systems
mutate components (authoritative) → dirty component fields → `PvsSystem` game-state messages → client
`ClientGameStateManager` applies state/interpolates; UI state flows via BUIs (`SetUiState` →
`MsgState`-like messages → client BUI `UpdateState`); echoed chat/announcements flow through
`MsgChatMessage`; per-player persistence flows through `IServerDbManager` on connect/disconnect and
periodic ticks. Full detail: `.ai/DATA_FLOW.md`.

## Important architectural boundaries

1. **Client/server**: enforced by assemblies, `NetMessage` registration and engine serialization;
   prediction only applies to explicitly shared systems.
2. **Engine/content**: content must not assume engine internals; content cannot use non-whitelisted BCL
   APIs when the client sandbox is on (default on for client, off for server).
3. **DB**: only `Content.Server` touches `IServerDbManager`; clients never see DB models.
4. **Fork code vs upstream**: merges upstream are the highest-risk area (see `.ai/HAZARDS.md`).
