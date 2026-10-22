# System: Networking, Auth & Client-Server Boundary

## Purpose

Everything that crosses the client/server divide: net messages, component replication, PVS,
connection/auth handshake, server HTTP APIs, Discord integration, and replay transport.

## Location

| Piece | Path |
|---|---|
| Content net messages | `Content.Shared/**/Msg*.cs`, `Content.Shared/**/*Message.cs` |
| Ticker network events | `Content.Shared/GameTicking/SharedGameTicker.cs:61-219` |
| Server admission | `Content.Server/Connection/ConnectionManager.cs`, `ConnectionManager.Whitelist.cs` |
| Multi-server auth | `Content.Server/_NF/Auth/Auth.cs:12` |
| Admin HTTP API | `Content.Server/Administration/ServerApi.cs` (+ `.Utility.cs`) |
| Server info/status | `Content.Server/ServerInfo/ServerInfoManager.cs`, `Content.Server/GameTicking/GameTicker.StatusShell.cs` |
| Discord | `Content.Server/Discord/**` |
| Replay record | `Content.Server/GameTicking/GameTicker.Replays.cs` |
| Client connect | `Content.Client/Launcher/LauncherConnecting.cs`, `Content.Client/MainMenu/MainMenu.cs` |
| BUI transport | engine `SharedUserInterfaceSystem` |

## Client/server split pattern

- `Content.Shared` defines components, net messages, shared systems. `Content.Server`/`Client` reference
  it; it references neither.
- Per-side systems subclass shared bases (`SharedXSystem` → `XSystem`); some shared systems run on both
  sides for prediction.
- Net message classes exist **only** in `Content.Shared` (26 total); each side registers handlers with
  `INetManager.RegisterNetMessage<T>(handler)`.
- Engine enforces direction (`NetMessageAccept`, MsgGroups); content sets `MsgGroup`/`DeliveryMethod`.

## Content network messages (most important)

| Message | Dir | Purpose | Handler |
|---|---|---|---|
| `MsgChatMessage` / `MsgDeleteChatMessagesBy` | S→C | Chat delivery/deletion | `ChatUIController.cs:193-194,831,981` |
| `MsgPreferencesAndSettings` | S→C | Preferences + GameSettings | `ClientPreferencesManager.cs:30,121` |
| `MsgSelectCharacter` / `MsgUpdateCharacter` / `MsgDeleteCharacter` / `MsgUpdateConstructionFavorites` | C→S | Profile edits | `ServerPreferencesManager.cs:48-53` |
| `MsgUpdateConsent` | C↔S | Consent sync | `ServerConsentManager.cs:34-124`, `ClientConsentManager.cs:19-39` |
| `MsgPlayTime`, `MsgRoleBans`, `MsgJobWhitelist`, `MsgWhitelist` | S→C | Restrictions/requirements | `JobRequirementsManager.cs:41-87` |
| `MsgUpdateAdminStatus` | S→C | Admin flags/commands | `ClientAdminManager.cs:77-86` |
| `MsgVoteData` / `MsgVoteCanCall` / `MsgVoteMenu` | S↔C | Voting | `VoteManager.cs` (server :56-306, client :67-211) |
| `SendRulesInformationMessage` / `RulesAcceptedMessage` | S→C / C→S | Rules handshake | `RulesManager.cs`, `InfoUIController.cs` |
| `BwoinkTextMessage` (+ relay/typing) | C↔S | AHelp | server `BwoinkSystem.cs`, client `AHelpUIController.cs` |
| `MsgEuiCtl` / `MsgEuiState` / `MsgEuiMessage` | S↔C | EUI lifecycle | `EuiManager.cs` both sides |
| `MsgGhostKick` | S→C | Fake network loss kick | `GhostKickManager.cs` both sides |
| `MappingSaveMapMessage` / `MappingMapDataMessage` / `MappingSaveMapErrorMessage` | C→S / S→C | Map editing | `MappingManager.cs` both sides |

Networked **events** (not NetMessage): ticker events listed in `.ai/EVENTS.md` §5,
`RequestCharacterInfoEvent`/`CharacterInfoEvent` (`Content.Shared/Examine/MsgCharacterInfo.cs`),
construction messages (`Content.Shared/Construction/Events.cs`), shuttle console/dock/emergency messages
(`Content.Shared/Shuttles/Events/`), `CustomObjectiveClientSetObjective` (`_DV`).

## Component replication

- Preferred: `[NetworkedComponent]` + `[AutoGenerateComponentState]` + `[AutoNetworkedField]`
  (400+/200+ uses). Generator emits state classes and handlers; supports field deltas and
  `raiseAfterAutoHandleState`.
- Manual handlers remain for special cases: DoAfter, gun battery/revolver, vending machines, absorbent,
  tray scanner, decals, handheld light, access reader, stacks; server-only visuals (explosion, singularity,
  shuttle pilot).
- Entity refs use `NetEntity`; positions `NetCoordinates`; component state gated by
  `ComponentGetStateAttemptEvent`.

## PVS / visibility

- Content uses `SharedPvsOverrideSystem` for forced visibility (mind entity of joining player,
  singularity, explosives, EMP, clock, holopad, ore silo, item recall, etc.).
- `SessionSpecific` components (Follower, RoleCodeword, Revolutionary) and `SendOnlyToOwner`
  (Pilot, Alerts, OwnInteractionVerbs, `_Floof` CustomExamine) hide data.
- Filters: `Filter.Pvs(...)`, `Filter.Broadcast()`.
- No content `GetPVSInterest` in this engine revision.

## Connection / auth handshake

Client (content):
1. Launcher args parsed by engine (`--launcher`, `--connect-address`, `--ss14-address`).
2. `LauncherConnecting` state (`:29-126`) calls `_baseClient.ConnectToServer` / `Redial`; UI in
   `LauncherConnectingGui.xaml.cs`; `ExtendedDisconnectInformationManager` shows reasons.
3. Direct connect from `MainMenu.cs:125`.
4. Engine handshake: `MsgLoginStart`/encryption/`MsgLoginSuccess`; auth server check
   (`auth.mode` Optional/Required/Disabled; default auth server `auth.spacestation14.com`).

Server (content):
1. `ConnectionManager.Initialize` hooks `NetMgr.Connecting`, assigns UserId callback, player status.
2. `ShouldDeny` order: HWID requirement → DB bans → panic bunker → soft player cap → whitelist
   prototypes → IPIntel → **NF multi-server `MiniAuthManager`** (unless `nf.allow_multi_connect`;
   fail-open on HTTP error; 10 s timeout).
3. `GameTicker.Player` reacts to status changes; per-connection data pushed: rules, preferences,
   playtime, whitelist, role bans, consent.
4. Guest IDs persisted only if `game.persist_guests`.

## HTTP APIs

| Endpoint | Implemented by | Data |
|---|---|---|
| `/status` | engine `StatusHost` + `GameTicker.StatusShell.cs:32-59` | name, map, round_id, players, soft cap, panic bunker, run level, preset, start time |
| `/info` | engine + `ServerInfoManager.cs:29-44` | connect address, auth mode/public key, build, desc, info links |
| `/admin/info` | `ServerApi.cs:493-558` | RoundId, Players (UserId/Name/IsAdmin/IsDeadminned), GameRules, GamePreset, Map, MOTD, PanicBunker — also consumed by `MiniAuthManager` |
| `/admin/game_rules`, `/admin/presets` | `ServerApi.cs:67-88` | rules/preset lists |
| `/admin/actions/...` | `ServerApi.cs` | start/end/restart round, kick, add/end rule, force preset, set MOTD, send bwoink (Frontier), panic bunker PATCH |
| `/teapot`, ACZ/manifest | engine `StatusHost` | manifest/ACZ build packaging |

Auth: `Authorization: SS14Token <admin_api_token>` with fixed-time compare (`ServerApi.cs:562-612`);
actor-based handlers additionally require a JSON `Actor` header.

## Discord integration (NetCord)

- `DiscordLink` (`Content.Server/Discord/DiscordLink/DiscordLink.cs`): bot gateway with
  Guilds/GuildUsers/GuildMessages/MessageContent/DMs intents; command/message events; send proxy.
- `DiscordChatLink`: Discord↔game OOC/admin chat bridge.
- Webhooks: `DiscordWebhook.cs`, `VoteWebhooks.cs`, watchlist webhook, AHelp relay
  (`BwoinkDiscordRelayUpdated`, `/admin/actions/send_bwoink`).
- **No Discord account/auth linking** exists in content.

## Replay networking

- `GameTicker.Replays.cs` starts/stops engine `IReplayRecordingManager` per round; metadata injected
  on stop; zips moved into `replays/`.
- Networked events are recorded when raised through filters/replay-enabled calls; PVS states are
  recorded by the engine.
- Playback: `Content.Replay` executable; client `ContentReplayPlaybackManager` + `ReplaySpectatorSystem`.

## Dependencies

Engine NetManager/PVS/serialization, `Content.Shared` messages, `IServerDbManager` (bans/logs),
`MiniAuthManager` HTTP, `BanManager`, `AdminManager` (flags), NetCord.

## Depended on by

Everything player-facing: chat, preferences, consent, admin tools, voting, mapping, BUI-driven UIs.

## Unknowns

- Production auth mode and admin API token are deployment-specific (not in repo).
- Whether external tooling consumes `map.json`/`/admin/*` beyond `MiniAuthManager` is unknown.
