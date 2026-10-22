# System: New Frontier (`_NF`) Systems

## Purpose

The parent fork's gameplay layer: Frontier replaces station round gameplay with a hub-and-space
economy. `_NF` is by far the largest modular folder and defines the live rounds.

## Location

`Content.Server/_NF`, `Content.Shared/_NF`, `Content.Client/_NF`,
`Resources/Prototypes/_NF`, `Resources/Maps/_NF`, `Resources/Locale/en-US/_NF`.

Namespace convention: `Content.<Side>._NF.<Area>`. Some `_NF` server files deliberately use upstream
namespaces to extend core partials (GameTicker, ShuttleSystem, SalvageSystem).

## Major systems

| System | Path | Purpose |
|---|---|---|
| Bank | `_NF/Bank/BankSystem.cs:14` (+ `.Ledger`, `.Sector`), `ATMSystem`, `SectorBankComponent` | Per-character bank accounts persisted in preferences; sector accounts accruing income; ledger; ATMs; market modifiers |
| Market | `_NF/Market/Systems/MarketSystem.cs:14` (+ crate machine/console) | Market consoles, buy/sell UI, crate machines |
| Shipyard | `_NF/Shipyard/Systems/ShipyardSystem.cs:31` (+ consoles) | Purchase/sell ships, spawn vessel maps, pricing/conditions |
| Ship deeds | `ShuttleDeedSystem.cs:7`, `ShuttleDeedComponent` | Ownership records on grids; shown on examine |
| Bluespace drydock | `BluespaceDrydockSystem.cs:38` | Store/retrieve purchased shuttles, async map (de)serialization |
| Shuttle records | `_NF/ShuttleRecords/ShuttleRecordsSystem.cs:19` | Sector-wide shuttle registry/console |
| Cargo | `_NF/Cargo/Systems/NFCargoSystem.cs:27` (+ Orders/Pallet/Telepad/TradeCrates/PirateBounty) | Frontier cargo: station order DB, pallet currencies, telepad delivery, trade crates |
| Bounties | `_NF/BountyContracts/BountyContractSystem.cs:21` | Player-created bounty contracts paid via bank |
| Smuggling/dead drops | `_NF/Smuggling/DeadDropSystem.cs:34`, `_NF/Contraband/ContrabandTurnInSystem.cs:25`, `_NF/Trade/*` | Dead-drop notes, smuggling pods, contraband turn-in |
| Public transit | `_NF/PublicTransit/PublicTransitSystem.cs:43`, `PublicTransitRoutePrototype` | Scheduled transit shuttle between POIs |
| Sector services | `_NF/SectorServices/SectorServiceSystem.cs:14`, `Resources/Prototypes/_NF/SectorServices/services.yml` | Single sector host entity holding bank/mail/records/alerts/dead-drops |
| Sectors / POIs / worldgen | `_NF/GameRule/PointOfInterestSystem.cs:21`, `NfAdventureRuleSystem.cs:33`, `_NF/Worldgen/**`, `Resources/Prototypes/_NF/{PointsOfInterest,Procedural,World}` | POI placement, worldgen carvers/debris, sector events, `StationsGeneratedEvent`, round-end summary, `NFPirate` fallback preset |
| Salvage/expeditions | `_NF/Salvage/**`; core patched `Content.Server/Salvage/SalvageSystem.Expeditions.cs` | Mob restrictions, expedition objectives/NPC spawners, NF CVars |
| Shuttles/FTL | `_NF/Shuttles/Systems/ShuttleSystem.cs:34`, `ForceAnchorSystem`, `ForceDampeningSystem`, `ShuttleFTLComponent` | FTL allow/deny, force anchor/dampening, FTL knockdown immunity |
| Cryo/respawn | `_NF/CryoSleep/CryoSleepSystem.cs:39` (+ `.Returning`, EUI) | Cryo storage, return from cryo, respawn timers (`nf14.respawn.*`, `nf14.uncryo.*`) |
| Pirates | `_NF/Pirate/Systems/NFPirateSystem.cs:9`, `_NF/GameTicking/Rules/NFPirateRuleSystem.cs:12` | Auto-pirate crews, sector bounty DB; default `nfpirate` preset |
| Round support | `_NF/RoundNotifications`, `_NF/Mail`, `_NF/Research/RandomBlueprintSystem`, `_NF/Lathe/BlueprintLatheSystem`, `_NF/Roles/{JobTracking,InterviewHologram}`, `_NF/Players/GhostRole/GhostRoleWhitelistSystem` | Notifications, sector mail, random blueprints, job tracking, ghost-role whitelist |
| Auth | `_NF/Auth/Auth.cs:12` (`MiniAuthManager`) | Multi-server account check via `/admin/info` |
| Misc | `_NF/Botany/PlantAnalyzer`, `_NF/EmpGenerator`, `_NF/Skrungler`, `_NF/Mech`, `_NF/Station/StationRename*`, `_NF/Tools/DisableToolUseSystem`, `_NF/Solar`, `_NF/Power/UpgradePowerSystem`, `_NF/Atmos/GasDepositSystem` | Various feature ports |

Client UIs: `Content.Client/_NF/{Bank,Shipyard,ShuttleRecords,Market,Cargo,PublicTransit,CryoSleep,Radar,Salvage,Trade,Store,...}`.

## Live round configuration

- Presets: `Resources/Prototypes/_NF/game_presets.yml` — `NFAdventure`, `NFPirate`, `NFTest`.
- Rules: `NFAdventure` (sector generation), `NFBasicStationEventScheduler`, `BluespaceEventScheduler`,
  `BluespaceDungeonEventScheduler`, `BluespaceSalvageEventScheduler`, `SmugglingEventScheduler`,
  `NFRoundstartVariation`.
- Defaults (`CCVars.Game.cs`): `game.defaultpreset = nfpirate`, `game.map = Frontier`,
  `game.map_pool = NFMapPool`.
- Live station: `StandardFrontierStation` (`Resources/Prototypes/_NF/Entities/Stations/nanotrasen.yml:3`)
  uses `NFBaseStationCargo` + `BaseStationCargoMarket`.

## CVars

`Content.Shared/_NF/CCVar/NFCCVars.cs` (prefix `nf14.*`), plus `nf.allow_multi_connect`,
`nf.server_auth_list`, `nf14.publictransit.enabled`, expedition cooldowns/travel/proximity limits.

## Data owned / modified

- Bank balances (on character profiles), sector accounts (entities), market data, shuttle records/deeds,
  sector shuttle registry, cargo orders, bounty contracts, sector services state, POI/worldgen data,
  pirate bounty DB.
- Persisted: bank balance + selected character; other state resets per round unless stored on entities
  or preferences.

## Events produced / consumed

- Produced: `StationsGeneratedEvent`, shuttle/BUI events, round notifications, market/shipyard UI events.
- Consumed: `RoundStartedEvent` (server-only), `RoundRestartCleanupEvent`, `PlayerSpawnCompleteEvent`
  (`NfAdventureRuleSystem`), salvage events, `RulePlayerSpawningEvent` (pirate antags), CVars.

## Dependencies

Core: GameTicker, Station, Shuttles, Salvage, Cargo, Preferences/DB, Mind/Roles, economy bank;
engine: maps/transform/physics/serialization (drydock), network/BUI.

## Depended on by

`_CS` RPI economy (direct bank dependency), station events, public transit, cargo economy, default round
presets, integration tests (`_NF/ShipyardTests`).

## Unknowns

- Which of the many `_NF` systems are reachable per preset/config; no exhaustive reachability audit.
- Production values for `nf.*` CVars.
