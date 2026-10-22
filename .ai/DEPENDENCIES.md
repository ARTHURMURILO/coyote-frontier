# DEPENDENCIES

## Project dependency graph (compile-time)

```
                      ┌────────────────────┐
                      │ Content.Shared     │
                      └─────┬──────┬───────┘
                            │      │
              ┌─────────────┘      └──────────────┐
              ▼                                   ▼
     ┌────────────────┐                  ┌────────────────┐
     │ Content.Server │                  │ Content.Client │
     └───┬────────┬───┘                  └───────┬────────┘
         │        │                              │
         │        └──────────────┐               │
         ▼                       ▼               ▼
 Content.Server.Database   Content.Shared.Database
         │
         ▼
   EF Core (SQLite / Npgsql)          (engine projects: Robust.Server / Robust.Client / Robust.Shared)
```

- `Content.Shared` references only `Content.Shared.Database` + engine shared projects (`Content.Shared.csproj:14-25`).
- `Content.Server` additionally references `Content.Server.Database`, `Content.Packaging`, Lidgren
  (`Content.Server.csproj:21-28`).
- `Content.Client` references engine client only (+ shared).
- No `Content.*` project references `RobustToolbox` gameplay-implementation assemblies directly except
  `Robust.Server`, `Robust.Client`, `Robust.Shared`, `Robust.Shared.Maths`, `Lidgren.Network`.
- Tool projects fan out to both sides: IntegrationTests/Benchmarks/MapRenderer/YAMLLinter → Server+Client+Shared.

## Engine vs content split

```
Engine (RobustToolbox)                      Content
──────────────────────                      ───────
ECS storage/lifecycle/event bus      ◄──►   Components + systems
Prototype engine + YAML serializer   ◄──►   4,107+ prototype files
Net transport + PVS + prediction     ◄──►   NetMessages, component states, filters
Map/grid/transform/physics           ◄──►   Map prototypes, shuttles, fixtures
UI framework (Clyde/XAML)            ◄──►   Screens, UIControllers, XAML, stylesheets
CVar registry/replication            ◄──►   Content.Shared/CCVar/CCVars.*
Console/toolshed discovery           ◄──►   Content.Server/Commands, toolshed commands
Admin permission hook (interface)    ◄──►   AdminManager + IPermissionController
```

## Content-subsystem dependency chains (runtime, important ones)

```
GameTicker
 ├─► GameMapManager ──► PrototypeManager (gameMap/gameMapPool)
 ├─► MapLoaderSystem (engine) ──► prototypes, migration.yml
 ├─► StationJobsSystem ──► StationSystem, JobPrototype, Preferences
 ├─► StationSpawningSystem ──► SharedBodySystem, HumanoidAppearance, LoadoutSystem,
 │                             NF BankSystem (loadout affordability), MindSystem
 ├─► SharedMindSystem / SharedRoleSystem ──► AntagSelection, JobRoleComponent
 ├─► UserDbDataManager ──► IServerDbManager, Preferences, Consent, Playtime, Whitelist
 ├─► GhostSystem/GhostRoleSystem
 └─► ReplayRecordingManager (engine) via GameTicker.Replays
```

```
Player connect
 └─► ConnectionManager ──► BanManager ──► IServerDbManager
                       ──► MiniAuthManager (_NF) ──► HTTP /admin/info of peer servers
                       ──► IPIntel / whitelist prototypes
 └─► UserDbDataManager (parallel load)
       ├─► ServerPreferencesManager ──► DB (profile JSON/scalars)
       ├─► PlayTimeTrackingManager ──► DB
       ├─► JobWhitelistManager ──► DB
       ├─► PlayerRateLimitManager
       └─► ServerConsentManager ──► DB
 └─► GameTicker.Player ──► ContentPlayerData, mind spawn/attach
```

```
Hands/Inventory/Storage cluster (most depended-upon)
 └─► Interaction/DoAfter/Containers
 └─ consumed by weapons, medical, construction, NPC operators, cargo, lathes
Body/Damage/Stamina/Bloodstream
 └─ consumed by chemistry/metabolism, atmos barotrauma, weapons, NPC combat, stuns, medical
Atmos ⇄ NodeContainer/NodeGroup ⇄ Power/DeviceNetwork
      └─ Wires/DeviceLinking glue doors, conveyors, triggers, shuttles
```

```
Frontier economy chain
 MarketSystem ──► NFCargoSystem ──► StationBank/sector accounts
 ShipyardSystem ──► ShuttleDeedSystem ──► ShuttleRecordsSystem ──► BluespaceDrydock (map save/load)
 BountyContractSystem ──► BankSystem
 RoleplayIncentiveSystem (_CS) ──► BankSystem (direct dependency)
 PublicTransitSystem ──► shuttle grids + POI prototypes
 NfAdventureRuleSystem ──► PointOfInterestSystem ──► Worldgen
```

```
Client chain
 States ──► UIControllers ──► BUIs ──► SharedUserInterfaceSystem (engine)
 ClientGameTicker ──► states
 Prediction ──► shared systems (IGameTiming, RaisePredictiveEvent)
```

## Coupling hotspots / circular or inverted relationships

| Coupling | Detail |
|---|---|
| Fork partials extend core classes | `_NF` files deliberately reuse upstream namespaces (`GameTicker.NFSpawning.cs`, `_NF/Shuttles/Systems/ShuttleSystem.cs`, `_Emberfall` gun system) so fork code is spliced into core classes without changing base files |
| `Content.Server` and `Content.Client` depend on `Content.Shared`, never each other | Network messages are the only cross-side channel |
| Consent ↔ Preferences | `ServerConsentManager.ReloadCharacterConsent` is invoked from `ServerPreferencesManager`; consent freetext is stored on the profile row |
| NF Bank ↔ Preferences/DB | `HumanoidCharacterProfile.BankBalance` lives in character profiles; DB save path clamps to stored balance (anti-cheat) |
| `_CS` RPI → `_NF` Bank | Coyote's economy depends directly on the parent fork's banking system |
| Salvage expedition core patched | `Content.Server/Salvage/SalvageSystem.Expeditions.cs` contains NF hooks/CVars; `_NF/Salvage` adds components/systems |
| `_PS`/`_CS` ERP systems → upstream Consent | All check `ConsentSystem.HasConsent` with toggle prototypes from `Resources/Prototypes/consent.yml` |
| `Pending` | No cyclic project references exist; runtime coupling is via events and IoC |

## External dependencies (NuGet / system)

| Dependency | Used by | Notes |
|---|---|---|
| Microsoft.EntityFrameworkCore.Sqlite.Core | Content.Server.Database | Default provider |
| Npgsql.EntityFrameworkCore.PostgreSQL | Content.Server.Database | Optional provider |
| NetCord | Content.Server | Discord gateway/webhooks |
| Lidgren.Network | engine submodule | UDP transport |
| Veldrid / OpenTK / ImGui.NET | Pow3r | Standalone tool only |
| CsvHelper | Content.PatreonParser | CSV parsing |
| NUnit / NUnit3TestAdapter / Microsoft.NET.Test.Sdk | tests | |
| BenchmarkDotNet | Content.Benchmarks | |
| SixLabors.ImageSharp | Content.MapRenderer | map images |
| YamlDotNet | Content.Tools | map merge driver |
| SDL2/OpenAL/GTK (native) | client via Nix shell | runtime audio/windowing |
| `RobustToolbox/NetSerializer`, `XamlX`, `Avalonia.Base`, `cefglue` | engine submodule | vendored |

## Engine version coupling

- Engine commit `feb9e1db6`, version 267.3.0 (`RobustToolbox` submodule).
- Content relies on engine specifics such as: auto component state generator, `AllEntityQueryEnumerator`,
  `SharedUserInterfaceSystem` prediction APIs, map format 7, `EntityPausedEvent`, `[AutoGenerateComponentPause]`.
  Updating the engine requires re-checking these (see `.ai/HAZARDS.md`).
- `Directory.Packages.props` imports the engine's package versions and removes EF packages that only
  engine benchmarks use.
