# System: Game Ticker & Round Lifecycle

## Purpose

Owns the round state machine, game presets/rules, map loading, player joining/spawning, lobby behavior,
round end, and replay triggers. This is the backbone of the server.

## Location

| Piece | Path |
|---|---|
| Ticker core | `Content.Server/GameTicking/GameTicker.cs` + partials (`.RoundFlow`, `.Player`, `.Spawning`, `.GamePreset`, `.GameRule`, `.Lobby`, `.Replays`, `.CVars`, `.StatusShell`, `.LobbyBackground`, NF `.NFSpawning` in `_NF/GameTicking`) |
| Shared base + ticker events | `Content.Shared/GameTicking/SharedGameTicker.cs` |
| Game rules | `Content.Server/GameTicking/Rules/*`, `Content.Shared/GameTicking/Components/GameRuleComponent.cs` |
| Game presets | `Resources/Prototypes/game_presets.yml` (inert), `Resources/Prototypes/_NF/game_presets.yml` (live) |
| Maps | `Content.Server/Maps/GameMapManager.cs`, `Resources/Prototypes/Maps/**`, `Resources/Prototypes/_NF/Maps/**` |
| Lobby / job UI data | `GameTicker.Lobby.cs`, `GameTicker.StatusShell.cs`, `Content.Client/GameTicking/Managers/ClientGameTicker.cs` |
| Round end | `Content.Server/RoundEnd/RoundEndSystem.cs` |

## Round state machine

States: `PreRoundLobby → InRound → PostRound` (`RoundFlow.cs:63-76`). There is no `RoundIdle`.

| Transition | Trigger | Key methods |
|---|---|---|
| startup → lobby | `GameTicker.PostInitialize` (`GameTicker.cs:117`) → `RestartRound` | `RestartRound`, `UpdateRoundFlow` |
| lobby → in-round | lobby countdown expires (`game.lobbyduration`, default 180 s) or forced start/admin | `StartRound` (`RoundFlow.cs`), `StartRoundInternal`, `LoadMaps`, `SpawnPlayers` |
| in-round → post-round | `RoundEndSystem.EndRound`, emergency shuttle, admin `endround`, rule end conditions, max time/inactivity rules, `ServerApi` | `EndRound`, `PostRound`, `RoundEndTextAppendEvent` |
| post-round → lobby | restart timer (`game.round_restart_time`, default 120 s) | `AfterEndRoundRestart` (`RoundEndSystem.cs:336`), `GameTicker.RestartRound` |
| process shutdown | `ServerUpdateManager`, empty-server timer, uptime restart | `IBaseServer.Shutdown` |

## Game presets and rules

- Preset prototypes: `gamePresets`; each lists rule prototype IDs.
- **Live:** `NFAdventure`, `NFPirate` (default), `NFTest` (`Resources/Prototypes/_NF/game_presets.yml`).
  All Wizden presets in `Resources/Prototypes/game_presets.yml` have `rules: []` (commented out by Frontier).
- Rule machinery: `GameRuleSystem<T>` (`Content.Server/GameTicking/Rules/GameRuleSystem.cs:10`),
  `GameTicker.GameRule.cs:16` spawns/starts/stops rule entities, events `GameRuleAddedEvent`,
  `GameRuleStartedEvent`, `GameRuleEndedEvent`.
- Rule variations: `Rules/VariationPass/*`; `SubGamemodesSystem` present but unused by shipped presets.
- NF rules: `NFAdventure` (`Content.Server/_NF/GameRule/NfAdventureRuleSystem.cs:33`),
  `NFBasicStationEventScheduler`, bluespace event schedulers, `SmugglingEventScheduler`,
  `NFRoundstartVariation`, `NFPirateRuleSystem`.
- Antags: `AntagSelectionSystem` (`Content.Server/Antag/AntagSelectionSystem.cs:40`) is invoked from
  `RulePlayerSpawningEvent`/`RulePlayerJobsAssignedEvent`; upstream antag rule content is dormant but the
  framework is live and used by NF pirates (see `.ai/systems/frontier-nf-systems.md`).

Step-by-step recipe for a new gamemode (preset + rules): `.ai/guides/adding-gamemodes.md`.

## Map loading

- `GameMapManager.GetSelectedMap()`: `game.map` CVar default `Frontier` short-circuits rotation/votes;
  otherwise map pool `game.map_pool` (default `NFMapPool`) with rotation (`game.map_rotation`) and memory.
- `GameTicker.RoundFlow.LoadMaps` (`:94-333`) uses engine `MapLoaderSystem`;
  `MapMigrationSystem` applies `migration.yml` + `nf_migration.yml` (`BeforeEntityReadEvent`).
- Persistence map support: `game.usepersistence`/`game.persistencemap`, save command
  (`Content.Server/Administration/Commands/PersistenceSaveCommand.cs`).
- Map lifecycle events raised: `LoadingMapsEvent`, `PreGameMapLoad`, `PostGameMapLoad` (`RoundFlow.cs:865-898`).

## Player join / spawn flow

- Connect: `GameTicker.Player.PlayerStatusChanged` (`:28-179`) — lobby or spawn, `ContentPlayerData`,
  PVS override for mind entity, DB load via `UserDbDataManager`.
- Round-start: `SpawnPlayers` (`Spawning.cs:69-139`) → `RulePlayerSpawningEvent` → `AssignJobs` →
  `SpawnPlayer` per player.
- `SpawnPlayer` (`:164-378`): job bans/`IsJobAllowedEvent`, `PlayerBeforeSpawnEvent`, job pick,
  mind create, `StationSpawningSystem.SpawnPlayerCharacterOnStation`, job role via `SharedRoleSystem`,
  `JobTrackingComponent` (NF), announcements, `PlayerSpawnCompleteEvent` (directed + broadcast).
- Late join: `JoinGameCommand` (`Content.Server/GameTicking/Commands/JoinGameCommand.cs`),
  `DisallowLateJoin` fallback to observer (`Spawning.cs:185-189`).
- Ghost roles: `GhostRoleSystem` (`Content.Server/Ghost/Roles/GhostRoleSystem.cs:42`), raffles and
  takeover; reset on `RoundRestartCleanupEvent`.
- Respawn: `_Corvax/Respawn/RespawnSystem.cs:123`, `RespawnRuleSystem` for respawn rules.

## Entry points / important symbols

| Symbol | Path | Purpose |
|---|---|---|
| `GameTicker` | `Content.Server/GameTicking/GameTicker.cs:34` | Round flow + managers |
| `SharedGameTicker` | `Content.Shared/GameTicking/SharedGameTicker.cs:16` | Ticker events, replay hook |
| `ClientGameTicker` | `Content.Client/GameTicking/Managers/ClientGameTicker.cs:18` | Client mirror, state switches |
| `RoundEndSystem` | `Content.Server/RoundEnd/RoundEndSystem.cs` | Round-end countdown/shuttle behavior |
| `GameMapManager` | `Content.Server/Maps/GameMapManager.cs` | Map selection/rotation |
| `SpawnPlayer` / `SpawnPlayers` | `Content.Server/GameTicking/GameTicker.Spawning.cs` | Player spawn pipeline |
| `GameRuleSystem<T>` | `Content.Server/GameTicking/Rules/GameRuleSystem.cs:10` | Rule lifecycle base |
| `GameRuleComponent` | `Content.Shared/GameTicking/Components/GameRuleComponent.cs` | Rule state + events |
| `StationJobsSystem` / `StationSpawningSystem` | `Content.Server/Station/Systems/` | Job slots and mob spawning |
| `NFInitialize` hook | `Content.Server/_NF/GameTicking/GameTicker.NFSpawning.cs:10` | NF extension of GameTicker |

## Data owned

- `RunLevel`, `RoundId`, `RoundStartTime`, `DefaultMap`, `ShiftEndTime`, `_randomizeCharacters`;
  active `GameRule` entities; lobby state; replay recording state; selected map/preset.
- DB writes: `AddNewRound` (round row), `AddRoundPlayers` (`player_round`), last-seen updates.

## Events produced

`RoundStartingEvent`, `RoundStartAttemptEvent`, `GameRunLevelChangedEvent`, `RulePlayerSpawningEvent`,
`RulePlayerJobsAssignedEvent`, `PlayerBeforeSpawnEvent`, `PlayerSpawnCompleteEvent`,
`RoundEndTextAppendEvent`, `RoundRestartCleanupEvent`, `RoundStartedEvent` (NF, server-only),
`GameRuleAdded/Started/EndedEvent`, `Ticker*` network events, `RoundEndMessageEvent`.

## Events consumed

`PlayerStatusChanged` (engine player manager), `RoundRestartCleanupEvent` (cleanup), CVar changes,
`ServerApi` admin actions, `GameRuleSystem` rule start/end events, `StationsGeneratedEvent` (NF).

## Dependencies

Station (jobs/spawning), Mind, Roles, Ghost system, Preferences/DB (`UserDbDataManager`),
MapLoaderSystem, engine PlayerManager/PVS, replay manager, chat manager (announcements),
`ServerUpdateManager` (restarts). NF extensions via partial class and `_NF` rules.

## Depended on by

Nearly everything round-scoped: chat, station events, alert levels, shuttle/emergency systems,
objectives, status shell, client lobby/gameplay states, integration test pool.

## Notes / caveats

- `GameRunLevelChangedEvent` fires even when the level does not change (`RoundFlow.cs:68-69`).
- Presets are selected by `game.defaultpreset`; fallback list is `Traitor,Extended` which have no rules.
- `game.destination_file` mode bypasses normal runtime and shuts down after dumping data.
- Round-scoped managers must reset state on `RoundRestartCleanupEvent`.
