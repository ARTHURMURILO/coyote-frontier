# API — Key Symbols

> The most important classes, systems, managers and events a future agent must know.
> "Dependencies" lists notable direct collaborators, not every reference.
> Engine symbols are under `RobustToolbox/`; content symbols are repo-relative.

## Engine foundation APIs (used by content daily)

| Name | Location | Purpose | Notes / dependencies |
|---|---|---|---|
| `GameServer` / `GameClient` / `GameShared` | `RobustToolbox/Robust.Shared/ContentPack/GameShared.cs:11` (+ `GameServer.cs`, `GameClient.cs`) | Entry-point base classes discovered by `ModLoader`; lifecycle `PreInit/Init/PostInit/Update/Shutdown` | Content `EntryPoint`s subclass these |
| `EntityManager` | `RobustToolbox/Robust.Shared/GameObjects/EntityManager.cs:34` | Entity/component storage, lifecycle, queries | `ServerEntityManager`/`ClientEntityManager` |
| `EntitySystem` | `RobustToolbox/Robust.Shared/GameObjects/EntitySystem.cs:24` | Behavior unit; `Initialize/Update/FrameUpdate/Shutdown`; `UpdatesBefore/After`, `UpdatesOutsidePrediction` | Subscriptions in `Initialize` only |
| `EntitySystemManager` | `RobustToolbox/Robust.Shared/GameObjects/EntitySystemManager.cs:30` | Discovers/initializes systems, computes update order via topo sort | Systems resolved through child IoC collection |
| `EntityEventBus` | `RobustToolbox/Robust.Shared/GameObjects/EntityEventBus.*.cs` | Directed + broadcast events; queued events | Locked after startup; see `.ai/EVENTS.md` |
| `EntityQueryEnumerator<T>` / `AllEntityQueryEnumerator<T>` | `RobustToolbox/Robust.Shared/GameObjects/EntityManager.Components.cs:2115,2424` | Cached component iteration | `EntityQueryEnumerator` skips paused entities |
| `IPrototypeManager` | `RobustToolbox/Robust.Shared/Prototypes/IPrototypeManager.cs` | Prototype lookup/validation/reload | `ProtoId<T>`, `EntProtoId` wrappers |
| `ISerializationManager` | `RobustToolbox/Robust.Shared/Serialization/Manager/ISerializationManager.cs:12` | YAML/data-field serialization, validation | `[DataField]` |
| `IResourceManager` | `RobustToolbox/Robust.Shared/ContentPack/IResourceManager.cs:14` | VFS (`/Prototypes`, `/Textures`...), `UserData` writes | `ResPath` |
| `IConfigurationManager` | `RobustToolbox/Robust.Shared/Configuration/IConfigurationManager.cs:59` | CVars: `GetCVar`, `SetCVar`, `OnValueChanged` | `CVar.REPLICATED` etc. |
| `ICommonSession` / `ISharedPlayerManager` | `RobustToolbox/Robust.Shared/Player/ICommonSession.cs:13`, `ISharedPlayerManager.cs:13` | Sessions, `AttachedEntity`, `UserId`, `Filter` | Content hangs `ContentPlayerData` off `SessionData.ContentDataUncast` |
| `SharedPvsOverrideSystem` | `RobustToolbox/Robust.Shared/GameStates/SharedPvsOverrideSystem.cs:12` | Force entities visible to sessions/globally | `PvsOverrideSystem` (server) |
| `SharedUserInterfaceSystem` | `RobustToolbox/Robust.Shared/GameObjects/Systems/SharedUserInterfaceSystem.cs:21` | BUI open/state/messages, prediction | `TryOpenUi :645`, `SetUiState :727`, `SendUiMessage :824` |
| `SharedPhysicsSystem` | `RobustToolbox/Robust.Shared/Physics/Systems/SharedPhysicsSystem.cs:19` | Physics body/velocity/contacts/queries | Engine solver |
| `SharedMapSystem` / `SharedTransformSystem` | `RobustToolbox/Robust.Shared/GameObjects/Systems/` | Maps, tiles, pause; parenting/coordinates | Map init pipeline |
| `SharedAudioSystem` | `RobustToolbox/Robust.Shared/Audio/Systems/SharedAudioSystem.cs:30` | `PlayGlobal/PlayEntity/PlayPvs/PlayPredicted` | `SoundSpecifier`, collections |
| `IConsoleCommand` / `IConsoleHost` | `RobustToolbox/Robust.Shared/Console/IConsoleCommand.cs:12`, `IConsoleHost.cs:79` | Console commands auto-discovered by reflection | `[AdminCommand]` attributes are content |
| `Timer` / `ITimerManager` | `RobustToolbox/Robust.Shared/Timing/Timer.cs:9` | `Spawn/SpawnRepeating/Delay` on main thread | `GameTiming.CurTime` |
| `IReplayRecordingManager` | `RobustToolbox/Robust.Shared/Replays/IReplayRecordingManager.cs:13` | Server replay record/stop | Driven by `GameTicker.Replays.cs` |

## Server managers (content IoC — `Content.Server/IoC/ServerContentIoC.cs`)

| Name | Location | Purpose | Dependencies |
|---|---|---|---|
| `GameTicker` | `Content.Server/GameTicking/GameTicker.cs:34` + 10 partials | Round state machine, spawning, presets/rules, maps, replays, status | Station, Mind, Roles, DB, preferences, map loader, ghost |
| `ChatManager` / `ChatSystem` | `Content.Server/Chat/Managers/ChatManager.cs`, `Chat/Systems/ChatSystem.cs:50` | IC/OOC/radio/announcements, net dispatch, rate limits | `MsgChatMessage`, radio, bwoink, Discord chat link |
| `ServerPreferencesManager` | `Content.Server/Preferences/Managers/ServerPreferencesManager.cs:24` | Profile cache, validation, DB save, net sync | `IServerDbManager`, consent, playtime |
| `IServerDbManager` (`ServerDbManager`) | `Content.Server/Database/ServerDbManager.cs:30,407` | All persistence API; provider selection | EF Core `ServerDbContext`, `BanMatcher`, notifications |
| `ConnectionManager` | `Content.Server/Connection/ConnectionManager.cs` | Admission (bans, panic bunker, whitelist, IPIntel, MiniAuth), connection logs | `BanManager`, `MiniAuthManager`, DB |
| `AdminManager` / `BanManager` / `AdminNotesManager` / `AdminLogManager` | `Content.Server/Administration/...` | Admin rank/flags, bans, notes/watchlists, batched logs | DB, Discord webhooks, permissions |
| `ServerApi` | `Content.Server/Administration/ServerApi.cs` | HTTP admin API (`/admin/*`) for external control | Token auth, `MiniAuthManager` schema |
| `MiniAuthManager` | `Content.Server/_NF/Auth/Auth.cs:12` | Multi-server dAuth check via `/admin/info` | `nf.allow_multi_connect`, fail-open |
| `DiscordLink` / `DiscordChatLink` | `Content.Server/Discord/DiscordLink/*` | Bot gateway + chat bridge/webhooks | NetCord; no account linking |
| `ServerConsentManager` | `Content.Server/Consent/ServerConsentManager.cs:17` | Account/character consent settings load/save/sync | DB, preferences, `ConsentSystem` |
| `PlayTimeTrackingManager` | `Content.Server/Players/PlayTimeTracking/PlayTimeTrackingManager.cs:58` | Playtime accumulation/DB save; tracker syncing | DB, CVar interval |
| `JobWhitelistManager` | `Content.Server/Players/JobWhitelist/JobWhitelistManager.cs:20` | Job/ghost-role whitelist checks | DB, `ContentPlayerData.Whitelisted` |
| `UserDbDataManager` | `Content.Server/Database/UserDbDataManager.cs:19` | Orchestrates concurrent per-user DB loads | prefs, playtime, whitelist, rate limits, consent |
| `EuiManager` | `Content.Server/EUI/EuiManager.cs` | Server-driven admin/global UIs | `BaseEui`, `MsgEui*` |
| `VoteManager` | `Content.Server/Voting/Managers/VoteManager.cs` | Vote types/countdown/results | `MsgVote*` |
| `ServerUpdateManager` | `Content.Server/ServerUpdates/ServerUpdateManager.cs` | Update/uptime/empty-server restarts | `IBaseServer.Shutdown` |
| `GameMapManager` | `Content.Server/Maps/GameMapManager.cs` | Map prototype selection/rotation/persistence map | `game.map`, `game.map_pool` |
| `RulesManager` | `Content.Server/Info/RulesManager.cs` | Core rules handshake + last-read persistence | `MsgRules`, DB |
| `ContentNetworkResourceManager` | `Content.Server/Administration/ContentNetworkResourceManager.cs` | Uploaded client resource cache | `UploadedResourceLog` |

## Shared/server gameplay APIs

| Name | Location | Purpose | Notes |
|---|---|---|---|
| `SharedGameTicker` | `Content.Shared/GameTicking/SharedGameTicker.cs:16` | Abstract base + networked ticker events (`TickerJoinLobbyEvent` etc.) | Client `ClientGameTicker` extends it |
| `SharedStationSystem` / `StationSystem` | `Content.Shared/Station/SharedStationSystem.cs:7`, `Content.Server/Station/Systems/StationSystem.cs:31` | Station registry/component management | `StationDataComponent` |
| `StationSpawningSystem` | `Content.Server/Station/Systems/StationSpawningSystem.cs:46` | Spawn player mob, gear, loadout, ID/PDA | `SpawnPlayerMob :109`; `PlayerSpawningEvent` |
| `StationJobsSystem` | `Content.Server/Station/Systems/StationJobsSystem.cs:46` | Job slot assignment/overflow | `AssignJobs (Roundstart.cs:59)`, `PickBestAvailableJobWithPriority :427` |
| `SharedMindSystem` / `MindSystem` | `Content.Shared/Mind/SharedMindSystem.cs:22`, `Content.Server/Mind/MindSystem.cs:20` | Nullspace mind entities; `CreateMind/TransferTo/Visit/WipeMind` | `MindComponent`, `MindContainerComponent` |
| `SharedRoleSystem` / `RoleSystem` | `Content.Shared/Roles/SharedRoleSystem.cs:20`, `Content.Server/Roles/RoleSystem.cs:9` | Mind role entities (`MindAddJobRole :106`), antag queries/briefings | `MindRoleComponent`, `MindRoleJob` |
| `AntagSelectionSystem` | `Content.Server/Antag/AntagSelectionSystem.cs:40` | Antag selection/takeover (`MakeAntag :372`) | `AntagPrototype`, selection definitions, rigs loadouts |
| `GameRuleSystem<T>` | `Content.Server/GameTicking/Rules/GameRuleSystem.cs:10` | Rule entity lifecycle | `GameRuleAdded/Started/EndedEvent` |
| `SharedHandsSystem` / `HandsSystem` | `Content.Shared/Hands/EntitySystems/SharedHandsSystem.cs:16`, `Content.Server/Hands/Systems/HandsSystem.cs:33` | Hand inventory, pickup/drop/use | Interaction, Inventory, Storage |
| `InventorySystem` / `ServerInventorySystem` | `Content.Shared/Inventory/InventorySystem.Equip.cs:24`, `Content.Server/Inventory/ServerInventorySystem.cs:6` | Slots/equip/relay events | `IInventoryRelayEvent` for armor/damage |
| `SharedStorageSystem` / `StorageSystem` | `Content.Shared/Storage/EntitySystems/SharedStorageSystem.cs:51`, `Content.Server/Storage/EntitySystems/StorageSystem.cs:19` | Containers UI/storage access | EntityStorage, Item, Hands |
| `SharedInteractionSystem` | `Content.Shared/Interaction/SharedInteractionSystem.cs:54` | In-world interaction pipeline/blocking/ranges | Verbs, do-after |
| `SharedBodySystem` / `BodySystem` | `Content.Shared/Body/Systems/SharedBodySystem.cs:10`, `Content.Server/Body/Systems/BodySystem.cs:20` | Body parts/organs graph | Prototypes `Body/Prototypes` |
| `DamageableSystem` | `Content.Shared/Damage/Systems/DamageableSystem.cs:22` | Damage application/containers | Armor, stamina, mob threshold |
| `SharedBloodstreamSystem` / metabolism systems | `Content.Shared/Body/Systems/SharedBloodstreamSystem.cs:27`, `Content.Server/Body/Systems/MetabolizerSystem.cs:20` | Blood/chem processing | Chemistry, EntityEffects |
| `AtmosphereSystem` | `Content.Server/Atmos/EntitySystems/AtmosphereSystem.cs:26` | Grid gas simulation (LINDA/Monstermos) | NodeContainer, puddles, physics |
| `PowerNetSystem` / `ApcSystem` | `Content.Server/Power/EntitySystems/PowerNetSystem.cs:21`, `ApcSystem.cs:19` | Power networks/APC | `NodeGroupSystem`, `BatteryRampPegSolver` |
| `SharedSolutionContainerSystem` | `Content.Shared/Chemistry/EntitySystems/SharedSolutionContainerSystem.cs:66` | Solutions/reagents containers | Reactions, injectors, metabolism |
| `ChemicalReactionSystem` | `Content.Shared/Chemistry/Reaction/ChemicalReactionSystem.cs:16` | Reagent reactions | Tile reactions, atmos temp |
| `ConstructionSystem` | `Content.Server/Construction/ConstructionSystem.cs:16` | Construction/deconstruction graphs | Tools, materials, machine frames |
| `LatheSystem` | `Content.Server/Lathe/LatheSystem.cs:45` | Lathe recipes, queues, material use | MaterialStorage, research |
| `SharedActionsSystem` / `ActionsSystem` | `Content.Shared/Actions/SharedActionsSystem.cs:22`, `Content.Server/Actions/ActionsSystem.cs:7` | Action bar/cooldowns/charges | Hands/items, magic |
| `SharedShuttleSystem` / `ShuttleSystem` | `Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs:15`, `Content.Server/Shuttles/Systems/ShuttleSystem.cs:37` | Shuttle grids, FTL, docking, thrusters, emergency shuttle | Physics, power, docking signals |
| `ExplosionSystem` / `TriggerSystem` | `Content.Server/Explosion/EntitySystems/ExplosionSystem.cs:35`, `TriggerSystem.cs:80` | Explosions and triggers | Tile flood, damage, signals |
| `NPCSystem` / `HTNSystem` / `NPCCombatSystem` | `Content.Server/NPC/Systems/*` | HTN AI, pathfinding, steering, combat | Hands/inventory, weapons, perception |
| `SharedChatSystem` | `Content.Shared/Chat/SharedChatSystem.cs` | Prefix parsing, radio keys, sanitization helpers | `ChatSystem`, radio |
| `RadioSystem` | `Content.Server/Radio/EntitySystems/RadioSystem.cs` | Radio broadcast/receive/degradation/telecom | Encryption keys, headsets, jammers |
| `BwoinkSystem` | `Content.Shared/Administration/SharedBwoinkSystem.cs` (shared) + server/client | AHelp network path + Discord relay | `AdminFlags.Adminhelp` |
| `ConsentSystem` / `SharedConsentSystem` | `Content.Server/Consent/ConsentSystem.cs:49`, `Content.Shared/Consent/SharedConsentSystem.cs:74` | `HasConsent` checks | Mind → session user → toggles; NPC=all, client=false |

## Client APIs

| Name | Location | Purpose | Notes |
|---|---|---|---|
| `Content.Client.Entry.EntryPoint` | `Content.Client/Entry/EntryPoint.cs:45` | Client bootstrap; state selection, overlays, BUI tick | |
| `ClientContentIoC.Register` | `Content.Client/IoC/ClientContentIoC.cs:33` | Client service registrations | |
| `ClientGameTicker` | `Content.Client/GameTicking/Managers/ClientGameTicker.cs:18` | Consumes ticker net events; drives state changes | |
| `MainScreen` / `LobbyState` / `GameplayState` / `LauncherConnecting` | `Content.Client/{MainMenu,Lobby,Gameplay,Launcher}` | Client states | `GameplayState` loads `DefaultGameScreen`/`SeparatedChatGameScreen` |
| `UIController` + `UserInterfaceManager.SetupControllers` | `RobustToolbox/Robust.Client/UserInterface/Controllers/...` | Reflection-discovered UI controllers with `[UISystemDependency]`, `IOnStateEntered<T>`, `IOnSystemChanged<T>` | |
| `BoundUserInterface` / `ActivatableUISystem` | `RobustToolbox/.../BoundUserInterface.cs:12`, `Content.Shared/UserInterface/ActivatableUISystem.cs:16` | BUI client base + open-on-activate | `UserInterfaceComponent`, `InterfaceData` |
| `BuiPredictionState` / `BuiPreTickUpdateSystem` | `Content.Client/UserInterface/BuiPredictionState.cs:27` | BUI prediction queue | Runs `EntryPoint.Update` PreEngine |
| `ScalingViewport` / `ViewportUIController` / `SharedContentEyeSystem` | `Content.Client/Viewport/ScalingViewport.cs:22`, `Content.Client/UserInterface/Systems/Viewport/ViewportUIController.cs:13`, `Content.Shared/Movement/Systems/SharedContentEyeSystem.cs:17` | Rendering viewport/eye/zoom | |
| `ContentAudioSystem` | `Content.Client/Audio/ContentAudioSystem.cs:7` (+ AmbientMusic/LobbyMusic partials) | Client audio + ambience | |
| `ContentLocalizationManager` | `Content.Shared/Localizations/ContentLocalizationManager.cs:8` | Fluent culture + custom functions | `.ftl` under `Resources/Locale` |
| `ContentContexts` / `ContentKeyFunctions` | `Content.Client/Input/ContentContexts.cs:10`, `Content.Shared/Input/ContentKeyFunctions.cs:6` | Input contexts/key functions | `Resources/keybinds.yml` |

## Fork-specific APIs

| Name | Location | Purpose |
|---|---|---|
| `BankSystem` (+ `.Ledger`, `.Sector`) | `Content.Server/_NF/Bank/BankSystem.cs:14` | Per-character bank accounts persisted via preferences; sector accounts |
| `MarketSystem` | `Content.Server/_NF/Market/Systems/MarketSystem.cs:14` | Market consoles/crate machines |
| `ShipyardSystem` | `Content.Server/_NF/Shipyard/Systems/ShipyardSystem.cs:31` | Buy/sell ships, spawn vessel maps, pricing |
| `ShuttleDeedSystem` | `Content.Server/_NF/Shipyard/Systems/ShuttleDeedSystem.cs:7` | Ship ownership deeds |
| `BluespaceDrydockSystem` | `Content.Server/_NF/Shipyard/Systems/BluespaceDrydockSystem.cs:38` | Store/retrieve shuttles async, map serialization |
| `ShuttleRecordsSystem` | `Content.Server/_NF/ShuttleRecords/ShuttleRecordsSystem.cs:19` | Sector shuttle registry |
| `NFCargoSystem` | `Content.Server/_NF/Cargo/Systems/NFCargoSystem.cs:27` | Frontier cargo (station orders, telepads, trade crates) |
| `BountyContractSystem` | `Content.Server/_NF/BountyContracts/BountyContractSystem.cs:21` | Player bounty contracts |
| `NfAdventureRuleSystem` / `PointOfInterestSystem` | `Content.Server/_NF/GameRule/NfAdventureRuleSystem.cs:33`, `PointOfInterestSystem.cs:21` | Sector generation, POIs, round-end summary |
| `PublicTransitSystem` | `Content.Server/_NF/PublicTransit/PublicTransitSystem.cs:43` | Scheduled shuttle routes |
| `CryoSleepSystem` | `Content.Server/_NF/CryoSleep/CryoSleepSystem.cs:39` | Cryo storage/return/respawn |
| `NFPirateSystem` / `NFPirateRuleSystem` | `Content.Server/_NF/Pirate/Systems/NFPirateSystem.cs:9`, `_NF/GameTicking/Rules/NFPirateRuleSystem.cs:12` | Pirate NPCs/events; default preset |
| `RoleplayIncentiveSystem` | `Content.Server/_CS/RolePlayIncentiveServer/RoleplayIncentiveSystem.cs:43` | Pay players for RP actions via NF bank |
| `NeedSystem` | `Content.Server/_CS/Needs/NeedSystem.cs:15` | Per-species needs |
| `ScentSystem` | `Content.Shared/_CS/SniffAndSmell/ScentSystem.cs:21` | Sniff/smell proximity system |
| `SizeManipulationSystem` / `HeightAdjustSystem` | `Content.Server/_CS/Body/Systems/SizeManipulationSystem.cs`, `Content.Shared/HeightAdjust/HeightAdjustSystem.cs:13` | Size gun/scaling |
| `ServerConcealableClothingSystem` | `Content.Server/_PS/Clothing/ServerConcealableClothingSystem.cs:8` | Implant-based concealed clothing |

## Important events (full catalog in `.ai/EVENTS.md`)

`ComponentAdd/Init/Startup/Shutdown/Remove`, `MapInitEvent`, `EntityTerminatingEvent`,
`EntityPausedEvent`/`EntityUnpausedEvent`, `EntInserted/RemovedFromContainerMessage`,
`RoundStartingEvent`, `RoundStartAttemptEvent`, `RoundStartedEvent` (server-only, `_NF`),
`GameRunLevelChangedEvent`, `GameRuleAdded/Started/EndedEvent`, `RulePlayerSpawningEvent`,
`RulePlayerJobsAssignedEvent`, `PlayerBeforeSpawnEvent`, `PlayerSpawnCompleteEvent`,
`RoundEndTextAppendEvent`, `RoundRestartCleanupEvent`, `PrototypesReloadedEventArgs`,
`Ticker*` network events, `Msg*` network messages.

## How-to guides

Step-by-step extension recipes live in `.ai/guides/` (dungeons, humanoid enemy NPCs, custom UI/BUI,
gamemodes, admin commands, species, saving grids). The API entries above are the symbols those guides build on.
