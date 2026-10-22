# PERSISTENCE AND STATE

## Overview

| Mechanism | Technology | Location | Lifecycle |
|---|---|---|---|
| Player/account data | EF Core (SQLite default, Postgres optional) | `data/preferences.db` or PG server | auto-migrated at startup; per-connection loads |
| Admin logs | EF Core, batched queues | DB only | flushed every N seconds/cap; dropped past thresholds |
| Replays | zip archives | `<userdata>/replays/` | per round (auto-record optional) |
| Maps (mapping/persistence) | YAML map format 7 | user data | on save/autosave |
| Uploaded client resources | EF Core byte arrays | `uploaded_resource_log` | admin upload, purge by age |
| Server config | TOML + env + CLI | `server_config.toml`, `Resources/ConfigPresets/*.toml` | startup |
| Client config/prefs cache | TOML/user data | client user dir | startup/runtime |
| Prototype migration | YAML | `Resources/migration.yml`, `nf_migration.yml` | applied on map load |
| Changelog read state | user data | client user dir | on changelog open |

## Database technology

- EF Core 9 with two providers selected by CVar `database.engine` (`sqlite` default | `postgres`)
  (`Content.Server/Database/ServerDbManager.cs:448-463`, `Content.Shared/CCVar/CCVars.Database.cs`).
- Packages in `Content.Server.Database/Content.Server.Database.csproj`.
- Default SQLite path: `database.sqlite_dbpath` = `preferences.db`, resolved under server user data
  (`data/`), `ServerDbManager.cs:1231-1238`. Integration tests use in-memory SQLite.
- Concurrency: all commands via `RunDbCommand` (`ServerDbManager.cs:1149-1194`); SQLite semaphore
  (`ServerDbSqlite.cs:596-657`), Postgres `SemaphoreSlim` (`ServerDbPostgres.cs:40`); `database.sync`
  forces single-threaded mode (tests).
- Notifications: Postgres LISTEN/NOTIFY for multi-server ban propagation and `DatabaseNotification`
  (`ServerDbPostgres.Notifications.cs`, `ServerDbManagerExt.cs`).
- Prometheus metrics `db_read_ops`, `db_write_ops`, `db_executing_ops` (`ServerDbManager.cs:409-419`).

## Schema areas (`Content.Server.Database/Model.cs`)

| Area | Tables/entities | Notes |
|---|---|---|
| Preferences | `Preference` (`:395`) → `Profile` (`:410`) | one row per character slot; scalar cols + JSON markings |
| Profile children | `Job`, `Antag`, `Trait`, `ProfileRoleLoadout` → `ProfileLoadoutGroup` → `ProfileLoadout` | job/antag/trait priorities, loadouts |
| Consent | `ConsentSettings` (`:450`), `ConsentToggle` (`:459`) | account-level; character freetext on `Profile.CharacterConsentFreetext` |
| Players | `Player` (`:601`), `AssignedUserId` (`:593`) | first/last seen, username, address, HWID |
| Bans | `ServerBan` (`:835`), `ServerUnban` (`:934`), `ServerBanExemption` (`:969`), `ServerRoleBan`/`ServerRoleUnban` (`:1048-1090`) | |
| Moderation | `Admin`, `AdminFlag`, `AdminRank`, `AdminRankFlag` (`:656-702`) | ranks/flags |
| Logs/records | `Round` (`:704`), `Server` (`:719`), `AdminLog` (`:733`), `AdminLogPlayer` (`:756`), `ConnectionLog` (`:985`), `ConnectionDenyReason` (`:1017`) | admin log FTS on Postgres |
| Notes | `AdminNote`/`Watchlist`/`Message` (`:1149-1253`), `BanTemplate` (`:1274`) | |
| Playtime | `PlayTime` (`:1092`) | per tracker |
| Whitelists | `Whitelist` (`:641`), `Blacklist` (`:650`), `RoleWhitelist` (`:1255`) | |
| Misc | `UploadedResourceLog` (`:1106`), `TypedHwid` owned (`:1323`), `IPIntelCache` (`:1356`) | |

`Content.Shared.Database` holds only enums: `LogType` (**numeric values are load-bearing; do not change**),
`LogImpact`, `NoteSeverity`, `NoteType`, `TypedHwid`.

## Migrations

- Two migration sets: `Content.Server.Database/Migrations/Postgres` and `/Sqlite` (59 each + model snapshots).
- Applied automatically at startup: SQLite `prefsCtx.Database.Migrate()` (`ServerDbSqlite.cs:50-63`),
  Postgres `MigrateAsync` in background (`ServerDbPostgres.cs:42-53`); all DB calls await `_dbReadyTask`.
- New migrations: `Content.Server.Database/add-migration.sh` / `.ps1` (runs `dotnet ef migrations add`
  for both contexts). Design-time factories in `DesignTimeContextFactories.cs` (`#if TOOLS`).
- Postgres-specific: `ban_notify_trigger` PL/pgSQL trigger (`20240606121555`), snake_case naming
  (`SnakeCaseNaming.cs`), `timestamp with time zone`, `jsonb`, GIN FTS on admin logs, IPv6 checks.
- SQLite-specific: value converters for IP/JSON (`ModelSqlite.cs:44-88`), manual admin-note ID
  assignment (`ServerDbSqlite.cs:493-538`).
- Drift check: `ServerDbBase.HasPendingModelChanges` used by
  `Content.IntegrationTests/Tests/Preferences/ServerDbSqliteTests.cs:474`.

## Read/write paths per data type

### Character profiles / preferences
- Read: `ServerDbBase.GetPlayerPreferencesAsync` (`:45-78`) → `ConvertProfiles` (`:210`);
  manager `ServerPreferencesManager` cache (`:38`), load at `:254-315`.
- Write: `SaveCharacterSlotAsync` (`:89`), `SaveSelectedCharacterIndexAsync` (`:80`),
  `DeleteSlotAndSetSelectedIndex` (`:168`); called from `ServerPreferencesManager.SetProfile` etc.
- Guests without static user IDs are not stored (`ShouldStorePrefs`).

### Consent
- Read: `ServerDbBase.GetPlayerConsentSettings` (`:1387-1429`); character freetext from profile.
- Write: `SavePlayerConsentSettingsAsync` (`:1320-1385`) — upserts account row + toggle diffs and the
  active character's freetext.
- Migration `20250308222529_FloofConsentSystem` (PG), `20251130190030_CharacterConsentFreetext`.

### Playtime
- Read: `GetPlayTimes` (`:634`) via `PlayTimeTrackingManager.LoadData`.
- Write: `UpdatePlayTimes` (`:643-682`); manager save interval `playtime.save_interval` = 900 s;
  session save on disconnect.

### Bans / whitelists / notes
- Write: `BanManager.CreateServerBan/CreateRoleBan/PardonRoleBan`; notes via `AdminNotesManager`.
- Read: `ConnectionManager.ShouldDeny` at connect; `JobWhitelistManager` for job/ghost-role whitelists.
- SQLite loads all bans into memory and matches with `Content.Server/Database/BanMatcher.cs`;
  Postgres queries directly.

### Admin logs
- `Content.Server/Administration/Logs/AdminLogManager.cs`: concurrent queues, batched flush
  (`SaveLogs :200-256`), retry/backoff; CVars in `CCVars.Admin.Logs.cs`
  (`adminlogs.enabled`, `queue_send_delay_seconds`=5, `queue_max`=5000, `drop_threshold`=20000).
- In-memory cache of last 3 rounds: `AdminLogManager.Cache.cs`.
- Logs are **DB-only** — excess logs are dropped, no file fallback.

## File-based persistence

| Data | Path | Serialization |
|---|---|---|
| SQLite DB | `<userdata>/preferences.db` (default) | SQLite |
| Replays | `<userdata>/replays/*.zip` | engine replay format (states+messages+meta) |
| Maps | user data | engine map YAML format 7 |
| Mapping autosaves | user data | map YAML |
| `game.destination_file` dump | user data (`chem_*`, `react_*`) | JSON (content, one-shot) |
| Client changelog read state, screenshots, parallax cache | client user data | various |
| Client keybinds/config | `client_config.toml` + user data | TOML |

## Server configuration persistence

- Engine loads `server_config.toml`, environment (`ROBUST_CVARS`, `ROBUST_CVAR_<NAME>` with `__`→`.`) and
  CLI cvar overrides (engine `BaseServer.cs:195-205`).
- `Resources/ConfigPresets/*.toml` are loaded by `Content.Server/Entry/EntryPoint.cs:201-243`
  (`config.presets` CVar, plus `Build/development.toml` under `TOOLS` and `Build/debug.toml` under `DEBUG`).
- Known fork presets: `Resources/ConfigPresets/_CS/coyote_sector.toml` (hub/hostname),
  `_NF/frontier.toml` (lobby/votes/worldgen), `Build/development.toml` (lobby on, map `NFDev`).
- Production values are not in this repo (deployment-specific) — see `.ai/UNKNOWN.md`.

## Prototype/map migration persistence

- `Resources/migration.yml` and `nf_migration.yml` map old entity prototype IDs to new IDs (or `null`
  to delete). Applied by `Content.Server/Maps/MapMigrationSystem.cs:25` on `BeforeEntityReadEvent`;
  DEBUG builds validate targets (`:32-49`).
- `Resources/IgnoredPrototypes/ignoredPrototypes.yml` marks upstream prototypes abstract for the fork.

## Replays

- Server records automatically when `replay.auto_record`; driven from
  `Content.Server/GameTicking/GameTicker.Replays.cs` (start :31-77, stop :82-95, move+metadata :97-133).
- `SharedGameTicker` injects `roundId` into recording metadata.
- Playback: `Content.Replay` executable; client hooks `ContentReplayPlaybackManager`.
