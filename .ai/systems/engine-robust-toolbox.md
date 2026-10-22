# System: RobustToolbox Engine

> `RobustToolbox/` is a git submodule (v267.3.0, commit `feb9e1db6`). It is **not to be modified**.
> This document records the engine behavior that content relies on.

## Purpose

The game engine: process bootstrap, ECS, prototypes, serialization, networking/PVS/prediction,
maps/grids/physics, UI framework, audio, timers, console, replays, and sandboxing.

## Bootstrap

```
Server: Robust.Server/Program.cs → BaseServer.Start (BaseServer.cs:166)
  config (CVars/env/CLI/toml :195-205) → network (:280) → VFS (:300) → ModLoader (:316)
  → BroadcastRunLevel(PreInit/Init/PostInit) → EntityManager/Prototypes/Serialization init
  → MainLoop (:562) → GameLoop.Run: Input → Tick → Frame
Client: Robust.Client/GameController/GameController.Standalone.cs → GameController.Start
  → IoC, VFS, Clyde → LoadModules → BroadcastRunLevel(PreInit/Init/PostInit)
  → GameLoop.Run (Tick/Render/Input/Update)
```

- `IModLoader` (`ContentPack/ModLoader.cs:21`) discovers assemblies (`Content.` prefix), topologically
  sorts by references, verifies if sandboxed, and finds `GameShared` subclasses
  (`ContentPack/BaseModLoader.cs:34-40`) to fan out lifecycle calls.
- Entry points are content classes: `GameServer`, `GameClient`, `GameShared`
  (`Robust.Shared/ContentPack/GameShared.cs:11`).

## ECS core

| Concept | Details | Paths |
|---|---|---|
| Entity | `EntityUid` local int; `NetEntity` network id (client entities have high bit set); `AllocEntity`, `GenerateNetEntity` | `GameObjects/EntityUid.cs`, `NetEntity.cs`, `EntityManager.cs:889,1101`, `EntityManager.Network.cs:18-181` |
| Component | Data class; `[RegisterComponent]`, `[ComponentProtoName]`, `[NetworkedComponent]`, `[DataField]`; `ComponentLifeStage`; `SendOnlyToOwner`, `SessionSpecific` | `GameObjects/Component.cs`, `ComponentAttributes.cs`, `GameStates/NetworkedComponentAttribute.cs` |
| Component factory | Reflection auto-registration; net IDs by ordinal name sort (`GenerateNetIds`); `IgnoredComponents`; `DoAutoRegistrations` | `ComponentFactory.cs:87,444,525` |
| System | `EntitySystem` with `Initialize/Update/FrameUpdate/Shutdown`, `UpdatesBefore/After`, `UpdatesOutsidePrediction`; discovered by reflection, topo-sorted | `GameObjects/EntitySystem.cs:24`, `EntitySystemManager.cs:132-264` |
| Queries | `EntityQueryEnumerator<T>` skips paused; `AllEntityQueryEnumerator<T>` includes paused | `EntityManager.Components.cs:2115,2424` |
| Event bus | Directed vs broadcast; queued events; subscription lock; `[ByRefEvent]`, `[ComponentEvent]` | `GameObjects/EntityEventBus.*.cs` |
| Spawning/deletion | `Spawn*`, `QueueDel`, `PredictedQueueDel`, terminating events | `EntityManager.Spawn.cs`, `EntityManager.cs:516-717` |

## Prototypes

- `IPrototype` (`ID`), `IInheritingPrototype` (`Parents`, `Abstract`); `[Prototype("kind")]` maps YAML
  `type:` to a C# class; `[IdDataField]`, `[ParentDataField]`, `[AbstractDataField]`.
- `PrototypeManager`: `LoadDirectory("/EnginePrototypes")` then `/Prototypes`;
  `ValidateFields` + static-field validation (`[ValidatePrototypeId<T>]`); inheritance merge; abstract
  file/dir support; hot reload only in TOOLS.
- C# references use `ProtoId<T>` / `EntProtoId` (serializer-validated); **no generated ID constants**.
- Prototype IDs are global; kind registry built via `ReloadPrototypeKinds`.

## Serialization

| Layer | Mechanism | Paths |
|---|---|---|
| Data definitions | `[DataField]` (+ tag/readOnly/priority/required/serverOnly/customTypeSerializer), `ISerializationManager.Read/Write/CopyTo/CreateCopy`, `ISelfSerialize`, custom type readers/writers | `Serialization/Manager/...` |
| Generated helpers | `ISerializationGenerated<T>` (Copy/Instantiate) from `Robust.Serialization.Generator`; component pause generation from `[AutoGenerateComponentPause]`/`[AutoPausedField]` | `Serialization/Generator/*` |
| Binary/network | vendored NetSerializer wrapped by `RobustSerializer`; `[Serializable, NetSerializable]`; string dedup via `RobustMappedStringSerializer`; handshake hashes | `Serialization/RobustSerializer*.cs` |
| Component state | `ComponentGetState`/`ComponentHandleState`, generated `<Component>_AutoState` + deltas (`IComponentDelta`), `[AutoNetworkedField]`, `AfterAutoHandleStateEvent` | `GameStates/ComponentStateEvents.cs`, `Robust.Shared.CompNetworkGenerator/ComponentNetworkGenerator.cs` |
| Maps | YAML map **format 7**; `MapLoaderSystem` load/save, chunk serializer | `EntitySerialization/Systems/MapLoaderSystem*.cs`, `EntitySerializer.cs:44` |

## Networking / sessions / replication

- Transport: Lidgren; `NetManager` partials; `NetMessage` with `MsgGroups` (Core, Entity, String,
  Command, EntityEvent) and default delivery methods; `RegisterNetMessage<T>`.
- Sessions: `ICommonSession`, `ISharedPlayerManager` (`Sessions`, `PlayerCount`, `SetAttachedEntity`);
  server `PlayerManager` (`Robust.Server/Player/PlayerManager.cs:26`).
- Game state: `ServerGameStateManager` sends `MsgState`; `PvsSystem` computes visibility with budgets
  (`NetPVS`, `NetPVSEntityBudget/Enter/Exit`, ranges, async); client `ClientGameStateManager` buffers,
  applies, predicts.
- `Filter` (`Shared/Player/Filter.cs`) for broadcast/PVS/session targeting; `SharedPvsOverrideSystem`.
- CVar replication (`NetConfigurationManager`, `MsgConVars`) and `CVar.REPLICATED`.

## IoC / DI

- `IoCManager` per-thread; `IDependencyCollection`; `[Dependency]` field injection; `IPostInjectInit`.
- Entity systems resolve from a child collection (`EntitySystemManager.SystemDependencyCollection`).
- `IDynamicTypeFactory` for safe runtime instantiation (sandbox).

## Resources / VFS

- `IResourceManager` mounts `Resources/` (dev: engine + content), exposes rooted paths
  (`/Prototypes/...`, `/Textures/...`), `UserData` for writable data.
- `IModLoaderInternal` loads assemblies from `/Assemblies/`.
- TOOLS-only hot reload (`IReloadManager`, XAML hot reload).

## CVars

- `CVarDef.Create` + `[CVarDefs]` classes; flags `CHEAT/SERVER/NOT_CONNECTED/REPLICATED/ARCHIVE/NOTIFY/SERVERONLY/CLIENTONLY/CONFIDENTIAL/CLIENT`.
- Sources: code defaults → `CVarDefaultOverrides` → env → CLI → config TOML; networked sync to clients.
- Server checks unused CVars at startup.

## Maps / grids / transforms / physics

- `IMapManager`, `SharedMapSystem` (maps/tiles/pause/init), `SharedTransformSystem`
  (parent/coordinates/anchoring), `EntityCoordinates`/`MapCoordinates`/`NetCoordinates`.
- Physics: `SharedPhysicsSystem` partials (fixtures/contacts/solver/islands/queries), `SharedBroadphaseSystem`,
  `FixtureSystem`, `RayCastSystem`, `CollisionManager`; server physics system.
- Map pause: `MapComponent.MapPaused`, `SharedMapSystem.IsPaused`, `EntityPaused/UnpausedEvent`,
  `MetaDataSystem.PauseOffset`.

## Timing / threading

- `GameLoop` single simulation loop: ticks (fixed period from `net.tickrate`) + per-frame update/render;
  `GameTiming.CurTime`/`CurTick`/`Paused`; `TimerManager`; ECS `TimerComponent`.
- Async: `TaskManager` marshals continuations to the main thread; parallel jobs via `ParallelManager`.
- Client has an optional separate window thread; game logic runs on the game thread.

## UI / input / audio (engine-side)

- Input: `InputCmdMessage` (state/event/pointer), `InputManager` (client), bound key maps/contexts,
  command replay/prediction.
- UI: `IUserInterfaceManager`, `UIController` discovery, `Control` tree, XAML loader, stylesheets,
  `UserInterfaceComponent`/`BoundUserInterface`, `SharedUserInterfaceSystem`.
- Audio: `SharedAudioSystem` (`PlayGlobal/PlayEntity/PlayPvs/PlayPredicted`), OpenAL backend, MIDI.

## Console / permissions

- `IConsoleCommand` auto-discovered; `IEntityConsoleCommand`; Toolshed `[ToolshedCommand]` +
  `[CommandImplementation]`.
- Admin permission hook `IConGroupControllerImplementation` is implemented by content
  (`AdminManager`); engine has no permission YAML.

## Sandbox

- Client sandbox default on; `AssemblyTypeChecker` parses embedded `Sandbox.yml` whitelists
  (`Robust`, `Content`, `OpenDreamShared` namespaces etc.) and can IL-verify.
- `[SkipIfSandboxed]`, `ISandboxHelper`.

## Replays

- `IReplayRecordingManager` records game states + server→client messages; server/client impls;
  playback via `IReplayLoadManager`/`IReplayPlaybackManager`, replay data model and checkpoints.

## Analyzers / source generators (summary)

- `Robust.Analyzers`: RA0001 `[Serializable]` on `[NetSerializable]`; RA0002 `[Access]`; RA0004 no `.Result`;
  RA0013+ by-ref events; RA0025 no dependency assignment; RA0026 cached regex; RA0031 prefer expected type;
  RA0039 no `new` prototypes; RA0041 auto-handle-state; etc.
- Generators: `Robust.Shared.CompNetworkGenerator` (component auto states), `Robust.Serialization.Generator`
  (data-definition copy/instantiate + component pause), `Robust.Client.NameGenerator` (XAML names).
- `Robust.Generators` project is an empty placeholder in this revision.

## Content-facing gotchas

See `.ai/HAZARDS.md` §4 for the list of engine APIs that differ from common upstream docs
(no `[EventHandler]`, no `[DependsOn]`, no `GetPVSInterest`, no `PrototypeReloadEvent`, map format 7, etc.).
