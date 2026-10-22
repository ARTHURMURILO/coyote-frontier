# .ai — AI Navigation Guide

This directory is the entry point for AI agents working on the **Coyote Sector / Coyote Frontier**
repository (a Space Station 14 fork of Frontier Station, running on the RobustToolbox engine).

Read this file first, then the document that matches your task.

## 1. What this project is

- Round-based multiplayer space game: C# ECS gameplay on the **RobustToolbox** engine (git submodule,
  v267.3.0) + ~4,100 YAML prototype files.
- Fork chain: Wizden SS14 → Frontier Station (`_NF`) → Coyote (`_CS`) → Palmtree Station (`_PS`).
- Live gameplay is Frontier-style: a hub station, ships, economy, sectors, salvage — not station
  antagonist rounds. Upstream antag presets are disabled (`rules: []`).
- Maintainer constraints (from `.ai/HUMAN_CONTEXT.md`): engine must not be modified; new code goes under
  `_PS` folders; the codebase is frozen upstream (Coyote abandoned it) and now maintained here.

## 2. Major architectural layers

```
RobustToolbox engine (submodule, frozen)
   └── Content.Shared  (components, prototypes, net messages)
           ├── Content.Server  (authoritative gameplay, DB, admin)
           └── Content.Client  (UI, prediction, input, audio)
                   └── Resources/ (prototypes, maps, locale, textures, configs)
```

Fork modules are prefixes: `_NF` (Frontier, largest), `_CS` (Coyote), `_PS` (Palmtree), plus imports
`_EE`, `_EinsteinEngines`, `_DV`, `_DeltaV`, `_Floof`, `_Goobstation`, `_Corvax`, `_EstacaoPirata`,
`_Mono`, `_Starlight`, `_WF`, `_White`, `_HL`, `_Offbrand`, `_CD`, `_Emberfall`, `_NC`, and resource-only
prefixes (`_DEN`, `_AS`, `_Impstation`, `_Funkystation`, `_Shitmed`).

## 3. Where systems live

| Area | Root paths |
|---|---|
| Engine | `RobustToolbox/` |
| Shared gameplay | `Content.Shared/<Feature>` |
| Server gameplay | `Content.Server/<Feature>` |
| Client gameplay/UI | `Content.Client/<Feature>` |
| Round/game rules | `Content.Server/GameTicking`, `Content.Server/GameTicking/Rules` |
| Database | `Content.Server.Database`, `Content.Server/Database` |
| Frontier gameplay | `Content.Server/_NF`, `Content.Shared/_NF`, `Content.Client/_NF` |
| Coyote gameplay | `Content.Server/_CS`, `Content.Shared/_CS`, `Content.Client/_CS` |
| Palmtree additions | `Content.Server/_PS`, `Content.Shared/_PS`, `Content.Client/_PS` |
| Content/data | `Resources/Prototypes`, `Resources/Maps`, `Resources/Locale`, `Resources/Textures`, `Resources/Audio` |
| Tests | `Content.Tests`, `Content.IntegrationTests` |
| Tools | `Content.YAMLLinter`, `Content.MapRenderer`, `Content.Packaging`, `Content.Tools`, `Content.PatreonParser`, `Content.Replay`, `Pow3r` |

## 4. Documentation map

| Document | Contents |
|---|---|
| `.ai/REPOSITORY.md` | Inventory: languages, projects, tooling, build, resources |
| `.ai/ARCHITECTURE.md` | Layer model, bootstraps, ECS, round architecture, fork modules |
| `.ai/API.md` | Key classes/managers/systems with locations and purposes |
| `.ai/DEPENDENCIES.md` | Project graph, subsystem chains, coupling, external deps |
| `.ai/DATA_FLOW.md` | Round, player, chat, damage, UI, economy, consent flows |
| `.ai/EVENTS.md` | Event dispatch model, lifecycle, event catalog, gotchas |
| `.ai/PERSISTENCE.md` | DB schema areas, migrations, files, save/load paths |
| `.ai/CONVENTIONS.md` | Observed patterns + explicit inconsistencies |
| `.ai/HAZARDS.md` | Things likely to be misunderstood or broken by blind edits |
| `.ai/UNKNOWN.md` | Open questions and unverifiable deployment facts |
| `.ai/systems/game-ticker.md` | Round state machine, presets, rules, maps, spawn |
| `.ai/systems/engine-robust-toolbox.md` | Engine ECS/prototypes/serialization/net/timing/sandbox |
| `.ai/systems/networking-and-auth.md` | Net messages, PVS, connect/auth, HTTP APIs, Discord, replays |
| `.ai/systems/persistence-and-database.md` | EF Core model, migrations, admin logs, file persistence |
| `.ai/systems/player-character-and-jobs.md` | Sessions, minds, profiles, species/markings, jobs/loadouts, ghosts, antags |
| `.ai/systems/client-ui-and-prediction.md` | States, UIControllers, BUIs, viewport, input, prediction, audio, loc |
| `.ai/systems/chat-and-communications.md` | Chat, radio, speech/accents/emotes, announcements, bwoink |
| `.ai/systems/consent-and-erp.md` | Consent toggles, enforcement points, ERP-adjacent systems, examine |
| `.ai/systems/frontier-nf-systems.md` | Bank/market/shipyard/cargo/sectors/cryo/pirates |
| `.ai/systems/coyote-cs-and-ps-systems.md` | RPI economy, needs, scent, size, `_PS` concealable clothing |
| `.ai/systems/core-gameplay.md` | Interactions, body/damage, atmos, power, chemistry, construction, NPC, shuttles |
| `.ai/systems/content-pipeline-and-tooling.md` | Prototypes, localization, maps, tests, CI, packaging |
| `.ai/HUMAN_CONTEXT.md` | Maintainer-provided context (not proof of behavior) |

### Procedural guides (how to add X)

| Guide | Answers |
|---|---|
| `.ai/guides/adding-dungeons.md` | Asteroid/salvage dungeons: runtime generation, atlas rooms, `dungeonConfig`/`salvageDungeonMod`, testing |
| `.ai/guides/adding-humanoid-npc.md` | Humanoid enemy NPC with preset appearance (species/profile/loadout/faction/AI) |
| `.ai/guides/adding-custom-ui.md` | BUI windows (two-panel John/Cindy example), messages, sprites, interaction verbs |
| `.ai/guides/adding-gamemodes.md` | Game presets, game rules, schedulers, station events, new gamemode recipe |
| `.ai/guides/adding-admin-commands.md` | Classic/toolshed commands, `[AdminCommand]`, permissions, localization, completion |
| `.ai/guides/adding-species.md` | `SpeciesPrototype`, mobs/dolls, sprites/markings, minimum-viable recipe |
| `.ai/guides/saving-grids.md` | `savegrid`/`savemap`/mapping mode, format v7, NF drydock, preservation semantics |
| `.ai/guides/changelogs.md` | Changelog YAML authoring (manual + commit draft generator), in-game verification, tabs, removed automation |

### Research / ideas (not implemented)

| Document | Topic |
|---|---|
| `.ai/ideas/lua.md` | Lua scripting feasibility: runtime-defined components, Garry's-Mod-style hot reload, in-game robot programming, MoonSharp, sandbox/perf/licensing, phased plan |

## 5. Where the main APIs are

- Engine foundation: `RobustToolbox/Robust.Shared/GameObjects/EntitySystem.cs`, `EntityManager*.cs`,
  `EntityEventBus.*.cs`, `Prototypes/IPrototypeManager.cs`, `Serialization/Manager/ISerializationManager.cs`.
- Server managers/IoC: `Content.Server/IoC/ServerContentIoC.cs`.
- Round: `Content.Server/GameTicking/GameTicker.cs`.
- Persistence: `Content.Server/Database/ServerDbManager.cs` (`IServerDbManager`).
- Client: `Content.Client/IoC/ClientContentIoC.cs`, `Content.Client/Entry/EntryPoint.cs`.
- Shared: `Content.Shared/**/Shared*System.cs` bases.
- Fork: `_NF/Bank/BankSystem.cs`, `_NF/Shipyard/ShipyardSystem.cs`, `_CS/RolePlayIncentiveServer/RoleplayIncentiveSystem.cs`.

## 6. Dependency boundaries to respect

1. `Content.Shared` must not reference server/client code; put new cross-side types there.
2. All network messages live in `Content.Shared`; only register handlers per side.
3. The client only sees replicated state; anything private requires filters/`SessionSpecific`/`SendOnlyToOwner`.
4. DB access is server-only through `IServerDbManager`; never share DB models with the client.
5. Engine code is out of bounds; build features on engine APIs, do not patch `RobustToolbox/`.
6. Upstream files are patched in-place by forks (`// Frontier`, `// Coyote` markers). Prefer `_PS`/`_CS`
   modules and partial classes; when touching a core file, preserve marker comments so the patch remains identifiable.

## 7. Most important hazards (details in `.ai/HAZARDS.md`)

- 649 core `.cs` files are fork-patched (3,111 `// Frontier` markers) — never assume a file is upstream.
- Directed vs broadcast events: `RaiseLocalEvent<T>(msg)` does not reach component handlers.
- Subscribe only in `Initialize()`; subscriptions lock after startup.
- Dormant-but-present systems: Chat V2/censor, upstream antag presets, upstream cargo, `_Offbrand` module.
- Consent semantics: client-side checks always false; NPCs consent to everything; `Hypno`/`NoClone` unused;
  CharacterInfo leaks flavor/consent text.
- `LogType` numeric values and DB migrations are load-bearing; both providers must be updated.
- Engine differs from public docs: no `[EventHandler]`, no `GetPVSInterest`, no `RoundEndedEvent`, map format 7.
- Production config is not in the repo; defaults favor `nfpirate`/`Frontier`/`NFMapPool`.

## 8. Before modifying common systems, search these

| You want to change | Search first |
|---|---|
| GameTicker behavior | `GameTicker` partials incl. `_NF/GameTicking/GameTicker.NFSpawning.cs` (namespace collision is intentional) |
| Spawning/jobs | `StationSpawningSystem`, `StationJobsSystem`, `_NF` loadout/bank hooks in `SpawnPlayerMob` |
| Any component state | `[AutoGenerateComponentState]` / `[AutoNetworkedField]` / manual `ComponentGetState` |
| Chat/messages | `Content.Shared/Chat`, `MsgChatMessage`, `ChatManager`, `ChatSystem` |
| UI | the BUI + `UserInterfaceComponent` YAML + `ActivatableUISystem`, then client BUI class by `ClientType` |
| Prototypes | `Resources/Prototypes/<prefix>` + `ignoredPrototypes.yml` + `migration.yml`/`nf_migration.yml` |
| Persistence | `Model.cs`, both migration folders, `ServerDb*.cs`, and the drift test |
| Consent behavior | `Resources/Prototypes/consent.yml` + all `HasConsent` callers (grep) |
| NF economy/ships | `_NF/Bank`, `_NF/Shipyard`, `_NF/ShuttleRecords`, `_NF/Cargo`, `_NF/Market` |
| Coyote additions | `_CS/RolePlayIncentiveServer`, `_CS/Needs`, `_CS/Body`, `_CS/SniffAndSmell` |
| Input keys/contexts | `ContentKeyFunctions.cs`, `ContentContexts.cs`, `Resources/keybinds.yml` |
| Config | `Content.Shared/CCVar/CCVars.*`, `_NF/CCVar/NFCCVars.cs`, `Resources/ConfigPresets` |

## 9. Poorly understood areas (verify before relying)

- Actual production config/topology (presets, auth mode, DB, multi-server list).
- Reachability of dormant upstream content (antags, cargo, spells, Chat V2).
- `_Offbrand` module intent; `_PS` empty directories; minor `_CS` systems (healing bank is WIP).
- Deep engine internals (PVS budgets, physics solver, handshake, sandbox whitelist coverage).
- Privacy expectations around consent text exposure.
- See `.ai/UNKNOWN.md` for the full list.

## 10. External dependencies that must be consulted

| Dependency | When to consult |
|---|---|
| `RobustToolbox/` source (pinned commit) | Any engine API behavior, serialization, networking, UI, map format |
| SS14 developer docs (`docs.spacestation14.com`) and RobustToolbox wiki | General patterns, but **verify against this pin** — APIs differ |
| Frontier Station upstream (`new-frontiers-14/frontier-station-14`) | `_NF` merge semantics/upstream fixes |
| Wizden SS14 (`space-wizards/space-station-14`) | Upstream content origins and benchmarks |
| EF Core docs | Migrations/model changes (two providers) |
| Fluent docs | `.ftl` syntax and functions |
| NetCord docs | Discord integration changes |

## "How to Investigate This Repository"

```
1. Read .ai/README.md (this file).
2. Read the relevant system doc (see §4) or procedural guide (`.ai/guides/`) and the matching sections
   of ARCHITECTURE / DATA_FLOW / EVENTS / PERSISTENCE.
3. Search the source for the relevant symbols:
     grep -rn "SymbolName" Content.Shared Content.Server Content.Client
     grep -rn "protoId" Resources/Prototypes
   Remember fork folders (_NF/_CS/_PS) and in-place `// Frontier` patches.
4. Trace callers and dependencies:
     - component → subscribing systems (grep SubscribeLocalEvent<TComp)
     - event → producers and consumers (grep RaiseLocalEvent/RaiseNetworkEvent)
     - prototype → C# class ([Prototype("kind")])
     - BUI → ClientType class
     - partial class → search all files declaring it (namespace collisions are real)
5. Verify framework behavior against the pinned engine source (RobustToolbox), not public docs.
6. Check hazards: events delivery mode, subscription lock, paused entities, prediction,
   config defaults, both DB migrations, prototype migration entries.
7. Only then propose a change. For content changes prefer new prototypes + `_PS` code;
   for core changes preserve upstream patch markers and update `.ai/` docs if architecture shifts.
```

## Maintenance of this documentation

When making architectural changes, update the affected `.ai/` document in the same change. Line numbers
in these docs are valid for branch `palm3` commit `4bfdad813c` and will drift; prefer symbol names as anchors.
