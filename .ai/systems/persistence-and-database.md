# System: Persistence & Database

## Purpose

Persists all server-side player and moderation data using EF Core with SQLite (default) or PostgreSQL;
also covers file-based persistence (replays, maps) and prototype migrations.

## Location

| Piece | Path |
|---|---|
| EF model (all entities) | `Content.Server.Database/Model.cs` (+ `ModelSqlite.cs`, `ModelPostgres.cs`, `SnakeCaseNaming.cs`) |
| Migrations | `Content.Server.Database/Migrations/{Sqlite,Postgres}` (59 each) |
| DB manager / API | `Content.Server/Database/ServerDbManager.cs` (`IServerDbManager :30`), `ServerDbBase.cs`, `ServerDbSqlite.cs`, `ServerDbPostgres.cs`, `ServerDbPostgres.Notifications.cs` |
| Server DB helpers | `Content.Server/Database/{BanMatcher,ServerBanDef,...,UserDbDataManager,ServerDbEntryManager}.cs` |
| Shared enums | `Content.Shared.Database/{LogType,LogImpact,NoteSeverity,NoteType,TypedHwid}.cs` |
| Admin logs | `Content.Server/Administration/Logs/{AdminLogManager,AdminLogManager.Cache}.cs` |
| Migration tooling | `Content.Server.Database/add-migration.sh` / `.ps1`, `DesignTimeContextFactories.cs` |

## Database technology

- Provider selected by `database.engine` (`sqlite` | `postgres`), `ServerDbManager.cs:448-463`.
- Default SQLite path `preferences.db` under server user data; in-memory SQLite when no user-data root
  (integration tests).
- All calls serialized through `RunDbCommand`; per-provider semaphores; `database.sync` for tests.
- Postgres notifications (LISTEN/NOTIFY) for remote ban propagation; `DatabaseNotification` records.
- Prometheus metrics for DB ops.

## Main API surface (`IServerDbManager`, `ServerDbManager.cs:30-380`)

Groups: Preferences (:36-53), User IDs (:55-59), Bans (:61-130), Role bans (:132-169),
Playtime (:171-187), Player records (:189-197), Connection logs (:199-212), Admin ranks (:214-238),
Rounds (:240-246), Admin logs (:248-257), Consent (:259-266), Whitelist/Blacklist (:268-286),
Uploaded resources (:288-294), Rules (:296-301), Notes/watchlists/messages (:303-335),
Job/ghost-role whitelists (:337-350), IPIntel (:352-358), DB notifications (:360-379).

## Schema map (key tables)

| Table/entity | Model line | Used by |
|---|---|---|
| `Preference` → `Profile` (+ children Job/Antag/Trait/Loadouts) | `:395,410` | `ServerPreferencesManager` |
| `ConsentSettings`, `ConsentToggle` | `:450,459` | `ServerConsentManager` |
| `Player`, `AssignedUserId` | `:601,593` | `UserDbDataManager`, `PlayerLocator` |
| `ServerBan`/`ServerUnban`/`ServerBanExemption`, role bans | `:835,934,969,1048` | `BanManager`, `ConnectionManager` |
| `Admin*`, `AdminLog*`, `ConnectionLog`, `AdminNote/Watchlist/Message`, `BanTemplate` | `:656-1274` | admin systems |
| `PlayTime` | `:1092` | `PlayTimeTrackingManager` |
| `Whitelist`, `Blacklist`, `RoleWhitelist` | `:641,650,1255` | `ConnectionManager.Whitelist`, `JobWhitelistManager` |
| `Round`, `Server` | `:704,719` | `GameTicker`, `ServerDbEntryManager` |
| `UploadedResourceLog` | `:1106` | `ContentNetworkResourceManager` |
| `TypedHwid`, `IPIntelCache` | `:1323,1356` | HWID/bans, IPIntel |

## Read/write paths (summary — full flows in `.ai/PERSISTENCE.md`)

- Profiles: `ServerPreferencesManager` cache → `SaveCharacterSlotAsync` / `GetPlayerPreferencesAsync`
  → `ServerDbBase.ConvertProfiles` (JSON markings, child tables, consent freetext).
- Consent: `SavePlayerConsentSettingsAsync` / `GetPlayerConsentSettings`.
- Playtime: `UpdatePlayTimes` / `GetPlayTimes` (batched every 900 s + disconnect).
- Bans: `BanManager` → `AddServerBanAsync`; read via `ShouldDeny` (SQLite in-memory + `BanMatcher`,
  Postgres queries).
- Admin logs: queued in `AdminLogManager`, flushed in batches with retry; last 3 rounds cached.
- Connection logs + player records updated in `ConnectionManager.NetMgrOnConnecting`.
- Round records: `GameTicker.AddNewRound`, `AddRoundPlayers` (`player_round` join).

## Migrations

- Auto-applied at startup on both providers; DB calls wait on a readiness task.
- 59 migrations per provider; Postgres has a ban-notify trigger migration; SQLite has value converters
  and manual admin-note ID handling.
- `add-migration.sh` creates migrations for both contexts; design-time factories under `#if TOOLS`.
- Model-drift test in `Content.IntegrationTests/Tests/Preferences/ServerDbSqliteTests.cs:474`.

## File-based persistence

| Data | Path | Format |
|---|---|---|
| Replays | `<user data>/replays/*.zip` | engine replay archive |
| Saved/autosaved maps | user data | map YAML v7 |
| Chem/reaction dump (`game.destination_file`) | user data | JSON, one-shot then shutdown |
| Uploaded resources | DB byte arrays | EF |
| Client-side state | client user dir | TOML/user data |

## Prototype/map migrations (persistent content IDs)

- `Resources/migration.yml`, `Resources/nf_migration.yml` → `MapMigrationSystem` on map load;
  DEBUG validates target IDs.
- `Resources/IgnoredPrototypes/ignoredPrototypes.yml` makes upstream prototypes abstract.

## Dependencies

EF Core providers, `Content.Shared.Database` enums, content managers (preferences, bans, admin, consent,
playtime, job whitelist, connection), `GameTicker` (rounds), `UserDbDataManager` orchestration.

## Depended on by

Connection admission, preferences/character system, consent, admin tools, playtime/job requirements,
disconnection handling, integration tests.

## Notes / caveats

- `LogType` numeric values are persisted — never renumber.
- SQLite is the default; many behaviors (notifications, FTS, jsonb) are Postgres-only.
- Admin logs are DB-only; excess is dropped, not written to file.
- No Postgres integration tests in-repo; model-drift check runs against SQLite only.
