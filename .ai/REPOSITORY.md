# REPOSITORY — Inventory

> Source of truth is the code. Paths are relative to the repository root unless prefixed with `RobustToolbox/`.
> This file was produced from a read-only inspection of the working tree (branch `palm3`, commit `4bfdad813c`,
> engine submodule `RobustToolbox` @ `feb9e1db6` / v267.3.0).

## What this repository is

**Coyote Sector** (a.k.a. "Coyote Frontier") is a Space Station 14 game codebase — a multi-fork merge:

```
Space Station 14 (Wizden / space-wizards)
    └── Frontier Station (new-frontiers-14, prefix _NF)
            └── Coyote Sector (ARF-SS13/coyote-frontier, prefix _CS)
                    └── Palmtree Station maintenance (prefix _PS, branch palm3)
```

It runs on the **RobustToolbox** engine, vendored as a git submodule. Gameplay content is C# (ECS)
plus thousands of YAML prototypes. The engine is explicitly **not to be modified** by content maintainers.

## Quick facts

| Item | Value |
|---|---|
| Primary language | C# 12 (nullable enabled, warnings-as-errors for `nullable`) |
| Runtime | .NET 9 (`global.json` → SDK `9.0.100`, rollForward `latestFeature`) |
| Engine | RobustToolbox v267.3.0, submodule at `RobustToolbox/` |
| Build system | MSBuild via `dotnet`, solution `SpaceStation14.sln` |
| Package management | NuGet, central versions in `Directory.Packages.props` (imports engine versions) |
| Configurations | `Debug`, `DebugOpt`, `Release`, `Tools` (`Robust.Configurations.props`) |
| Content C# files (excl. obj/bin) | 7,929 |
| — Content.Server | 2,876 |
| — Content.Client | 1,579 |
| — Content.Shared | 3,227 |
| — Content.Server.Database / Content.Shared.Database | 242 / 5 |
| — Content.IntegrationTests / Content.Tests | 205 / 27 |
| Prototype YAML files | 4,107 under `Resources/Prototypes` |
| Tests | 442 `[Test]` methods in 198 files (NUnit) |
| CI workflows | 27 under `.github/workflows` |

## Top-level layout

| Path | Contents |
|---|---|
| `Content.Shared/` | Components, shared systems, prototypes, network messages, data fields; referenced by both sides |
| `Content.Server/` | Authoritative game logic, managers, game ticker, DB glue, admin |
| `Content.Client/` | Client systems, UI states, UI controllers, overlays, input, prediction |
| `Content.Server.Database/` | EF Core DbContexts and Migrations (SQLite + Postgres) |
| `Content.Shared.Database/` | Shared DB enums (`LogType`, `LogImpact`, `NoteType`, `NoteSeverity`, `TypedHwid`) |
| `Content.IntegrationTests/` | NUnit integration tests with pooled server+client instances |
| `Content.Tests/` | NUnit unit tests |
| `Content.Benchmarks/` | BenchmarkDotNet benchmarks |
| `Content.MapRenderer/` | Renders maps to images / viewer JSON |
| `Content.Packaging/` | Builds distributable client/server zips |
| `Content.YAMLLinter/` | Prototype/YAML static validation (CI gate) |
| `Content.Tools/` | Map merge driver (only command in this fork) |
| `Content.PatreonParser/` | CSV → `Resources/Credits/Patrons.yml` |
| `Content.Docfx/` | DocFX config (not in solution; weekly CI) |
| `Content.Replay/` | Replay playback client executable |
| `BuildChecker/` | Git hooks / submodule bootstrap helper used by `RUN_THIS.py` |
| `Pow3r/` | Standalone power-net simulation/debug GUI (not shipped) |
| `Resources/` | Prototypes, maps, locale, textures, audio, configs, changelogs |
| `Tools/` | Build/publish/changelog scripts + git merge driver wrapper |
| `RobustToolbox/` | Engine submodule (never modify) |
| `bin/` | Build output |
| `BuildFiles/` | macOS bundle assets |
| `.github/` | CI workflows, labeler, map checker |

## Solution projects

```
Content.Shared ──► Content.Shared.Database, Robust.Shared, Robust.Shared.Maths, Lidgren.Network
Content.Server ──► Content.Shared, Content.Server.Database, Content.Shared.Database,
                   Content.Packaging, Robust.Server, Robust.Shared, Lidgren.Network
Content.Client ──► Content.Shared, Robust.Client, Robust.Shared, ...
Content.IntegrationTests ──► Content.Client, Content.Server, Content.Shared, Robust.UnitTesting
Content.Tests ──► Content.Shared + side-specific IoC, Robust.UnitTesting
Content.Benchmarks/MapRenderer/YAMLLinter/Packaging ──► Content.Server+Client+Shared
Content.Server.Database ──► EF Core Sqlite.Core + Npgsql (library)
Pow3r, Content.PatreonParser, Content.Tools, Content.Replay — leaf tools
```

Engine projects (submodule): `Robust.Shared`, `Robust.Shared.Maths`, `Robust.Server`, `Robust.Client`,
`Robust.UnitTesting`, `Robust.Analyzers`, `Robust.Shared.CompNetworkGenerator`,
`Robust.Serialization.Generator`, `Robust.Client.NameGenerator`, `Robust.Packaging`, `NetSerializer`,
`Lidgren.Network`, `XamlX.*`, `Robust.Client.Injectors`, `Robust.Generators` (empty placeholder).

## Entry points

| Executable | Bootstrap |
|---|---|
| Server | `Content.Server/Program.cs` → `ContentStart.Start(args)` → `Entry/EntryPoint.cs : GameServer` |
| Client | `Content.Client/Program.cs` → `ContentStart.Start(args)` → `Entry/EntryPoint.cs : GameClient` |
| Shared module | `Content.Shared/Entry/EntryPoint.cs : GameShared` |
| Replay client | `Content.Replay/Program.cs` (config `replay.toml`) |
| Tools | `Content.MapRenderer/Program.cs`, `Content.Packaging/Program.cs`, `Content.YAMLLinter/Program.cs`, `Content.Tools/MappingMergeDriver.cs`, `Content.PatreonParser/Program.cs`, `Pow3r/Program.cs` |

## Languages / formats

| Format | Usage |
|---|---|
| C# | All game logic (ECS: components are data, systems are behavior) |
| YAML | Prototypes (`Resources/Prototypes`), maps (`Resources/Maps`, format 7), migrations (`migration.yml`, `nf_migration.yml`), changelogs |
| Fluent `.ftl` | Localization under `Resources/Locale/<culture>/` |
| TOML | Runtime config + `Resources/ConfigPresets/**` |
| XAML | Client UI; loaded by engine `RobustXamlLoader`, typed name refs generated by `Robust.Client.NameGenerator` |
| JSON | `.rsi` sprite metadata, DB jsonb columns |
| Python / JS / bash / ps1 | Tooling, CI, migration scripts |

## Build & run

```
python RUN_THIS.py          # installs git hooks, updates submodules (BuildChecker/git_helper.py)
dotnet build                # debug build
dotnet run --project Content.Server      # runserver.sh
dotnet run --project Content.Client      # runclient.sh
dotnet run --project Content.Server --configuration Tools   # runserver-Tools.sh (hot reload etc.)
dotnet test Content.Tests/Content.Tests.csproj
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj   # excludes ShipyardTest in CI
dotnet run --project Content.YAMLLinter
dotnet run --project Content.MapRenderer [ids|files]
dotnet run --project Content.Packaging server|client
```

Nix users: `flake.nix` / `shell.nix` / `.envrc` provide a dev shell with SDL2, OpenAL, GTK, .NET 9.
`bors.toml` lists required CI statuses (some stale — see `.ai/HAZARDS.md`).

## Runtime directory

- Server user data defaults to `data/` next to the executable (or `--data-dir` in engine `BaseServer`),
  containing `preferences.db` (SQLite default), `replays/`, saved maps, etc.
- Server config: `server_config.toml` (engine-loaded), CVars via CLI/env (`ROBUST_CVARS`,
  `ROBUST_CVAR_<NAME>`), `Resources/ConfigPresets/*.toml` loaded by
  `Content.Server/Entry/EntryPoint.cs:201-243`.
- Client config: `client_config.toml` in the client user-data dir.

## Generated / vendored / non-editable

| Item | Notes |
|---|---|
| `RobustToolbox/**` | Engine submodule; do not modify (maintainer constraint) |
| `RobustToolbox/NetSerializer`, `Lidgren.Network`, `XamlX`, `Avalonia.Base`, `cefglue` | Vendored engine deps |
| Source-generated code | Component network auto-states, serialization `ISerializationGenerated<T>`, XAML name fields, component pause handling. Imports in `RobustToolbox/MSBuild/*.targets` |
| `bin/`, `obj/` | Build artifacts (not source) |
| `identifier.sqlite` | 0-byte tracked file at repo root, referenced nowhere — likely accidental (hazard) |

## Tests / tooling quick map

| Tool | Purpose |
|---|---|
| `Content.IntegrationTests` | `PoolManager` pairs a server+client; `TestMap = "Empty"`; ~100 test folders incl. `_NF/ShipyardTests` |
| `Content.Tests` | Unit tests (chemistry, atmos, wires, chat censor, localization, IPIntel, preferences) |
| `Content.YAMLLinter` | Loads all prototypes on server+client, `ValidateStaticFields`; CI `::error` annotations |
| Migration system | `Resources/migration.yml` + `nf_migration.yml` applied on map load (`MapMigrationSystem`) |
| Changelog | `Resources/Changelog/Palmtree.yml`, manual entries (draft from commits with `Tools/_PS/generate_commit_changelog.py`); see `.ai/guides/changelogs.md` |
| Packaging | `Content.Packaging` zips per RID; `Tools/publish_multi_request.py` for deployment |
| Map renderer | `Content.MapRenderer <mapId|all>` → `Resources/MapImages/` |

## Related documentation

- Architecture layers: `.ai/ARCHITECTURE.md`
- Subsystem deep dives: `.ai/systems/`
- Procedural how-to guides (dungeons, NPCs, UI, gamemodes, commands, species, grids): `.ai/guides/`
- Hazards and unknowns: `.ai/HAZARDS.md`, `.ai/UNKNOWN.md`
- Maintainer-provided context (not authoritative for behavior): `.ai/HUMAN_CONTEXT.md`
