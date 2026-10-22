# System: Client, UI & Prediction

## Purpose

Client bootstrap, state machine, UI (XAML/UIControllers/BUIs), viewport/overlays, input,
prediction model, audio and localization.

How-to: custom BUI windows (e.g. two-panel interaction UI) `.ai/guides/adding-custom-ui.md`.

## Location

| Piece | Path |
|---|---|
| Client bootstrap | `Content.Client/Entry/EntryPoint.cs:45`, `Content.Client/IoC/ClientContentIoC.cs:33` |
| States | `Content.Client/{MainMenu/MainMenu.cs, Launcher/LauncherConnecting.cs, Lobby/LobbyState.cs, Gameplay/GameplayState.cs, Mapping/MappingState.cs, Replay/...}` |
| Screens | `Content.Client/UserInterface/Screens/{DefaultGameScreen,SeparatedChatGameScreen}.xaml(.cs)` |
| UI controllers | `Content.Client/UserInterface/Systems/...` (+ engine controller discovery) |
| BUIs | feature folders + engine `SharedUserInterfaceSystem`; `Content.Shared/UserInterface/ActivatableUISystem.cs` |
| BUI prediction | `Content.Client/UserInterface/BuiPredictionState.cs:27`, `BuiPreTickUpdateSystem.cs:23` |
| Viewport | `Content.Client/Viewport/{ScalingViewport.cs,ViewportManager.cs}`, `UserInterface/Controls/MainViewport.cs`, `UserInterface/Systems/Viewport/ViewportUIController.cs` |
| Eye | `Content.Shared/Movement/Systems/SharedContentEyeSystem.cs:17`, `Content.Client/Movement/Systems/ContentEyeSystem.cs:9` |
| Overlays | `Content.Client/Overlays/*`, `Content.Client/Viewport/*Overlay*` |
| Input | `Content.Client/Input/ContentContexts.cs:10`, `Content.Shared/Input/ContentKeyFunctions.cs:6`, `Resources/keybinds.yml` |
| Audio | `Content.Client/Audio/ContentAudioSystem.cs` (+ AmbientMusic/LobbyMusic partials) |
| Localization | `Content.Shared/Localizations/ContentLocalizationManager.cs:8`, `Resources/Locale/**` |

## Bootstrap

`Init`: `ClientContentIoC.Register` → BuildGraph → localization → component auto-registration →
prototype ignore list → net IDs → manager init. `PostInit`: stylesheets, input contexts, parallax,
global overlays, chat/consent/prefs/EUI/vote init, theme, run-level hook, disables engine main viewport,
then `SwitchToDefaultState()` (replay bundle → launcher → main menu).

`Update` at `FramePreEngine` runs `DebugMonitorManager.FrameUpdate()`; at `PreEngine` runs
`BuiPreTickUpdateSystem.RunUpdates()` when in game.

## State machine

```
Replay bundle? → Replay loading states
else if launcher → LauncherConnecting (Connecting / ConnectFailed / Disconnected)
else → MainScreen
network TickerJoinLobbyEvent → LobbyState (LobbyGui screen)
network TickerJoinGameEvent  → GameplayState (DefaultGameScreen or SeparatedChatGameScreen by CVar)
Mapping/admin → MappingState (extends GameplayStateBase)
replay playback → LoadingScreen / ReplayLoadingFailed / spectate/ghost states
```

State changes are requested through engine `IStateManager.RequestStateChange`; `ClientGameTicker`
(`Content.Client/GameTicking/Managers/ClientGameTicker.cs:18`) drives lobby/game transitions from ticker
network events.

## UI architecture

- **UIControllers**: engine `UIController` classes auto-discovered by reflection
  (`UserInterfaceManager.SetupControllers`), dependency-sorted, injected via `[UISystemDependency]`,
  and notified through `IOnStateEntered<T>`, `IOnStateExited`, `IOnSystemChanged<T>`, `IOnSystemLoaded`.
  Access via `IUserInterfaceManager.GetUIController<T>()` or `UIManager.GetActiveUIWidgetOrNull<T>()`.
- **Screens**: `UIScreen` subclasses with `GetWidget`/`GetOrAddWidget`; `DefaultGameScreen.xaml` arranges
  `MainViewport`, `GameTopMenuBar`, `ActionsBar`, `GhostGui`, `InventoryGui`, `HotbarGui`, `ChatBox`,
  `AlertsUI`. `GameplayStateLoadController` fans out screen load/unload.
- **XAML**: `RobustXamlLoader.Load(this)` in ctors; `[GenerateTypedNameReferences]` generates fields for
  `Name=`; `{Loc key}` binds localization; hot reload only in TOOLS.
- **Stylesheets/themes**: `StylesheetManager` provides `StyleNano`/`StyleSpace`; default theme
  `SS14DefaultTheme` set in EntryPoint.
- **BUIs** (bound user interfaces):
  - Server sets `UserInterfaceComponent.Interfaces` in YAML (`enum.XUiKey.Key: {type: XBoundUserInterface}`)
    and pushes state via `SetUiState` (engine `SharedUserInterfaceSystem:727`).
  - Client instantiates the BUI class named by `InterfaceData.ClientType`, receives state through
    `Bui.UpdateState`/`Update<T>`, sends via `SendMessage`/`SendPredictedMessage`.
  - `ActivatableUIComponent` + `ActivatableUISystem` handle opening on activate/in-hand/verb with
    `ActivatableUIOpenAttemptEvent`, `Before/AfterActivatableUIOpenEvent`, single-user UI handling.
  - Manual prediction: `BuiPredictionState` queues predicted messages and replays against latest server
    state; `IBuiPreTickUpdate` runs pre-engine for coalescing; examples: gas canister, lathe, cargo.
- **EUIs** (admin/global): `EuiManager` both sides + `MsgEuiCtl/State/Message`; `BaseEui` classes.

## Viewport / overlays / eye

- `MainViewport` wraps `ScalingViewport`, which creates an engine `IClydeViewport`, renders overlays below/above,
  maps screen↔map coordinates, forwards key events.
- `ViewportUIController` sets viewport size from CVars and assigns `EyeManager.CurrentEye` to the viewport each frame.
- Eye comes from `EyeComponent`; `SharedContentEyeSystem` handles zoom/FOV/light and admin eye requests;
  `ContentEyeSystem` sends predictive zoom events.
- Overlays: engine `OverlayManager` keyed by type and ZIndex; content adds global overlays in EntryPoint
  (`SingularityOverlay`, `RadiationPulseOverlay`, `EmpBlastOverlay`) and component-driven overlays via
  `EquipmentHudSystem<T>` (health bars, job icons, etc.).

## Input

- `ContentKeyFunctions` declares key functions; `Resources/keybinds.yml` provides defaults.
- `ContentContexts.SetupContexts` builds `common`/`human`/`ghost`/`aghost` contexts and hotbar/admin keys.
- Input flows: `GameplayStateBase.OnKeyBindStateChanged` → `ClientFullInputCmdMessage` → engine
  `InputSystem.HandleInputCommand` (local handlers) → `DispatchInputCommand` to server; server re-runs
  the same bound handlers.
- Content binds via `CommandBinds.Builder.Bind(...).Register<T>()`.
- Fork additions in `ContentContexts.cs:77,82` (`SmartEquipWallet`, `OpenWallet`) and consent window key.

## Prediction model

- No `PredictionSystem`/`PredictedComponent`; prediction = client command replay:
  1. Local input handler runs immediately (client feedback).
  2. Message sent to server; client buffers inputs and (if `net.predict`) replays pending inputs each tick.
  3. Server authoritative state; client resets predicted entities and re-applies.
- Shared systems run on both sides; guard with `IGameTiming.IsFirstTimePredicted`; use
  `RaisePredictiveEvent` for client→server predicted calls; set `UpdatesOutsidePrediction = true` for
  systems that must not run in replay (e.g. eye, status effects, pulling, audio).
- BUI prediction via `SendPredictedMessage` + `BuiPredictionState`.

## Audio

- `SharedAudioSystem` in engine provides `PlayGlobal/PlayEntity/PlayPvs/PlayPredicted/PlayStatic`.
- Client root `ContentAudioSystem` sets `UpdatesOutsidePrediction`, fades; partials:
  `AmbientMusic` (prototype-driven, 30-60 s cooldown), `LobbyMusic` (playlist network events/CVars),
  `AndyAnnouncements`.
- Jukebox, ambient sound components, UI sounds (`AudioUIController`).

## Localization

- `ContentLocalizationManager.Initialize` loads `en-US` and registers Fluent functions
  (`PRESSURE`, `POWERWATTS`, `POWERJOULES`, `ENERGYWATTHOURS`, `UNITS`, `TOSTRING`, `LOC`,
  `NATURALFIXED`, `NATURALPERCENT`, `PLAYTIME`, `GASQUANTITY`, plus `MAKEPLURAL`, `MANY`, `NUMBER-WORDS`).
- `.ftl` files under `Resources/Locale/<culture>/**`; lookup with `Loc.GetString("id", args)`.
- No CI FTL validator found; engine logs malformed/duplicate keys.

## Fork integration

`_NF`/`_CS` add overlays, key functions, BUI partials, locale; no fork-specific client IoC registrations,
states, or UI controllers were found.

## Dependencies

Engine UI/input/audio/graphics, `Content.Shared` (BUI state types, net messages), `ClientGameTicker`,
preferences/consent managers, `Resources` data.

## Depended on by

Gameplay interaction (clicking, BUIs), admin tools, lobby/character editor, mapping.

## Notes / caveats

- Engine main viewport is disabled; always use content `MainViewport`.
- `MsgCharacterInfo.cs` is event args, not a NetMessage.
- Client sandbox is on by default; avoid non-whitelisted BCL APIs.
- `TitleWindowManager`, XAML hot reload and mapping are TOOLS/dev-only contexts.
