# Idea: Lua Scripting (Components, Hot Reload, In-Game Robot Programming)

> **Status: research / feasibility — nothing here is implemented.**
> In-repo facts are cited with paths and line numbers (valid for branch `palm3`, commit `4bfdad813c`,
> engine `feb9e1db6`). External facts (MoonSharp, alternatives) are marked **[external]** and were
> checked against public sources in October 2026. Anything estimated (effort, performance) is marked
> **[inference]**.

## 1. Executive summary

| Goal | Verdict | Key reason |
|---|---|---|
| Define brand-new **component types** in Lua (new CLR classes at runtime) | ❌ **Not viable content-only** | Component registration, net IDs, event subscriptions, and serialization definitions are all locked after startup; client would need the same types |
| Lua-driven **behavior + data** on one generic compiled component | ✅ **Feasible** | One `LuaComponent` + one `LuaSystem`; scripts are data, not types |
| **Hot reload on file change** (Garry's Mod style) | ✅ **Feasible server-side** | The engine has a client prototype watcher to copy; a server-side watcher can live in a content system |
| Lua for **faster development** (dev-only, trusted) | ✅ **Feasible**; overlaps existing C# `scsi` REPL | Full-trust server scripting already exists; Lua adds hot reload + safer syntax |
| **Player-authored Lua** (robot programming) | ⚠️ **Feasible with heavy sandboxing/budgeting** | Server-authoritative only; no prediction; persistence and CPU limits need design |
| Lua on the **client** (predicted scripts, client UI scripting) | ❌ **Blocked without engine changes** | Client sandbox (`Sandbox.yml`) rejects content assemblies that statically reference MoonSharp types |

**Bottom line:** a content-only, server-side Lua layer is realistic and can deliver hot-reloadable
gameplay scripting plus in-game robot programming. "New components defined in Lua" is only achievable
as a *pattern* (one generic component interpreted by Lua), not as runtime CLR types. True runtime
component types would require engine changes and still fight the client/server code-sync model — not
recommended, and conflicts with the maintainer constraint that the engine is never modified.

## 2. What already exists in this repo

### 2.1 Engine C# scripting (Roslyn) — the current arbitrary-code path
- `RobustToolbox/Robust.Shared.Scripting/` (assembly `Robust.Shared.Scripting`): Roslyn C# scripting core.
  `ScriptInstanceShared.cs` compiles `SourceCodeKind.Script` with references to **all** loaded
  assemblies; `ScriptGlobalsShared.cs` exposes IoC (`res<T>()`), raw reflection (`prop/setprop/fld/call`),
  entity proxies (`Comp/TryComp/Spawn/Del/Query`), and Toolshed invocation (`tsh(...)`).
- Server: `RobustToolbox/Robust.Server/Scripting/ScriptHost.cs` — per-session REPL, network messages,
  permission gate `CanScript`; initialized in `BaseServer.cs:395`. **No sandbox, no timeout, no
  cancellation.**
- Client: `Robust.Client/Console/ScriptConsoleClient.cs` local REPL is `#if CLIENT_SCRIPTING` (Tools
  builds only, `Robust.Properties.targets:19-22`); the server REPL client (`ScriptClient.cs`) always ships.
- Permissions: `CanScript` → `AdminFlags.Host` (`Content.Server/Administration/Managers/AdminManager.cs:646-649`,
  `Content.Shared/Administration/AdminData.cs:52-55`). Commands `scsi`/`csi` are HOST-gated in
  `Resources/engineCommandPerms.yml:119-134`.
- Takeaway: **the project already accepts full-trust scripting as an admin-only feature.** A Lua layer
  would be a safer, reloadable alternative for the same niche plus player content.

### 2.2 Toolshed — the existing in-game command language
- `RobustToolbox/Robust.Shared/Toolshed/` — a pipeline/stack language over the console, discovered via
  `[ToolshedCommand]`, server-side in normal builds (`ToolshedManager.Permissions.cs:34-45` throws on client).
- It invokes fixed C# methods only: no user-defined functions, no dynamic compilation. Reachable in-game
  by admins through the client console (`ServerConsoleHost.cs:126-144`).
- Takeaway: Toolshed proves "in-game programmable console" is viable, but it is not a scripting language.

### 2.3 Hot reload infrastructure (all client-side today)
| Mechanism | Path | Build gate |
|---|---|---|
| Prototype watcher (client, on window focus) | `RobustToolbox/Robust.Client/Utility/ReloadManager.cs` | `#if TOOLS` + CVar `res.prototype_reload_watch` |
| Prototype reload on server | `ServerPrototypeManager.cs:37-52` (`MsgReloadPrototypes`) | body `#if TOOLS` + admin permission |
| Runtime prototype load (production-safe) | `Robust.Shared/Upload/SharedPrototypeLoadManager.cs:43-46` + `loadprototype` command | none |
| XAML hot reload | `Robust.Client/UserInterface/XAML/Proxy/XamlHotReloadManager.cs` | `#if TOOLS` |
| Localization/shader/tile reload | `rldloc`, `rldshader`, `rldrsc`, `reloadtiletextures` | mostly ungated |
| `IReloadManager` (generic watcher interface) | `Robust.Shared/Utility/IReloadManager.cs` | implementation registered **client-only** (`ClientIoC.cs:108`) |

- No server-side file watcher exists. A content `EntitySystem` can create its own `FileSystemWatcher`
  (pattern to copy: `ReloadManager.cs:91-104`), debounce with `Timer.Spawn` (`:42-55`), and marshal to the
  simulation thread with `ITaskManager.RunOnMainThread` (`TaskManager.cs:31-34`).
- Assembly hot swap is **not** supported: the ModLoader `AssemblyLoadContext` is non-collectible
  (`ModLoader.cs:28,49`), reflection caches are append-only, and net IDs/types are frozen at startup.
  **Lua is attractive precisely because reload happens inside the interpreter, not at the CLR level.**

### 2.4 Sandbox
- Client sandbox **on** by default (`GameControllerOptions.cs:11`); server sandbox **off**
  (`ServerOptions.cs:11`). `AssemblyTypeChecker` + `ContentPack/Sandbox.yml` whitelist namespaces
  `Robust`, `Content`, `OpenDreamShared` and enumerate BCL/third-party types (Nett, ImageSharp).
- A `Content.Client`/`Content.Shared` assembly that statically references `MoonSharp.*` types fails the
  type check (and the CI `SandboxTest`) unless the types are added to `Sandbox.yml` or the library is
  shipped as an engine module. **Server-only usage avoids this entirely.**
- The sandbox is compile-time only; it cannot constrain what an interpreter does at runtime — the Lua
  binding layer must be the security boundary.

### 2.5 Robotics / automation today (integration surface for later)
- Borg assembly: exosuit fabricator parts → `PartAssemblyComponent` + construction graph
  (`Resources/Prototypes/Recipes/Construction/Graphs/machines/cyborg.yml`) → `BorgChassisSelectable`;
  systems in `Content.Server/Silicons/Borgs/` (brain/module/UI/transponder).
- Mechs: `MechAssemblySystem.cs:33-66` + mech construction graphs; `Content.Server/Mech/`, `_NF/Mech/`.
- No Roboticist job prototype exists (only an icon + loc string); robotics is done by Science roles.
- I/O available to scripts: device linking signals (`DeviceLinkSystem.InvokePort`), logic gates/timers
  (`Resources/Prototypes/Entities/Structures/gates.yml`), device network packets
  (`Content.Server/DeviceNetwork/Systems/DeviceNetworkSystem.cs`), triggers, power.
- Autonomous control surfaces: HTN + blackboard (`NPCSystem.Blackboard.cs:7-16` `SetBlackboard`),
  steering registration (`NPCSteeringSystem.cs:253-291`), `HTNComponent.Enabled`.
- Player-authored data precedents: `PaperComponent` (string DataField + BUI editor), `_Floof`
  CustomExamine text, cartridge loader programs (`NotekeeperCartridgeComponent`), silicon law editor.
- Persistence caveat: borg chassis and mechs are `save: false` (`base_borg_chassis.yml:6`, `mechs.yml:3`),
  so scripts attached to them do not survive map save/load; there is no per-entity DB persistence.

## 3. Goal A — "Define new components through Lua"

### 3.1 Why true runtime component types are blocked (content-only)
| Lock | Detail | Source |
|---|---|---|
| Component registration lock | `ComponentFactory.Register` throws `ComponentRegistrationLockException` once `_networkedComponents` exists | `ComponentFactory.cs:96-97` |
| Net ID assignment | `GenerateNetIds()` runs once at content `Init` (`Content.Server/Entry/EntryPoint.cs:89`), sorting `[NetworkedComponent]` names ordinally; no incremental API | `ComponentFactory.cs:525-554` |
| Subscription lock | Event subscriptions are locked at `EntityManager.Startup()`; late `Subscribe*` throws | `EntityManager.cs:223-233`, `EntityEventBus.Directed.cs:416-423` |
| Serialization registry | `SerializationManager.Initialize()` is one-shot and `GetDefinition` never lazily creates; runtime types have no `DataDefinition` | `SerializationManager.cs:39-45,302-312`; `Reading.cs:505-516` |
| Network type map | `RobustSerializer.Initialize` freezes the `[NetSerializable]` type map at startup | `RobustSerializer.cs:59-98` |
| Source generators | `[AutoGenerateComponentState]`, `[AutoGenerateComponentPause]`, `ISerializationGenerated<T>` are compile-time only | `Robust.Shared.CompNetworkGenerator`, `Robust.Serialization.Generator` |
| Reflection cache | `ReflectionManager` only sees assemblies loaded through the (internal) mod loader | `ReflectionManager.cs:75-113`; `IModLoaderInternal` |
| Client sync | Even with engine patches, the client must run identical component types and net IDs; pure Lua can only avoid this by *not* creating CLR types | — |

Runtime **prototypes** for existing kinds are supported (`PrototypeManager.LoadString` +
`ReloadPrototypes`, used by `SharedPrototypeLoadManager.cs:43-46` and the `loadprototype` command), and
`PrototypeReloadSystem` adds/removes components on live entities. Runtime **prototype kinds** are
mechanically registrable (`PrototypeManager.RegisterKind`) but serialization-blocked for runtime types.

### 3.2 The feasible pattern: one generic component, behavior in data
```
[LuaComponent]  (compiled once, C#)
  DataField: ProtoId<LuaScriptPrototype> Script / or inline source string
  DataField: LuaValue[] Args            (primitives only)
  manual ComponentGetState/HandleState if it must replicate (source can be big — prefer prototype ref)

[LuaScriptPrototype] (compiled C# prototype kind, YAML-defined)
  DataField: string Source              (inline or a path under Resources/Lua/)

[LuaSystem] (compiled once, C#)
  - loads script prototypes into MoonSharp Script instances (cached by prototype id)
  - per-entity script state (Lua table) serialized as primitives if persistence is needed
  - subscribes a fixed set of events at Initialize (e.g. ComponentStartup/Shutdown, a tick event)
  - dispatches to scripts from a table keyed by script id (no late event subscriptions)
```
This gives "Lua-defined components" in the practical sense: prototypes compose `LuaComponent` with
script id + parameters, and scripts implement per-entity behavior. It works with map saving, prototype
reloading, and the existing networking model.

### 3.3 If true dynamic components were ever required
Required engine work (all out of bounds per the maintainer constraint): re-openable component factory +
incremental net ID negotiation/handshake; re-openable event subscriptions; runtime `DataDefinition`
registration; serializer type-map extension; a way for the client to receive and run the same Lua;
and deterministic save/load of dynamic components. Even then, dynamic components are a cross-side ABI
problem, not a scripting convenience. **Recommendation: do not pursue; use 3.2.**

## 4. Goal B — Hot reload (Garry's Mod style)

### 4.1 Feasibility
Feasible for **server-side** Lua. There is no server watcher today, but all the pieces exist:
`FileSystemWatcher` (as in `ReloadManager.cs:91-104`), debouncing via `Timer.Spawn`, main-thread
marshalling via `ITaskManager.RunOnMainThread`, and `IResourceManager.UserData` for a writable script
directory. The client cannot run player Lua (sandbox), so reloaded behavior is server-authoritative;
clients see only replicated state.

### 4.2 Proposed design
1. **Location**: a `Content.Server` `EntitySystem` (e.g. `Content.Server/_PS/Scripting/LuaScriptSystem.cs`)
   registered through normal system discovery — no engine changes, no IoC edits.
2. **Script dirs** (in priority order): `Resources/Lua/**` (shipped, read-only at runtime) and
   `<UserData>/Lua/**` (server-writable; `IResourceManager.UserData.RootDir`, may be null in tests —
   handle it). Add a `SERVERONLY` CVar for an absolute override dir.
3. **Watcher**: `FileSystemWatcher` on `*.lua` (`IncludeSubdirectories=true`, `LastWrite|FileName`),
   enqueue changed paths into a `ConcurrentDictionary`, debounce 100-500 ms with `Timer.Spawn`, flush on
   the main thread. Ignore `Deleted` (or mark script missing), handle `Renamed`.
4. **Reload semantics**: parse/compile into a **fresh** MoonSharp sandbox; atomically swap the cached
   script; keep per-entity state if the script exposes stable globals, otherwise reset; log compile
   errors with file/line and keep the previous version running.
5. **Gating**: enable only under `#if TOOLS || DEBUG` or an explicit `SERVERONLY` CVar defaulting off, so
   release servers never watch the filesystem; add an admin `lua_reload` command for manual reload.
6. **Prototype integration**: if scripts only produce data (e.g. generated prototypes), feed YAML into
   `IPrototypeManager.LoadString` + `ReloadPrototypes` (pattern `SharedPrototypeLoadManager.cs:43-45`).

### 4.3 Differences from Garry's Mod to keep in mind
- GMod reloads Lua inside one process that is both server and client for the local player; SS14 has a
  strict authoritative server and a compiled, sandboxed client. Only server scripts can hot reload.
- GMod has no ECS/prediction constraints; SS14 scripts must not mutate entities off the simulation
  thread, and Lua-driven actions cannot be predicted client-side without client Lua.
- Replays record network state/messages, so server-only nondeterminism is mostly invisible, but scripts
  that affect gameplay should still be deterministic to keep replays and debugging sane.

## 5. Goal C — Lua for faster development

Two distinct modes:

| Mode | Who | Value | Cost |
|---|---|---|---|
| Dev-only Lua (trusted) | maintainers | fast iteration, hot reload, less rebuild time | interpreter dependency, dev watcher |
| Player Lua (untrusted) | players | robot programming, automation | sandbox + budgets + audit + persistence |

For dev speed, note the existing alternative: the server C# REPL (`scsi`, HOST) already executes
arbitrary C# with full API access and no rebuild. Lua's advantages are hot reload, a safer default
surface, and reusing the same runtime for player content. Recommendation: build one Lua runtime with two
trust profiles rather than two systems.

## 6. Goal D — In-game robot programming (roboticist feature)

### 6.1 Proposed model
- **Program item/module**: a `BorgModule` or mech equipment item with a `[DataField] string Source`
  (or `ProtoId<LuaScriptPrototype>`); installed through the existing module/equipment pipelines.
- **Program runner**: a `LuaProgramRunnerComponent` on the borg/mech/NPC; the compiled `LuaSystem`
  ticks registered runners with a per-entity VM and dispatches sensor/event callbacks.
- **I/O API (curated)**: device-link ports in/out, device-network send/receive, power/battery level,
  health/status, movement intent, speech/emote, equipment activation. For NPC robots also
  `NPCSystem.SetBlackboard` and steering registration.
- **Editor UI**: a BUI `FancyWindow` with a `TextEdit` (no syntax highlighting needed for MVP), modeled
  on `PaperWindow`/`CustomExamineSettingsWindow`; program item holds the text (networked like paper).
- **Persistence**: store programs on a savable item (e.g. a "program core" entity with `save: true`) or
  account-side; borgs/mechs themselves are `save: false`, so attaching scripts directly to them loses
  them on map save.

### 6.2 Security for player code
- Player scripts run on the **server**, in a Lua sandbox with **no CLR reflection**, no `io`/`os`/
  `load`/`loadfile`/`dofile`/`require`/`debug`, and a curated global table exposing only gameplay APIs.
- Hard budgets: wall-clock/instruction budget per tick and per script (see §8), max active VMs, max
  source size, compile rate limits, error isolation via `pcall`.
- Admin logging of script create/edit/run events (the existing C# scripting path does **not** log script
  bodies — a gap to improve for player code).
- Consider a review/whitelist flow for scripts that can control physical robots to reduce griefing.

### 6.3 Gaps in current robotics content
No Roboticist job; borg/mech `save: false`; no programmable circuit/automation system; no generic
code-execution event; player-controlled borg actions are input-driven rather than logic-driven. An MVP
should pick one robot archetype (e.g. autonomous NPC robots first, player borg modules second).

## 7. Security & sandbox design (server-side)

| Layer | Measure |
|---|---|
| Language | MoonSharp with a restricted `CoreModules` set; no `require`/`loadfile` (custom `ScriptLoader`); no `debug`; no `io`/`os` |
| Bindings | A hand-written API table; never expose `ScriptGlobalsShared`-style `res<T>()`/reflection/Toolshed |
| Resource limits | Per-script step/time budget enforced by the host (coroutine yielding or debugger hooks — verify what the chosen MoonSharp version supports **[external]**); max VMs; source size cap |
| Isolation | One `Script` per program; `pcall` around every callback; errors disable/quarantine the program with a popup/log |
| Permissions | Dev mode: `AdminFlags.Host`. Player mode: gameplay permissions only; script actions are still subject to in-game checks (range, access, action blockers) |
| Audit | Admin-log program edits/runs; surface program identity on examine/console |
| Threading | All execution on the main simulation thread; watcher callbacks only enqueue |

Known threat: MoonSharp's CLR interop is powerful. If any C# object with dangerous members is exposed
as userdata, scripts can reach it. Bind only purpose-built wrapper objects/functions, never raw
`IEntityManager`/`IoCManager`/`Type` objects.

## 8. Performance

- MoonSharp is a managed interpreter and is slower than native Lua and much slower than C# **[external;
  benchmark before committing]**. Budgeting is mandatory.
- Copy existing time-slicing patterns: `atmos.max_process_time` (3 ms, `AtmosphereSystem.Processing.cs:22-27`),
  `explosion.max_tick_time` (7 ms, `ExplosionSystem.Processing.cs:33-44`), pathfinding 3 ms
  (`PathfindingSystem.cs:61`), `DungeonJob.TimeSlice`/HTN plan jobs for long work.
- Practical plan: a `lua.max_process_time` CVar (default 1-3 ms), round-robin scripts, sleep idle
  scripts, cap VM count, and auto-yield long loops. A non-yielding script blocks the main thread and
  starves the 15 s watchdog heartbeat (`WatchdogApi.cs:29-30`), so a hard budget is a safety requirement,
  not an optimization.

## 9. Dependency, licensing, packaging

### 9.1 MoonSharp **[external]**
- Pure C# Lua 5.2 implementation, no native dependencies; coroutines, metatables, JSON, debugger.
- License: **3-clause BSD** (per project README) — one-way compatible with this project's AGPLv3; keep
  the license notice in distributions.
- Versions: stable `2.0.0`; beta `3.0.0-beta.1` (2026). The project announced a new maintainer in
  July 2026 after a long dormancy — evaluate stability before pinning.
- Alternatives to evaluate: **NLua/KeraLua** (native Lua; per-RID packaging burden under
  `Content.Packaging`), **Lua-CSharp** (newer pure-C# interpreter, high-performance claim **[external]**),
  **WattleScript** (MoonSharp fork **[external]**), or **Jint** (JavaScript, pure managed) if language
  choice is flexible.

### 9.2 Adding it (content-only, server-side)
1. `<PackageVersion Include="MoonSharp" Version="…" />` in `Directory.Packages.props` (central versions).
2. `<PackageReference Include="MoonSharp" />` in `Content.Server/Content.Server.csproj` **only** — never
   `Content.Shared`/`Content.Client` (client sandbox type check + CI `SandboxTest`).
3. Add the assembly to `Content.Packaging/ServerPackaging.cs` `ServerExtraAssemblies` (NetCord
   precedent, `ServerPackaging.cs:45-51`) so it ships in server zips.
4. If client-side Lua is ever wanted: requires an engine change (`Sandbox.yml` whitelist, engine module
   shipping, or a `SkipIfSandboxed` leaf assembly) — conflicts with the "never modify engine" constraint.

## 10. Recommended phased plan

| Phase | Scope | Outcome | Effort **[inference]** |
|---|---|---|---|
| 0. Spike | Server-only MoonSharp; `LuaScriptSystem`; watch `<UserData>/Lua`; console `lua_eval`/`lua_reload`; curated API for entities/timers/say | Devs can hot-reload gameplay snippets; proves perf/security assumptions | 1-2 weeks |
| 1. Dev tooling | Script prototypes (`luaScript` kind), `LuaComponent`, example scripts, docs, unit tests for sandbox | Hot-reloadable behavior for prototypes without rebuilds | 2-4 weeks |
| 2. Robot MVP | Program item + runner component, editor BUI, device-link/network I/O, one robot archetype, budgets | Roboticists can code a robot in-game | 4-8 weeks |
| 3. Hardening | Quotas, audit logging, persistence design, replay/perf testing, admin controls | Production-ready player scripting | 4-6 weeks |
| — (non-goal) | Dynamic CLR component generation | — | months + engine changes; not recommended |

## 11. Open questions

1. Is MoonSharp (BSD-3, renewed maintenance) or Lua-CSharp/NLua the right runtime? Benchmark against a
   representative robot script before committing.
2. Does the chosen runtime support host-enforced instruction/time budgets without debugger overhead?
3. Where do player programs persist: item `DataField`, account preferences (DB migration), or a new
   file store? Borgs/mechs are `save: false` today.
4. Should player scripts run for player-controlled borgs, autonomous NPC robots, or both?
5. What API surface is safe for players, and who reviews scripts that can move/attack?
6. Does the server's no-sandbox default permit shipping MoonSharp in `Content.Server` without engine
   changes? (Analysis says yes; verify packaging and CI.)
7. How are script errors surfaced to the player/admin, and how is abuse handled?
8. Replay/audit: should script bodies and actions be recorded (admin logs currently do not log C#
   script bodies either)?
9. Is there appetite for an upstream contribution (engine-side `IReloadManager` server support or a
   first-class scripting layer) instead of a fork-local system?

## 12. Source anchors

**Engine scripting:** `RobustToolbox/Robust.Shared.Scripting/{ScriptInstanceShared,ScriptGlobalsShared}.cs`,
`RobustToolbox/Robust.Server/Scripting/ScriptHost.cs`, `RobustToolbox/Robust.Client/Console/Script*`,
`RobustToolbox/MSBuild/Robust.Properties.targets:19-22`.
**Locks:** `RobustToolbox/Robust.Shared/GameObjects/ComponentFactory.cs:96-97,525-554`,
`EntityManager.cs:223-233`, `Robust.Shared/GameObjects/EntityEventBus.Directed.cs:416-423`,
`Robust.Shared/Serialization/Manager/SerializationManager.cs:39-45,302-312`,
`Robust.Shared/Serialization/RobustSerializer.cs:59-98`,
`Robust.Shared/GameObjects/Systems/PrototypeReloadSystem.cs:22-82`.
**Prototype runtime load:** `Robust.Shared/Upload/SharedPrototypeLoadManager.cs:43-46`,
`Robust.Shared/Prototypes/PrototypeManager.cs:1017-1026`.
**Hot reload:** `Robust.Client/Utility/ReloadManager.cs:42-143`, `IReloadManager.cs`,
`XamlHotReloadManager.cs`, `Robust.Shared/Timing/Timer.cs`, `Asynchronous/TaskManager.cs:31-34`.
**Sandbox:** `Robust.Shared/ContentPack/AssemblyTypeChecker.cs`, `ContentPack/Sandbox.yml`,
`ModLoader.cs:120-139,452-461`, `Robust.Client/GameControllerOptions.cs:11`, `ServerOptions.cs:11`.
**Robotics/I-O:** `Content.Server/Silicons/Borgs/*`, `Content.Server/Mech/*`,
`Content.Server/DeviceLinking/Systems/*`, `Content.Server/DeviceNetwork/Systems/*`,
`Content.Server/NPC/Systems/NPCSystem.Blackboard.cs:7-16`, `Content.Server/NPC/Systems/NPCSteeringSystem.cs:253-291`,
`Content.Shared/Paper/PaperComponent.cs`, `Resources/Prototypes/Entities/Mobs/Cyborgs/*`.
**Performance patterns:** `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Processing.cs:22-27`,
`Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs:33-44`,
`Content.Server/NPC/Pathfinding/PathfindingSystem.cs:61`, `Content.Server/Procedural/DungeonJob/DungeonJob.cs:32`.
**Packaging/licensing:** `Directory.Packages.props`, `Content.Server/Content.Server.csproj:18`,
`Content.Packaging/ServerPackaging.cs:45-51`, `LEGAL.md`, `RobustToolbox/LICENSE-MIT.TXT`.
