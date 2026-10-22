# System: Chat & Communications

## Purpose

In-character chat, OOC/LOOC/deadchat, radio, speech/accents/emotes, announcements, admin AHelp
(bwoink) and the Discord bridge.

## Location

| Piece | Path |
|---|---|
| Server chat | `Content.Server/Chat/{Managers/ChatManager*.cs,Systems/ChatSystem*.cs,Commands/,ChatUser.cs}` |
| Shared chat | `Content.Shared/Chat/{SharedChatSystem.cs,ChatChannel.cs,ChatSelectChannel.cs,MsgChatMessage.cs,Prototypes/,Emotes?}` |
| Radio | `Content.Server/Radio/EntitySystems/{RadioSystem,HeadsetSystem,RadioDeviceSystem,JammerSystem}.cs`, `Content.Shared/Radio/...`, `Resources/Prototypes/radio_channels.yml`, `_NF/radio_channels.yml` |
| Speech/accents/emotes | `Content.Server/Speech/...`, `Content.Shared/Speech/...`, `Resources/Prototypes/{Voice,Accents}` |
| Bwoink | `Content.Shared/Administration/SharedBwoinkSystem.cs`, `Content.Server/Administration/Systems/BwoinkSystem.cs`, `Content.Client/UserInterface/Systems/Bwoink/*` |
| Announcements | `ChatSystem` `#region Announcements`, `Content.Server/Communications/CommunicationsConsoleSystem.cs`, `Content.Server/Announcements/*` |
| Client chat UI | `Content.Client/UserInterface/Systems/Chat/*`, `Content.Client/Chat/UI/SpeechBubble.cs` |
| Dormant Chat V2 | `Content.Server/Chat/V2/**`, `Content.Shared/Chat/V2/**` |

## Chat data flow

1. `ChatUIController.SendMessage` parses prefix/channel and delegates to client `ChatManager`, which maps
   channels to console commands (`say`, `whisper`, `me`, `subtle`, `ooc`, `looc`, `subtlelooc`, `dsay`,
   `asay`, ...).
2. Server commands in `Content.Server/Chat/Commands/*` execute with permission attributes.
3. `ChatSystem.TrySendInGameICMessage` (`:190`): ghost reroute to deadchat, rate limit, action-block
   checks, sanitization (`SanitizeInGameICMessage :1097` with accent word replacement and emote shorthands).
4. Routing: emote → `SendEntityEmote` (`:732`); radio prefix →
   `SharedChatSystem.TryProccessRadioMessage` (`:129`) → `RadioSystem`; else speak/whisper/emote/subtle.
   OOC path via `TrySendInGameOOCMessage` (`:361`: LOOC, subtle LOOC, deadchat).
5. `ChatManager.ChatMessageToOne/ToMany/Filtered/ToAll` (`:365-438`) builds `ChatMessage`, sends
   `MsgChatMessage` (per session or filter), records to replay (admin chat gated by
   `CCVars.ReplayRecordAdminChat`).
6. Client `ChatUIController.OnChatMessage` applies name colors, highlights, mentions sounds, tabs/filters,
   then `ChatBox` renders and `SpeechBubble` displays overhead text.

Message length: server `chat.max_message_length` (10,000); rate limits in `ChatManager.RateLimit.cs`
(`chat.rate_limit_period` 2 s / `count` 10).

## Radio architecture

- Channels: `RadioChannelPrototype` (`keycode`, `frequency`, `color`, `longRange`, `showFrequency`,
  range-degradation fields); definitions in `radio_channels.yml` + `_NF/radio_channels.yml`
  (`d` traffic, `s` NFSD, `r` greeting).
- Send: `RadioSystem.SendRadioMessage` (`:116/132`) raises `RadioSendAttemptEvent` (cancellable),
  builds chat message, iterates `ActiveRadioComponent` receivers; telecom servers
  (`TelecomServerComponent` + key holders) gate transmission unless exempt.
- Receive: intrinsic radios, headsets (`HeadsetSystem`), intercoms/handhelds (`RadioDeviceSystem`);
  `RadioReceiveEvent`; range degradation mangles text/color (`RadioSystem.cs:314-462`).
- Encryption keys: `EncryptionKeySystem` unions channels from inserted keys into `EncryptionKeyHolderComponent`;
  headsets sync `ActiveRadioComponent`.
- Jammers cancel sends (`JammerSystem`).
- `_CS` radio static: `Content.Shared/_CS/RadioNoises/RadioStaticSystem.cs` (KSHHT sounds, squelch/volume verbs).
- `_NF` shuttle intercom appends ship name (`_NF/RadioEntity/Systems/ShuttleIntercomSystem.cs`).

## Speech / accents / emotes

- `TransformSpeechEvent` → `AccentSystem` → accent systems mutate text (slurred, stuttered, pirate,
  French, lizard, mobster, southern, Russian, OwO, monkey, bleating, `_NF` caveman/goblin/etc.).
- Word replacements via `ReplacementAccentPrototype` (`chatsanitize`).
- `SpeechVerbPrototype` controls verb/bold/font/size; equipment can override name/verb
  (`TransformSpeakerNameEvent`, voice mask).
- Speech sounds (`SpeechSoundSystem`) use `SpeechSoundsPrototype` (say/ask/exclaim, pitch, 0.5 s cooldown).
- Emotes: `EmotePrototype` (triggers, messages, category, whitelist); `ChatSystem.Emote.cs` caches word
  triggers and raises `EmoteEvent`; radial menu via `EmotesMenuSystem`; auto-emotes, damage emotes,
  spawn announcements, muting, vocal sounds.
- `_CS` custom emotes/sounds in `Resources/Prototypes/_CS/emotes_but_cooler.yml` etc.

## Announcements

- No `AnnouncementSystem`; logic is in `ChatSystem.DispatchGlobalAnnouncement` /
  `DispatchFilteredAnnouncement` / `DispatchStationAnnouncement` (`:429-528`), plus
  `ChatManager.DispatchServerAnnouncement`.
- Comms console `CommunicationsConsoleSystem.OnAnnounceMessage` (`:231-280`) with length limit
  (`chat.max_announcement_length` 256), cooldown, station/global split and
  `CommunicationConsoleAnnouncementEvent`.
- Alert levels, station events, round announcements (`RoundAnnouncementPrototype`), admin announce EUI
  (`AdminAnnounceEui` / `announce` command).

## AHelp / Bwoink

- Network: `BwoinkTextMessage` + `BwoinkDiscordRelayUpdated`, `BwoinkClientTypingUpdated`,
  `BwoinkPlayerTypingUpdated`.
- Server `BwoinkSystem` (`:645-847`): auth via personal channel or `AdminFlags.Adminhelp`, rate limits,
  formatted echo to admins/player, hidden admin-only segments, echoes to `ChatChannel.AdminChat`,
  "starmute" when no admins; optional Discord webhook queue with embed payload generation.
- Client `AHelpUIController`/`BwoinkWindow` manage sounds, typing indicators, relay status.
- `asay` is a separate admin chat channel via `AdminChatCommand` → `ChatManager.SendAdminChat`.

## Dormant components (do not assume active)

- `ChatRepositorySystem` (Chat V2): IDs/delete/nuke, no callers in live chat.
- `SimpleCensor`/`RegexCensor`/`ChatCensor`: only referenced by tests.
- No `ahelp` console command; AHelp is opened from the game menu bar.

## Dependencies

Engine network/UI/audio, `ChatSanitizationManager`, `RadioSystem`, consent? (no), Discord webhooks,
admin permissions, replay recording.

## Depended on by

Stations events, alert levels, communications console, emergency shuttle, nuke ops, criminal records,
bounty contracts (`_NF`), guidebook? (no), and every gameplay system that speaks.

## Unknowns

- Whether Chat V2 is a future refactor or abandoned.
- Locale keys for wrapped messages were not enumerated.
