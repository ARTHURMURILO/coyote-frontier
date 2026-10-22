# EVENTS / HOOKS / LIFECYCLE

> All facts from source. Engine files under `RobustToolbox/`. Subscriptions are explicit calls
> (there is no `[EventHandler]` attribute in this engine revision).

## 1. Event dispatch model (engine)

```
RaiseLocalEvent<T>(msg)                    → broadcast-only (system subscriptions)
RaiseLocalEvent(uid, args)                 → directed (component subscriptions only)
RaiseLocalEvent(uid, args, broadcast:true) → component handlers then broadcast handlers
QueueLocalEvent(msg)                       → deferred FIFO; processed at end of EntityManager.TickUpdate
RaiseNetworkEvent(msg[, session|filter|uid]) → [engine] net send (server↔client), optional replay record
RaiseComponentEvent(uid, comp, args)       → single component handler only
```

Subscription kinds:

| Call | Receives |
|---|---|
| `SubscribeLocalEvent<TComp,TEvent>` | directed component event on an entity |
| `SubscribeLocalEvent<TEvent>` | local broadcast event |
| `SubscribeNetworkEvent<TEvent>` | events received from the network |
| `SubscribeAllEvent<TEvent>` | local + network |
| `[ByRefEvent]` | must be subscribed by ref; analyzer RA0013/15/16 enforces |
| `[ComponentEvent]` | only raised through component lifecycle (see below) |

Ordering: `SubscribeLocalEvent<T>(..., before: [...], after: [...])` participates in a topological sort
computed once per event type; all subscriptions from one system to the same event must use identical
ordering data or it throws. Subscriptions are **locked** after all systems initialize
(`EntitySystemManager.Initialize` → `EventBus.LockSubscriptions`); subscribe only in `Initialize()`.

Dispatch rules / traps (engine `EntityEventBus`):

- Directed raise does not reach broadcast handlers unless `broadcast:true`.
- Broadcast raise does not reach component handlers — at all. `RaiseLocalEvent<T>(msg)` will not fire
  `SubscribeLocalEvent<TComp,TEvent>` handlers.
- Duplicate broadcast subscription per system+event throws; component duplicates throw.
- Deferred (`QueueLocalEvent`) events are processed later in the same tick, not next tick.

## 2. Component / entity lifecycle

Definitions in `RobustToolbox/Robust.Shared/GameObjects/Component.cs:186-215`, raised in
`EntityManager.LifeCycle.cs`:

| Event | When | Delivery |
|---|---|---|
| `ComponentAdd` | component attached to entity | component-only |
| `ComponentInit` | after all components added; not started | component-only |
| `ComponentStartup` | entity initialized and component starts | component-only |
| `ComponentShutdown` | component stopping | component-only |
| `ComponentRemove` | component detached | component-only |
| `MapInitEvent` | entity reaches map-init (`EntityManager.RunMapInit`) | directed, broadcast=false |
| `EntityTerminatingEvent` | before recursive deletion (children first) | directed + broadcast, `[ByRefEvent]` |
| `EntityPausedEvent` / `EntityUnpausedEvent(PausedTime)` | map/entity pause/unpause | directed |
| `EntInsertedIntoContainerMessage` / `EntGotInsertedIntoContainerMessage` | container insert | directed at container + broadcast |
| `EntRemovedFromContainerMessage` / `EntGotRemovedFromContainerMessage` | container remove | directed at container + broadcast |
| `PrototypesReloadedEventArgs` | prototype hot reload (`reload prototypes`) | local event + C# event |

Entity added order: `ComponentAdd → ComponentInit → ComponentStartup → MapInitEvent`.
Deletion order: `EntityTerminatingEvent` (children first) → `ComponentShutdown` → `ComponentRemove`
→ engine C# `EntityManager.EntityDeleted` (not a bus event).
There is **no** `MetaFlagChangedEvent` and **no** `EntityInitializedEvent`/`EntityStartedEvent` bus event.
`MetaDataSystem.EntityPaused` is the supported pause query.

## 3. System lifecycle

- `EntitySystem.Initialize()` — subscribe here; `UpdatesBefore/After` declared here.
- `EntitySystem.PostInject()` — called after DI resolution; log available.
- `Update(float)` every tick (topological order; skipped when `noPredictions && !UpdatesOutsidePrediction`).
- `FrameUpdate(float)` every render frame (client).
- `Shutdown()` — engine auto-unsubscribes.
- `Subs` helper auto-unregisters CVar/C# event handlers on shutdown (`Subs.CVar(...)`).
- Timers: `Timer.Spawn/SpawnRepeating/Delay` run on the main thread (`BaseServer.Update` calls
  `TimerManager.UpdateTimers`), while `ITimerManager` is also available. ECS `TimerComponent` +
  `[AutoGenerateComponentPause]`/`[AutoPausedField]` handle pause-aware timers.

## 4. Round / game lifecycle events (content)

| Event | Defined at | Raised by | When | Args | Notable consumers |
|---|---|---|---|---|---|
| `RoundStartingEvent` | `Content.Server/GameTicking/Events/RoundStartingEvent.cs:6` | `GameTicker.RoundFlow.cs:418` | `StartRound`, before preset attempt | `int Id` | admin logs, ambient music, clock |
| `RoundStartAttemptEvent` | `GameTicker.RoundFlow.cs:932` | `GameTicker.GamePreset.cs:33` | preset validation, cancellable | `Players, Forced` | `GameRuleSystem` min players |
| `GameRunLevelChangedEvent` | `GameTicker.RoundFlow.cs:847` (plain class) | `RunLevel` setter `RoundFlow.cs:74` | run level changes (fires even if unchanged) | `Old, New` | bwoink, station, sandbox |
| `RulePlayerSpawningEvent` | `RoundFlow.cs:949` | `GameTicker.Spawning.cs:74` | before jobs/spawn | `PlayerPool, Profiles, Forced` | `AntagSelectionSystem` |
| `RulePlayerJobsAssignedEvent` | `RoundFlow.cs:972` | `GameTicker.Spawning.cs:135` | after assignment/spawn | `Players[], Profiles, Forced` | `AntagSelectionSystem`, ID cards |
| `PlayerBeforeSpawnEvent` | `Content.Shared/GameTicking/PlayerBeforeSpawnEvent.cs:13` | `Spawning.cs:222` | broadcast before own spawn; `Handled` skips ticker | `Player, Profile, JobId, LateJoin, Station` | `DeathMatchRuleSystem` |
| `PlayerSpawnCompleteEvent` | `Content.Shared/GameTicking/PlayerSpawnCompleteEvent.cs:13` | `Spawning.cs:369` | after spawn (mob-directed + broadcast) | `Mob, Player, JobId, LateJoin, Silent, JoinOrder, Station, Profile` | rules, `_NF/InterviewHologramSystem` |
| `RoundStartedEvent` | `Content.Server/_NF/RoundNotifications/Events/RoundStartedEvent.cs:4` | `RoundFlow.cs:444` | after spawn, **server-only fork addition** | `int RoundId` | `_NF` round notifications |
| `RoundEndTextAppendEvent` | `RoundFlow.cs:989` | `RoundFlow.cs:520` | building end screen | `AddLine(string)` | rules, objectives, `_DV` pacified |
| `RoundRestartCleanupEvent` | `Content.Shared/GameTicking/RoundRestartCleanupEvent.cs:7` | `RoundFlow.cs:728` | reset cleanup; local + network | — | wires, explosions, mind, audio, client sounds |
| `GameRuleAddedEvent` / `GameRuleStartedEvent` / `GameRuleEndedEvent` | `Content.Shared/GameTicking/Components/GameRuleComponent.cs:45,52,59` | `GameTicker.GameRule.cs:86,170,197` | rule entity spawn/start/end | `RuleEntity, RuleId` | `GameRuleSystem`, power console |
| `StationsGeneratedEvent` (NF) | `Content.Server/_NF/GameTicking/SectorGeneratedEvent.cs:6` | `NfAdventureRuleSystem.cs:251` | sector generated | — | public transit, dead drops |

Important: **there is no `RoundEndedEvent`.** Use `RoundEndMessageEvent` (network) and
`GameRunLevelChangedEvent`.

## 5. Networked ticker events (server → client)

Defined in `Content.Shared/GameTicking/SharedGameTicker.cs:61-219`; raised with `RaiseNetworkEvent` from
`GameTicker.Lobby.cs`/`Player.cs`/`RoundFlow.cs`; consumed by `Content.Client/GameTicking/Managers/ClientGameTicker.cs`.

`TickerJoinLobbyEvent`, `TickerJoinGameEvent`, `TickerLateJoinStatusEvent`, `TickerConnectionStatusEvent`,
`TickerLobbyStatusEvent`, `TickerLobbyInfoEvent`, `TickerLobbyCountdownEvent`, `TickerJobsAvailableEvent`,
`RoundEndMessageEvent`.

## 6. Important content network messages

All `NetMessage` subclasses live in `Content.Shared` (26 total). Most important:

| Message | Direction | Purpose |
|---|---|---|
| `MsgChatMessage` / `MsgDeleteChatMessagesBy` | S→C | Chat delivery/deletion |
| `MsgPreferencesAndSettings` | S→C | Profile + GameSettings on load |
| `MsgSelectCharacter` / `MsgUpdateCharacter` / `MsgDeleteCharacter` / `MsgUpdateConstructionFavorites` | C→S | Preferences edits |
| `MsgUpdateConsent` | C↔S | Consent sync both ways |
| `MsgPlayTime`, `MsgRoleBans`, `MsgJobWhitelist`, `MsgWhitelist` | S→C | Player restrictions |
| `MsgUpdateAdminStatus` | S→C | Admin flags/title/commands |
| `MsgVoteData`, `MsgVoteCanCall`, `MsgVoteMenu` | S↔C | Voting |
| `SendRulesInformationMessage`, `RulesAcceptedMessage` | S→C / C→S | Rules handshake |
| `BwoinkTextMessage` + relay/typing variants | C↔S | AHelp |
| `MsgEuiCtl` / `MsgEuiState` / `MsgEuiMessage` | S↔C | EUI (admin/global UIs) |
| `MsgGhostKick` | S→C | Fake network loss kick |
| `MappingSaveMapMessage` / `MappingMapDataMessage` / `MappingSaveMapErrorMessage` | C→S / S→C | Map editing |
| `CustomObjectiveClientSetObjective` (`_DV`) | C→S | Custom objective text |

Network events (`[Serializable, NetSerializable] EntityEventArgs`, via `RaiseNetworkEvent`):
`RequestCharacterInfoEvent`/`CharacterInfoEvent`, construction messages
(`TryStartStructureConstructionMessage`, `TryStartItemConstructionMessage`,
`AckStructureConstructionMessage`, guide request/response), shuttle messages
(`EmergencyShuttlePositionMessage`, dock/undock/FTL/IFF console messages).

## 7. IoC / manager lifecycle

- `[Dependency]` injection; `IoCManager.BuildGraph()`; `IPostInjectInit.PostInject` runs after injection.
- Entity systems get a child dependency collection; systems are created once and not per round.
- Content EntryPoints explicitly initialize managers in `Init`/`PostInit` and update them in
  `Update(ModUpdateLevel, ...)` (server: `PostEngine`/`FramePostEngine`; client: `FramePreEngine`/`PreEngine`).

## 8. CVars / timers hooks

- `IConfigurationManager.OnValueChanged<T>(cvar, handler)` or system helper `Subs.CVar(...)`
  (auto-unsubscribed; `invokeImmediately` option).
- `Timer.Spawn/SpawnRepeating` on the main thread; `GameTiming.CurTime` is simulation time,
  pause-aware. Paused maps: check `MetaDataSystem.EntityPaused`/`SharedMapSystem.IsPaused`.

## 9. Prototype reload

- `PrototypesReloadedEventArgs` raised on `PrototypeManager.Reload` (local bus + C# event
  `IPrototypeManager.PrototypesReloaded`).
- Engine `PrototypeReloadSystem` patches live entities when their `EntityPrototype` changed.
- Content handlers subscribe `SubscribeLocalEvent<PrototypesReloadedEventArgs>` or the C# event
  (audio, lathe, alerts, markings, jukebox, ...).

## 10. Lifecycle order summary

```
ENGINE STARTUP
  ModLoader discovers GameShared/GameServer/GameClient
  → PreInit (shared IoC) → Init (content IoC, prototypes, entity manager)
  → PostInit (content managers, GameTicker.PostInitialize → RestartRound)
  → main loop
TICK (server)
  input/tasks → PreEngine → CVars → console+timers → async → EntityManager.TickUpdate
  (systems Update in topo order → queued events → deferred deletions) → PostEngine → send state
FRAME (client)
  FramePreEngine → render → PreEngine (BUI tick) → interpolation/prediction
SHUTDOWN
  EntryPoint.Dispose → engine Cleanup → EntitySystemManager.Shutdown (each system) → EventBus cleared
```
