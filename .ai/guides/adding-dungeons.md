# Guide: Adding an Asteroid / Salvage Dungeon

> Answer to: "How do asteroid dungeons work and how can one be added?"
> Facts below are from source; inferred items are marked. Line numbers are valid for branch `palm3`
> commit `4bfdad813c`.
>
> Fork convention: new Palmtree content belongs in `_PS` folders (`Resources/Prototypes/_PS/...`).
> Dungeon prototypes are global, so a `_PS` file can add `dungeonConfig`/`salvageDungeonMod` entries
> even though the generating systems live in `_NF`/upstream. The traced examples below live in `_NF`.

## TL;DR

Dungeons are **not map prototypes**. They are generated at runtime by `DungeonSystem` from a
`dungeonConfig` prototype (a list of generation layers) that copies rooms out of pre-authored "atlas"
maps. To add one you add (A) a `dungeonConfig`, and (B) a `salvageDungeonMod` hook that makes the
salvage-expedition system pick it for a biome. New room art requires (C) an atlas map + `dungeonRoom`
prototypes + a theme file.

## How it works

| Piece | C# type / path | Role |
|---|---|---|
| `dungeonConfig` | `Content.Shared/Procedural/DungeonConfig.cs:45` | Layer list (`PrefabDunGen`, `CorridorDunGen`, `DungeonEntranceDunGen`, …) |
| `dungeonRoom` | `Content.Shared/Procedural/DungeonRoomPrototype.cs:8` | A rectangle cut out of an atlas map (`atlas`, `offset`, `size`, `tags`) |
| `dungeonRoomPack` | `Content.Shared/Procedural/DungeonRoomPackPrototype.cs` | Slot layout of room sizes |
| `dungeonPreset` | `Resources/Prototypes/Procedural/dungeon_presets.yml` | Arrangement of packs (Bucket/Wow/SpaceShip/Tall) |
| `salvageDungeonMod` | `Content.Shared/Salvage/Expeditions/Modifiers/SalvageDungeonModPrototype.cs:8` | Expedition hook: `proto` + eligible `biomes` |
| `DungeonSystem` / `DungeonJob` | `Content.Server/Procedural/DungeonSystem.cs`, `Content.Server/Procedural/DungeonJob/` | Generation + atlas copying |
| `salvageMap` | `Content.Shared/Salvage/SalvageMapPrototype.cs` | Prebuilt wreck maps, **not** dungeons |
| `pointOfInterest` | `Content.Server/_NF/GameRule/PointOfInterestSystem.cs:21` | Prebuilt station grids in the sector, **not** dungeons |

### What actually spawns dungeons in live play

1. **Salvage expeditions (primary).** Station salvage console → mission →
   `SpawnSalvageMissionJob` generates a biome map and calls `DungeonSystem.GenerateDungeonAsync`
   (`Content.Server/Salvage/SalvageSystem.Expeditions.cs`, `SpawnSalvageMissionJob.cs:245-277`).
   The dungeon is chosen deterministically from biome/budget/seed via
   `SharedSalvageSystem.GetBiomeMod<SalvageDungeonModPrototype>` (`Content.Shared/Salvage/SharedSalvageSystem.cs:89-106`).
2. **Bluespace dungeon station events (VGRoid asteroids).** `BluespaceErrorRule`
   (`Content.Server/_NF/StationEvents/Events/BluespaceErrorRule.cs`) generates a `dungeonConfig` on a
   temp grid and FTLs it near the sector; scheduled by `BluespaceDungeonEventScheduler` (already in
   `NFAdventure`/`NFPirate` presets).
3. **Station grid fill** `DungeonSpawnGroup` (`Resources/Prototypes/Entities/Stations/base.yml:85-114`)
   spawns debris wrecks + one `VGRoid` per station (`ShuttleSystem.GridFill.cs:86-121`).
4. **Salvage magnet** (`SalvageSystem.Magnet.cs:274-312`) — see hazard below: asteroid configs are
   commented out in this fork; only debris offers currently resolve.

## Traced example: `NFMineshaft`

1. Hook: `Resources/Prototypes/_NF/Procedural/salvage_mods.yml:83-88` →
   `salvageDungeonMod NFMineshaft { proto: NFMineshaft, biomes: [Caves] }`.
2. Config: `Resources/Prototypes/_NF/Procedural/dungeon_configs.yml:254-311` →
   `PrefabDunGen roomWhitelist.tags: [NFMineshaft]` + corridor/entrance/wallmount/junction/cabling layers.
3. Rooms: `Resources/Prototypes/_NF/Procedural/Themes/mineshaft.yml` (e.g.
   `dungeonRoom NFMineshaft17x5a { size: 17,5; atlas: /Maps/_NF/Dungeon/mineshaft.yml; offset: 0,0; tags: [NFMineshaft] }`).
4. Atlas: `Resources/Maps/_NF/Dungeon/mineshaft.yml` (`meta.format: 6`, `postmapinit: false`), loaded
   and cached by `DungeonSystem.GetOrCreateTemplate` (`DungeonSystem.cs:167-191`).
5. Layout: `DungeonJob.DunGenPrefab.cs:17-215` picks preset → pack → exact-size room; missing sizes
   leave empty slots (logs "Unable to find room variant").
6. Player reaches it by claiming the expedition and FTLing the station grid
   (`SpawnSalvageMissionJob.cs:169-176,480-485`).

## Recipe A — new dungeon variant using existing room art (fastest)

1. Copy the `NFMineshaft` config block in `Resources/Prototypes/_NF/Procedural/dungeon_configs.yml`
   and change tiles/airlocks/wallmounts/clutter. Keep `PrefabDunGen.roomWhitelist.tags` pointing at an
   existing theme (e.g. `NFMineshaft`) or another theme's tag.
2. Add a `salvageDungeonMod` in `Resources/Prototypes/_NF/Procedural/salvage_mods.yml`:
   `proto: <YourConfigId>`, `biomes: [Caves, ...]`.
3. Add a loc string `salvage-dungeon-mod-<yourid> = ...` in
   `Resources/Locale/en-US/_NF/procedural/expeditions.ftl` (base example:
   `Resources/Locale/en-US/procedural/expeditions.ftl:71`).
4. Test with `dungen <mapId> <yourConfigId> <x> <y>` (see Testing).

No registration is needed: mission generation enumerates all `salvageDungeonMod` prototypes.

## Recipe B — full new theme (new rooms/atlas)

1. **Atlas map**: in mapping mode, build all room modules on one grid and save to
   `Resources/Maps/_NF/Dungeon/<theme>.yml` (`postmapinit: false`, `meta.format: 6`). Doors/windows
   must be placed so the post-gen layers (corridors, junctions, entrances) can connect rooms.
2. **Rooms**: `Resources/Prototypes/_NF/Procedural/Themes/<theme>.yml` with one `dungeonRoom` per
   module: `size` (w,h), `atlas`, `offset` (top-left tile in the atlas), `tags`.
3. **Config**: add a `dungeonConfig` with
   `PrefabDunGen { roomWhitelist.tags: [<theme>], presets: [Bucket, Wow, SpaceShip, Tall] }` plus the
   standard post-gen layers, copying `NFMineshaft`.
4. **Hook**: `salvageDungeonMod` + loc string as in Recipe A.
5. **Test**: `dungen` with the config; use `dungen_preset_vis`/`dungen_pack_vis` to see layouts.

### Atlas authoring requirements

- File must live under `Resources/Maps/` (loaded with `FileCategory.Map`); `postmapinit: false`.
- No `gameMap` prototype, no `BecomesStation` — `DungeonAtlasTemplateComponent` is added dynamically.
- Entities are respawned **by prototype ID only**: `Spawn(protoId)`, transform + anchored flag and
  decals are copied. Custom `DataField` values (locker/vending contents, custom names) are **lost**
  (`DungeonSystem.Rooms.cs:176-199`). Do not rely on map-only entities or pre-stocked containers.
- Room `tags` must match `roomWhitelist`; offsets must cover the module; sizes must exist in the
  `dungeonRoomPack` slots used by the preset, or rooms silently don't spawn.

## Recipe C — new bluespace VGRoid asteroid dungeon event

1. Root `dungeonConfig` in `Resources/Prototypes/_NF/Procedural/<name>_vgroid.yml` composed of
   `PrototypeDunGen` sub-configs (noise blob, `ExteriorDunGen { proto: <interior config> }`, paths),
   `EntityTableDunGen` room markers, `BiomeDunGen`, ores, mobs, and a `WarpPointDungeon` marker.
   Copy `Resources/Prototypes/_NF/Procedural/basalt_vgroid.yml:10-149`.
2. Event entity in `Resources/Prototypes/_NF/Events/nf_bluespace_dungeons_events.yml` (copy
   `BluespaceDungeonBasalt`), parent `BluespaceDungeonBase`, with the `BluespaceErrorRule` group
   `protos: [<root config>]` and the `addComponents` anchor (`Gravity`, `IFF`,
   `LinkedLifecycleGridParent`, `ProtectedGrid`, `PreventPilot`, static `Physics`,
   `DeletionCensusExempt`).
3. Add the event ID to `DungeonBluespaceEventsTable` in
   `Resources/Prototypes/_NF/GameRules/roundstart.yml:44-52`. Schedulers are already in the presets.

## Testing / debugging

| Action | Command |
|---|---|
| Generate a dungeon manually | `dungen <mapId> <dungeonConfigId> <x> <y> [seed]` (admin FUN; `DungeonSystem.Commands.cs:15-73`) |
| Visualize layout | `dungen_preset_vis <mapId> <presetId>`, `dungen_pack_vis <mapId> <packId>` |
| Force a bluespace event | `addgamerule BluespaceDungeonBasalt` (also Cave/Snow/Chromite/CaveZombie); `endgamerule` to clean up |
| Expedition testing | spawn `ComputerSalvageExpeditionDebug` (skips proximity check, `SalvageSystem.ExpeditionConsole.cs:100-101`) |
| Lazy atlas loading | CVar `procgen.preload false` (default true) |
| Prototype validation | `dotnet run --project Content.YAMLLinter` |

## Pitfalls

- **Room-size mismatch** → empty slots, only a log line (`DungeonJob.DunGenPrefab.cs:184-208`).
- **Atlas data loss** (see authoring requirements).
- **Budget exhaustion**: biome → light → temp → air → dungeon share `modifierBudget`; an expensive
  dungeon mod can make `GetBiomeMod` throw and fail the expedition
  (`SharedSalvageSystem.cs:89-106`, handled in `SpawnSalvageMissionJob.Process:134-159`).
- **Multiple eligible mods in one biome** → random selection; restrict `biomes` while testing.
- **Bluespace YAML tag ambiguity**: event YAML uses `!type:DungeonSpawnGroup`, but the only
  implementation is `BluespaceDungeonSpawnGroup`; it resolves only via loose suffix matching. Prefer
  `!type:BluespaceDungeonSpawnGroup` in new content.
- **Magnet path is broken in this fork**: `Resources/Prototypes/Procedural/Magnet/asteroid.yml`
  configs (`BlobAsteroid`, `ClusterAsteroid`, `SpindlyAsteroid`, `SwissCheeseAsteroid`) are commented
  out while `SharedSalvageSystem.Magnet.cs:24-30` still references them — asteroid magnet offers would
  fail `_proto.Index()`. Only debris offers work. Don't extend the magnet path without restoring them.
- **Shared/open-contract expeditions** use a capped 1×1 config with `ReserveTiles = true`
  (`SpawnSalvageMissionJob.cs:496-509`).
- **VGRoid grids** need the event `addComponents` or players can pilot/steal the asteroid; the grid is
  deleted on event end.

## CVars

`procgen.preload` (`Content.Shared/CCVar/CCVars.Misc.cs:14`); `salvage.expedition_cooldown`
(`CCVars.Salvage.cs:17`); `nf14.salvage.expedition_max_active`, `expedition_failed_cooldown`,
`expedition_travel_time`, `expedition_proximity_check` (`Content.Shared/_NF/CCVar/NFCCVars.cs:119-138`).

## Unknowns

- Whether `!type:DungeonSpawnGroup` loose resolution actually works at runtime (unverified without
  running the game); if bluespace events never fire, switch to `BluespaceDungeonSpawnGroup`.
- Whether the missing magnet asteroid configs are intentional removal or a fork regression.
- `CCVars.Salvage.cs:11` mentions a `SalvageTimeMod` that does not exist in this tree.
- No dungeon-specific automated test exists; YAMLLinter is the closest validation.

## Source anchors

`Content.Server/Procedural/{DungeonSystem.cs,DungeonJob/}`, `Content.Shared/Procedural/*`,
`Content.Server/Salvage/{SalvageSystem.Expeditions.cs,SpawnSalvageMissionJob.cs}`,
`Content.Shared/Salvage/SharedSalvageSystem.cs`, `Content.Server/_NF/StationEvents/Events/BluespaceErrorRule.cs`,
`Content.Server/_NF/Procedural/`, `Resources/Prototypes/_NF/Procedural/`, `Resources/Maps/_NF/Dungeon/`.
