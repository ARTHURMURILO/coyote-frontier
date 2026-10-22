# DATA FLOW

Flows below are derived from source. Where engine code performs the mechanic it is prefixed `[engine]`.

## 1. Round / map lifecycle flow

```
GameTicker.RestartRound / StartRound                     Content.Server/GameTicking/GameTicker.RoundFlow.cs
  ├─ [engine] MapLoaderSystem.TryLoadMap(gameMap)
  │     └─ MapMigrationSystem applies migration.yml + nf_migration.yml on BeforeEntityReadEvent
  ├─ map init → MapInitEvent for each entity → components start
  ├─ RoundStartingEvent (int Id)
  ├─ GamePreset → RoundStartAttemptEvent (cancellable; min players)
  ├─ rule entities spawned (GameRuleAddedEvent) and started (GameRuleStartedEvent)
  ├─ RulePlayerSpawningEvent (rules may claim players)
  ├─ StationJobsSystem.AssignJobs + AssignOverflowJobs
  ├─ per player: PlayerBeforeSpawnEvent (Handled = skip ticker spawn)
  │     └─ SpawnPlayer → mind create → StationSpawningSystem.SpawnPlayerMob
  │           ├─ HumanoidAppearanceSystem.LoadProfile (species, markings, height/width)
  │           ├─ loadout loop (NF bank affordability) + job StartingGear
  │           └─ StartingGearEquippedEvent
  ├─ PlayerSpawnCompleteEvent (directed at mob + broadcast)
  ├─ RulePlayerJobsAssignedEvent
  └─ RoundStartedEvent (NF extension, server-only)
...
EndRound → RoundEndTextAppendEvent → GameRunLevelChangedEvent(PostRound)
        → Timer → RoundRestartCleanupEvent (broadcast local + network) → PreRoundLobby
```

## 2. Player connect → spawn flow

```
Client                                    Server
──────                                    ──────
launcher/main menu ConnectToServer   ──►  BaseServer handshake [engine: auth.mode, auth server]
                                          PlayerManager.NewSession [engine]
                                          ConnectionManager.NetMgrOnConnecting
                                             ├─ DB bans / panic bunker / whitelist / IPIntel
                                             └─ MiniAuthManager (multi-server dAuth, fail-open)
                                          GameTicker.Player.Connected
                                             ├─ ContentPlayerData created
                                             └─ TickerConnectionStatusEvent
                                          UserDbDataManager.Load (parallel, per user)
                                             ├─ preferences  → MsgPreferencesAndSettings
                                             ├─ playtime     → MsgPlayTime
                                             ├─ job whitelist→ MsgJobWhitelist/MsgWhitelist
                                             ├─ role bans    → MsgRoleBans
                                             └─ consent      → MsgUpdateConsent (S→C)
Client lobby: select/update character ──► ServerPreferencesManager
   (MsgSelectCharacter/MsgUpdateCharacter/MsgDeleteCharacter)
                                          profile.EnsureValid + Frontier bank clamp
                                          SaveCharacterSlotAsync → DB profile row
                                          ReloadCharacterConsent
Ready / round start                   ──► GameTicker.SpawnPlayers → SpawnPlayer
                                          (see flow 1)
```

## 3. Preferences / character profile data

```
Client HumanoidProfileEditor
  → LobbyUIController.SaveProfile
  → ClientPreferencesManager (local cache)
  → MsgUpdateCharacter (NetMessage, Command group)
  → ServerPreferencesManager.SetProfile
      → HumanoidCharacterProfile.EnsureValid  (name/age/species/markings/loadout/trait/bank clamps)
      → IServerDbManager.SaveCharacterSlotAsync
          → ServerDbBase.ConvertProfiles(profile, slot)
              ├─ scalar columns (name, species, height, width, leg style...)
              ├─ markings = JSON array (jsonb on Postgres)
              ├─ child tables: jobs, antags, traits, loadouts (groups/items)
              └─ CharacterConsentFreetext
  → MsgPreferencesAndSettings sent back on load
```

Profile import/export (`HumanoidProfileExport`) is YAML via `SharedHumanoidAppearanceSystem.ToDataNode/FromStream`
and includes consent freetext/toggles; importing resets bank balance handling on the client.

## 4. Chat / radio flow

```
[client] ChatUIController.SendMessage
   → ChatManager (client) maps channel to command (say/whisper/me/ooc/...)
   → command executes locally in client console [engine] → command sent to server? no:
     IC messages are sent as console commands executed on server side via input/console pathway
[server] ChatSystem.TrySendInGameICMessage
   ├─ ghost reroute to deadchat
   ├─ rate limit (chat.rate_limit_period/count)
   ├─ SanitizeInGameICMessage (accents, emote shorthands, capitalization)
   ├─ radio prefix? → RadioSystem.SendRadioMessage (encryption keys, telecom, range degradation)
   └─ Speak/Whisper/Emote/Subtle
        → ChatManager.ChatMessageToMany/Filter
             → MsgChatMessage (ReliableOrdered, Chat group)
[client] ChatUIController.OnChatMessage → filters/tabs → ChatBox → speech bubbles
```

Announcements reuse `ChatSystem.DispatchGlobalAnnouncement`/`DispatchStationAnnouncement`
(no dedicated announcement system class). AHelp (`BwoinkTextMessage`) is bidirectional; server also
echoes to `ChatChannel.AdminChat` and optionally posts a Discord webhook.

## 5. Damage / medical flow

```
attacker/weapon → SharedMeleeWeaponSystem / GunSystem / projectile
   → DamageableSystem.TryChangeDamage
        ├─ armor via IInventoryRelayEvent (SharedArmorSystem)
        ├─ DamageModifierSet from species/armor
        └─ DamageableComponent.TotalDamage updated → appearance/alerts
   → MobThresholdSystem checks → MobState (Alive/Critical/Dead)
   → SharedStunSystem / StaminaSystem / bloodstream
   → medical items (healing system) reverse via DamageSpecifier negation
   → metabolism: Stomach → MetabolizerSystem → EntityEffectSystem applies reagent effects
```

## 6. Chemistry / solution flow

```
reagent dispenser / chem master / injector UI (BUI)
   → SharedSolutionContainerSystem (SolutionComponent, SolutionContainerManager)
   → ChemicalReactionSystem (on mix, temperature, tile reaction) → reactions prototype
   → ReactiveSystem (per-entity reactive groups, e.g. spill)
   → ingestion/injection → MetabolizerSystem (stomach/bloodstream)
   → EntityEffectSystem (metabolism effects) → damage/status/consent conditions
```

## 7. UI / BUI data flow (content ↔ client)

```
server: SetUiState(entity, key, state)          [engine SharedUserInterfaceSystem:727]
   → dirty States field → PVS/game state [engine]
client: OnUserInterfaceHandleState → BUI.UpdateState(state) / BUI.Update<T>(...)
client: bui.SendPredictedMessage / SendMessage
   → BoundUIWrapMessage → server validates actor + input → local event / server BUI handler
EUI (admin/global): MsgEuiCtl / MsgEuiState / MsgEuiMessage
```

## 8. Frontier economy flow (`_NF`)

```
preferences (BankBalance per character) ──► BankSystem accounts on spawn
ATM console (BUI) ──► deposit/withdraw ──► BankSystem.ModifyBalance
        └─ persists to preferences (character) / sector account entity
Market console ──► MarketSystem (MarketData) ──► NFCargoSystem orders
Shipyard console ──► ShipyardSystem.PurchaseShuttle
        ├─ vessel prototype price/map id
        ├─ spawn map via MapLoader (grid) [engine]
        └─ ShuttleDeedComponent written on grid + records
      sell/retrieve → BluespaceDrydockSystem (serialize/deserialize vessel grid)
BountyContractSystem → payout through BankSystem
_CS RoleplayIncentiveSystem → paywards (bank) with tax brackets
```

## 9. Consent flow

```
client ConsentWindow → MsgUpdateConsent (replicated, Command group)
server ServerConsentManager.HandleUpdateConsentMessage
   ├─ EnsureValid (limits, drops unknown/off toggles)
   ├─ admin log (LogType.Consent, Medium)
   ├─ SavePlayerConsentSettingsAsync → consent_settings + consent_toggle (+ profile freetext)
   └─ echo MsgUpdateConsent back
queries: ConsentSystem.HasConsent(entity, toggle)
   └─ mind → ContentPlayerData.UserId → ServerConsentManager cache
      mindless/NPC = true; unknown = false; client SharedConsentSystem = false
consumers: VoreSystem (Vore/Digestion), ModifyUndiesSystem (GenitalMarkings),
           SizeManipulationSystem (SizeManipulation), TransformationToolSystem (Transformation),
           CustomExamineSystem (NSFWDescriptions), ScentSystem (CanSmellLewdScents),
           ConsentCondition + AphroLacedVisibility (Aphrodisiacs/AphrodisiacsVisibility)
```

## 10. Map / worldgen / POI flow (`_NF`)

```
GameTicker map load
  └─ NfAdventureRuleSystem (rule) 
       ├─ PointOfInterestSystem places POI prototypes on sector map (worldgen carvers/debris)
       ├─ raises StationsGeneratedEvent / sector events
       └─ round-end summary ("capitalism" report)
PublicTransitSystem moves transit shuttle between POIs on a schedule
Salvage/Expeditions: SalvageSystem.Expeditions.cs (core, NF-patched) + _NF components/spawners
```

## 11. Persistence flows (summary)

| Data | Write path | Read path |
|---|---|---|
| Character profiles | `ServerPreferencesManager` → `ServerDbBase.SaveCharacterSlotAsync` | `GetPlayerPreferencesAsync` on connect |
| Consent | `ServerConsentManager` → DB (`consent_settings/toggle`, profile freetext) | `LoadData` via `UserDbDataManager` |
| Playtime | `PlayTimeTrackingManager.Save/DoSaveAsync` (interval 900 s, disconnect) | `LoadData` on connect |
| Bans | `BanManager` → DB; Postgres NOTIFY trigger propagates | `ShouldDeny` at connect; `BanMatcher` for SQLite |
| Admin logs | `AdminLogManager` batched queues → DB; cache last 3 rounds | console/UI queries; dropped past thresholds |
| Whitelists | admin UI/commands → DB | `JobWhitelistManager` |
| Replays | engine recorder driven by `GameTicker.Replays.cs` → `replays/*.zip` | `Content.Replay` / client replay loader |
| Server maps (mapping) | `MappingManager` autosave to user data | map save commands / editor |
| Uploaded resources | `ContentNetworkResourceManager` → DB byte arrays | admin resource serving |

## 12. Replay flow

```
round start (replay.auto_record) → engine IReplayRecordingManager starts
  ├─ every game state sent to clients is recorded
  ├─ RaiseNetworkEvent(recordReplay:) records events (e.g. popups, global sounds)
  ├─ metadata injected on stop (map, gamemode, round id...)
  └─ zip moved into replays/
client: Content.Replay executable or bundle replay → IReplayLoadManager → ReplayPlaybackManager
```
