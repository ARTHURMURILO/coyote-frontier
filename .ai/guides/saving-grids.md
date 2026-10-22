# Guide: Saving and Loading Grids (and Maps)

> Answer to: "How does saving grids work?"
> Facts from source; inferred items marked. Line numbers valid for branch `palm3` commit `4bfdad813c`.

## TL;DR

Grids are saved through the engine's `MapLoaderSystem` (YAML, map format v7). There are server-console
commands (`savegrid`, `loadgrid`, `savemap`, `loadmap`), content admin commands (`mapping`,
`toggleautosave`, `resave`, `persistencesave`, `loadgamemap`), client mapping mode (Ctrl+S downloads
YAML), and code APIs (`TrySaveGrid`/`TryLoadGrid`). **All saves write to the server `data/` user-data
directory, never into `Resources/`** — shipping a map means copying the YAML into the repo manually.

## Commands

### Engine server-console commands (`RobustToolbox/Robust.Server/Console/Commands/MapCommands.cs`)

These have no `[AdminCommand]`, so `AdminManager` treats them as **server-console-only**
(`Content.Server/Administration/Managers/AdminManager.cs:543-546`) — remote admins cannot run them.

| Command | Args | Behavior | Writes |
|---|---|---|---|
| `savegrid` | `<netEntityId> <path>` | `TrySaveGrid` (`:22,47`) | UserData |
| `loadgrid` | `<mapId> <path> [x y [rot [storeUids]]]` | `TryLoadGrid` (`:77,152`), creates map if missing, no map-init | — |
| `savemap` | `<mapId> <path> [force]` | `TrySaveMap` (`:167,219`); refuses initialized maps unless `force` (`:211-216`) | UserData |
| `loadmap` | `<mapId> <path> [x y [rot [storeUids]]]` | `TryLoadMapWithId` (`:236,329`) | — |

### Content commands

| Command | Path | Notes |
|---|---|---|
| `mapping <MapID> [Path] [Grid]` | `Content.Server/Mapping/MappingCommand.cs:22` | `[AdminCommand(Server|Mapping)]`; creates a paused pre-init map, starts autosave, enters mapping mode |
| `toggleautosave <mapId> [path]` | `Content.Server/Mapping/MappingSystem.cs:41-44,135-158` | Same flags |
| `resave` | `Content.Server/Maps/ResaveCommand.cs:23` | `AdminFlags.Host`; loads every `/Maps/` content file and rewrites to UserData at the same relative path (`:40,65,69`) |
| `persistencesave <mapId> [path]` | `Content.Server/Administration/Commands/PersistenceSaveCommand.cs:18` | `AdminFlags.Server`; default path from `game.map` |
| `loadgamemap <mapId> <gameMapProto> [x y [name]]` | `Content.Server/Administration/Commands/LoadGameMapCommand.cs:15` | Merges if the map exists (`:51-53`) |
| `purchaseshuttle <station> <gridfile>` | `Content.Server/_NF/Shipyard/Commands/PurchaseShuttleCommand.cs:18` | NF shipyard |

Client mapping mode: Ctrl+S (`Content.Client/Mapping/MappingState.cs:745-755`, compiled out under
`FULL_RELEASE`, requires `AdminFlags.Host`) → `MappingSaveMapMessage` → server serializes the map the
admin is on (`Content.Server/Mapping/MappingManager.cs:39-58`) → client writes the YAML via a local
file dialog (`Content.Client/Mapping/MappingManager.cs:46-68`).

## Engine serialization internals

`RobustToolbox/Robust.Shared/EntitySerialization/Systems/MapLoaderSystem*.cs`:

- `TrySaveGrid` (`Save.cs:149`) — serializes the grid root + children; rejects map entities.
  `EntitySerializer.SerializeEntityRecursive` sets `Truncate = root's parent` (`EntitySerializer.cs:246`),
  so the parent map is not written; the grid lands in the `orphans` section.
- `TrySaveMap` (`Save.cs:101,113`) — map root + children; requires exactly one map entity; grids may be
  embedded or orphaned.
- `TrySaveGeneric` (`Save.cs:193,207`) — arbitrary entity subtree.
- Category validation on save: map requires exactly 1 map (`EntitySerializer.cs:804`); grid requires
  0 maps + exactly 1 grid (`:807-809`).
- Writes always go to `IResourceManager.UserData` (`MapLoaderSystem.cs:45-56`); reads check
  **UserData first, then mounted content** (`:96-117`).
- Format v7: `EntitySerializer.MapFormatVersion = 7` (`:44`); deserializer supports v3-v7
  (`EntityDeserializer.cs:41-43`). Sections: `meta, maps, grids, orphans, nullspace, tilemap, entities`
  (`:691-708`). Tiles are base64 chunks with a v7 rotation/mirroring byte (`MapChunkSerializer.cs:76-137`).

### What is preserved

- Per-entity `uid`, `mapInit: true`/`paused` flag (`EntitySerializer.cs:508-525`), and per-component
  **diffs against the prototype cache** (`:621-663`) — any `DataField` that differs from the prototype
  (e.g. a stocked vending machine inventory) is preserved.
- Components marked `[UnsavedComponent]` are skipped (`:628`; engine-internal only: `YamlUidComponent`,
  `MapSaveTileMapComponent`, `LoadedMapComponent`). Prototypes with `mapSavable: false` are excluded
  wholesale (`:192`); none in this fork.
- `EntityUid`/`NetEntity`/`MapId` references become YAML integers; references to entities outside the
  saved set become `invalid` unless included via `MissingEntityBehaviour` (`:976-1040`).
- `DeserializationOptions.StoreYamlUids` keeps ids stable on re-save; `MapSaveTileMapComponent` does the
  same for tile ids (`:301-344`).
- **Grid collision fixtures are not saved** — they are regenerated from tiles
  (`SharedGridFixtureSystem.cs:47-64`; chunk reads suppress per-tile regeneration).
- `postmapinit`: v7 stores per-entity `mapInit`; on load, post-init entities are only flagged
  `MapInitialized` — **`MapInitEvent` does not run** (`EntityDeserializer.SetMapInitLifestage :1015-1032`).
  Pre-init entities are paused unless `DeserializationOptions.InitializeMaps`.

### Options

`RobustToolbox/Robust.Shared/EntitySerialization/Options.cs`: `SerializationOptions.MissingEntityBehaviour`
(default `IncludeNullspace`), `ExpectPreInit`; `DeserializationOptions.InitializeMaps`/`PauseMaps`;
`MapLoadOptions.MergeMap/Offset/ForceMapId/ExpectedCategory`. `FileCategory` in `SerializationEnums.cs:10-38`.

### Runtime map prototypes

`type: gameMap` → `GameMapPrototype` (`Content.Server/Maps/GameMapPrototype.cs:19-46`, `MapPath :45`,
`IsGrid :31`); loaded by `GameTicker.LoadGameMap` (`GameTicker.RoundFlow.cs:190-234`). `GameMapManager`
swaps in a UserData persistence save when `game.usepersistence` is set
(`Content.Server/Maps/GameMapManager.cs:55-70`). There is **no `map.save_directory` CVar**; mapping
autosave uses `mapping.autosave_dir = "Autosaves"` (`Content.Shared/CCVar/CCVars.Mapping.cs:22-23`).

## Traced NF Bluespace Drydock (save/load of a live ship)

`Content.Server/_NF/Shipyard/Systems/BluespaceDrydockSystem.cs`:

**Store:**
1. Requires a docked shuttle (`:143`), powers systems down, deletes the owning station (`:177-183`), undocks.
2. `TrySerializeShuttleAsync` (`:466-531`): `_mapLoader.TrySaveGrid` to a temp path in UserData (`:482`)
   with `MissingEntityBehaviour.IncludeNullspace, ErrorOnOrphan=false` (`:474-479`); if it fails
   (station/other-map refs) retries with `Ignore` (`:487-498`); reads the YAML text back and deletes
   the temp file.
3. Stores the raw YAML string on an ID card: `BluespaceStorageComponent.StoredGridData`
   (`:237-242`; component `Content.Shared/_NF/Shipyard/Components/BluespaceStorageComponent.cs:9-17`,
   `AutoNetworkedField`); deletes the grid.

**Retrieve:**
1. Writes the string to a temp file, `_map.CreateMap`, `_mapLoader.TryLoadGrid` (`:538-552`).
2. Manually raises `GridInitializeEvent` (`:574-575`) and per-entity `MapInitEvent` (`:580-606`),
   skipping `PoweredLightComponent`, `ItemSlotsComponent`, `DockingComponent` to avoid duplicate
   bulbs/items/joints; deletes the temp file.
3. Creates the vessel station, restores `ShuttleDeedComponent`, FTL-docks, clears storage (`:374-427`).
   Note: `TryLoadGrid` onto the initialized temp map already map-inits entities
   (`MapLoaderSystem.Load.cs:178-179`), so the manual raises are effectively a second initialization —
   a known NF workaround.

Static ship spawning (not mid-round serialization): `ShipyardSystem.TryAddShuttle` loads
`vessel.ShuttlePath` via `TryLoadGrid` into a paused shipyard map
(`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.cs:143-200`). Vessel prototypes are both
`type: vessel` and `type: gameMap` pointing at the same file
(`Resources/Prototypes/_NF/Shipyard/adder.yml:11-29`).

## Step-by-step recipes

### Save a grid built in-game (server console)
1. Find the grid's NetEntity id (`vv`, entity menu, or `savegrid` completion).
2. `savegrid <netEntityId> /shuttles/myship.yml` → `<server>/data/shuttles/myship.yml`.
3. Load back: `loadgrid <mapId> /shuttles/myship.yml` (creates the map pre-init if needed).

### Mapping-mode workflow (recommended for mappers)
1. `mapping 0 /Maps/Shuttles/foo.yml true` (or no path for blank) — pre-init map + autosave + mapping UI.
2. Edit; autosaves land in `<server>/data/Autosaves/<name>/<timestamp>-AUTO.yml`.
3. Ctrl+S downloads the current map YAML to a local path (only the map the admin is on, not a lone grid).
4. Ship it: copy the YAML into `Resources/Maps/...`, then add the `gameMap` prototype (and `vessel`
   prototype for ships). Keep it pre-map-init (`NoSavedPostMapInitTest` enforces this,
   `Content.IntegrationTests/Tests/PostMapInitTest.cs:244-263`).

### Save/load from code
```csharp
// save
using var writer = _resources.UserData.OpenWriteText(path);
_mapLoader.TrySaveGrid(gridUid, writer, new SerializationOptions { MissingEntityBehaviour = ... });
// load
_mapLoader.TryLoadGrid(targetMapId, path, out var grid, new MapLoadOptions { Offset = pos, ... });
```

## Pitfalls

- **Player-run grid mid-round**: sessions are not entities; player mobs parented to the grid serialize
  normally. Nullspace references (minds, power/gas nets, device lists) are included with
  `IncludeNullspace`; references to non-nullspace entities (station, other grids) log errors and are
  dropped unless `Ignore`/other behaviour is used.
- **Content save hooks**: `NetworkConfiguratorSystem` closes configurator UIs and clears active device
  lists before serialization; `DeviceListSystem` prunes cross-map devices
  (`Content.Server/DeviceNetwork/Systems/*`).
- **Post-init saves**: `savemap` refuses initialized maps without `force`; autosave stops once entities
  become map-initialized (`MappingSystem.cs:68-73`). Loaded post-init entities never re-run `MapInitEvent`.
- **UIDs/references** to entities outside the saved set become `invalid`; re-save fidelity needs
  `StoreYamlUids`/`MapSaveTileMapComponent` (both regenerated via deserializer options).
- **Grid fixtures** are rebuilt from tiles; per-tile fixtures and broadphase state are not saved.
- **Paths**: all writes go to `data/`; `resave` also writes to UserData, not `Resources/`.
- **Engine save commands are server-console-only**; content commands require their admin flags.
- **Docs stale**: `RobustToolbox/docs/Map Format.md` describes format v2, not v7.

## Unknowns

- Whether any fork-specific system hooks `OnIsSerializable`/`AfterSerializationEvent` (searches found
  none in `Content.Server`).
- `GameMapManager` persistence has no automatic saver; `persistencesave` must be invoked by tooling.

## Source anchors

`RobustToolbox/Robust.Shared/EntitySerialization/{Systems/MapLoaderSystem*.cs,EntitySerializer.cs,EntityDeserializer.cs,MapChunkSerializer.cs,Options.cs}`,
`RobustToolbox/Robust.Server/Console/Commands/MapCommands.cs`,
`Content.Server/Mapping/{MappingCommand,MappingSystem,MappingManager}.cs`,
`Content.Server/Maps/{ResaveCommand,GameMapManager}.cs`,
`Content.Server/Administration/Commands/{PersistenceSaveCommand,LoadGameMapCommand}.cs`,
`Content.Server/_NF/Shipyard/Systems/BluespaceDrydockSystem.cs`.
