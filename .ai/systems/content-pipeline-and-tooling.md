# System: Content Pipeline, Tooling & Tests

## Purpose

How content is authored, validated, tested, built and shipped: prototypes/YAML, localization,
migrations, linters, tests, packaging, CI and documentation.

## Prototype pipeline

```
Resources/Prototypes/**.yml
  → engine PrototypeManager (server + client separately, engine /EnginePrototypes first)
  → [DataField] deserialization into C# prototype/component classes
  → inheritance via parent:/abstract:
  → runtime errors logged per file and skipped
Resources/IgnoredPrototypes/ignoredPrototypes.yml → mark upstream prototypes abstract
Resources/{migration.yml,nf_migration.yml} → MapMigrationSystem rewrites entity IDs on map load
Content.YAMLLinter (CI) → ValidateDirectory + ValidateStaticFields, GitHub ::error annotations
```

- Static-field validation (`[ValidatePrototypeId<T>]`, `ProtoId<T>`) catches bad ID references.
- `Content.IntegrationTests/Tests/PrototypeTests/*` round-trips all prototypes (serialize, save, load, compare).
- No CI FTL validator; malformed Fluent keys only log.

## Localization

- `Resources/Locale/<culture>/{upstream folders,_NF,_CS,...}` — Fluent `.ftl`.
- `ContentLocalizationManager` registers custom functions (`PRESSURE`, `ENERGYWATTHOURS`, `PLAYTIME`, ...).
- `Content.IntegrationTests/Tests/Localization/LocalizedDatasetPrototypeTest.cs` checks datasets.
- Languages present: `en-US`, `nl-NL`, `pt-BR`, `ru-RU` (fork coverage limited).

## Maps

How-to: saving/loading grids and maps `.ai/guides/saving-grids.md`.

- Format: engine YAML map **format 7**; schema `RobustToolbox/Schemas/mapfile.yml` validated in CI (Yamale).
- Maps live in `Resources/Maps/**` (+ `_NF`, `_CS`); map prototypes in `Resources/Prototypes/Maps/**`.
- Renderer: `Content.MapRenderer` → `Resources/MapImages/` (`--format png|webp`, `--viewer`, markers).
- Map merge driver: `Content.Tools/MappingMergeDriver.cs` wired through `.gitattributes` and `Tools/mapping-merge-driver.sh`.
- Map checker CI (`.github/mapchecker/mapchecker.py`), shipyard tests CI (`nf-shipyard-tests.yml`).

## Tests

| Project | Contents / how to run |
|---|---|
| `Content.Tests` | Unit tests (`ContentUnitTest : RobustUnitTest`): chemistry, atmos, wires, damage, alerts, preferences, chat censor, IPIntel, job queue, localization, UI. `dotnet test Content.Tests/Content.Tests.csproj` |
| `Content.IntegrationTests` | Pooled server+client (`PoolManager`, `TestPair`, `PoolSettings`), `TestMap = "Empty"`, `[TestPrototypes]`, DB tests, serialization/prototype tests, `_NF/ShipyardTests`. `dotnet test ... --filter ...`; CI excludes `ShipyardTest` in the main job and runs it separately |
| `Content.Benchmarks` | BenchmarkDotNet (map load, entity fetch, net serializer, PVS); daily CI |

Key integration test infrastructure: `PoolManager.cs`, `PoolManager.Cvars.cs` (`database.sync=true`,
`nftest` preset, `GameMap=Empty`), `Pair/TestPair.cs`, `Pair/TestMapData.cs`, `PoolSettings.cs`.
`Content.Tests/Server/Connection/IPIntelTest.cs` mocks `IServerDbManager`.

## CI (`.github/workflows/`, 27 files)

| Workflow | Checks |
|---|---|
| `build-test-debug.yml` | DebugOpt build; `Content.Tests`; integration tests (excl. ShipyardTest), warnings as failures |
| `yaml-linter.yml` | Release build + `Content.YAMLLinter` |
| `test-packaging.yml` | server packaging 4 RIDs + client |
| `nf-shipyard-tests.yml` | path-filtered ShipyardTest |
| `validate_mapfiles.yml` / `nf-mapchecker.yml` | map schema + map prototype checks |
| `validate-rsis.yml` / `rsi-diff.yml` | RSI validation/diff |
| `validate-rgas.yml` | attribution YAML schema |
| `check-crlf.yml` | line endings |
| `build-map-renderer.yml`, `benchmarks.yml`, `build-docfx.yml` | tools/docs |
| `nf-validate-changelog.yml` | optional PR `:cl:` syntax validation (changelog writer/publisher workflows were removed; see `.ai/guides/changelogs.md`) |
| `publish.yml`, `publish-testing.yml`, `update-credits.yml`, labelers, `close-master-pr.yml`, `no-submodule-update.yml` | release/ops automation |

`bors.toml` requires statuses including a "Build & Test Release" that no workflow here produces (stale).

## Build & packaging

- Configurations: Debug/DebugOpt (DEBUG + tools), Tools (TOOLS), Release (RELEASE); packaging adds
  `FullRelease=true`, which enables `FULL_RELEASE`/`DEVELOPMENT` defines.
- `Content.Packaging`: `server|client` with `--platform`, `--configuration`, `--hybrid-acz`, etc.;
  server zips per RID; client zip. Distribution uses `Tools/publish_multi_request.py` (watchdog).
- Nix dev shell (`flake.nix`/`shell.nix`) with native libs; `RUN_THIS.py` installs git hooks and updates
  the engine submodule.

## Changelog

- Changelogs are **manual, client-side YAML**: the client loads every direct `Resources/Changelog/*.yml`
  as a tab (`ChangelogManager`) and tracks last-read per server id. Git commits are not scanned.
- Palmtree entries go in `Resources/Changelog/Palmtree.yml` (tab "Palmtree Station" via
  `Resources/Locale/en-US/_PS/changelog/changelog-window.ftl`). Draft them from commits with
  `Tools/_PS/generate_commit_changelog.py` or interactively with `Tools/manual_changelog.py`.
- The GitHub writer/publisher workflows were removed; only `nf-validate-changelog.yml` remains.
  Full workflow, verification and gotchas: `.ai/guides/changelogs.md`.

## Documentation

- `Content.Docfx/docfx.json` builds API docs on a weekly workflow; not in the solution.
- This `.ai/` directory is the AI-oriented documentation set.

## Conditional compilation (content-relevant)

| Define | Effects |
|---|---|
| `TOOLS` | engine hot reload/prototype reload paths, EF design-time factories, dev config preset, client scripting |
| `DEBUG` | fake net lag/loss defaults, migration target validation, debug commands, gun/melee asserts |
| `RELEASE` / `FULL_RELEASE` | packaging builds; release diagnostics; `DEVELOPMENT` set otherwise |
| `EXCEPTION_TOLERANCE` | engine robustness paths |
| `WINDOWS`/`LINUX`/`MACOS` + `UNIX` | platform detection in `Robust.DefineConstants.targets` |

## Dependencies

Engine PrototypeManager/serialization, YamlDotNet, ImageSharp, Yamale/GitHub Actions, NUnit,
BenchmarkDotNet.

## Depended on by

Every gameplay feature (prototypes/localization), release engineering, and future maintenance.

## Unknowns

- No Postgres test coverage; no FTL validation job.
- `Frontier.yml`/`Content.Docfx`/`Content.Tools` legacy status (see `.ai/UNKNOWN.md`).
