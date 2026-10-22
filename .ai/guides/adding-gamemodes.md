# Guide: Gamemodes — How They Work and How to Add One

> Answer to: "How do gamemodes work and how can one add a new gamemode?"
> Facts from source; inferred items marked. Line numbers valid for branch `palm3` commit `4bfdad813c`.

## TL;DR

A "gamemode" is a **`gamePreset` prototype** listing the IDs of round-start rule entities, plus the
rules themselves. A "game rule" is **an entity prototype carrying the `GameRule` component** — there is
no `GameRulePrototype` C# class. To add a gamemode: write a `GameRuleSystem<T>` + rule component, add an
entity prototype with the `GameRule` component, add a `gamePreset` that references it, and localize it.

## Concept mapping

| Concept | Scope | Implementation |
|---|---|---|
| Game preset | Round-level choice | `gamePreset` prototype (`Content.Server/GameTicking/Presets/GamePresetPrototype.cs:12`) |
| Game rule | Logic active during a round | Entity prototype with `GameRule` component + `GameRuleSystem<T>` |
| Roundstart rule | Usually whole round | Listed in preset `rules:`; `Started` sets up, often never self-ends |
| Station event | Timed random occurrence | Rule entity with `StationEvent` + custom component; `StationEventSystem<T>` owns duration/announcements |
| Scheduler | Rule that adds events | `BasicStationEventScheduler`/`RampingStationEventScheduler`; ticks and calls `EventManagerSystem.RunRandomEvent` |

Live presets: `Resources/Prototypes/_NF/game_presets.yml` (`NFAdventure`, `NFPirate` default, `NFTest`).
**Every upstream preset in `Resources/Prototypes/game_presets.yml` has `rules: []`** (commented out) and
will run as an empty round if selected.

## Preset machinery

`GamePresetPrototype` fields: `id`, `alias[]`, `name` (loc key), `description` (loc key), `showInVote`,
`minPlayers`/`maxPlayers` (**vote-only gates**), `rules` (entity prototype IDs), `supportedMaps`
(`GameMapPoolPrototype`).

Selection:
- Startup: `GameTicker.Initialize` → `SetGamePreset(CCVars.GameLobbyDefaultPreset)`; default
  `nfpirate` (`Content.Shared/CCVar/CCVars.Game.cs:36`).
- Votes (`VoteManager.DefaultVotes.cs:243-261`), admin `forcepreset` (immediate start in pre-round
  lobby), `setgamepreset <preset> [rounds]`.
- `Preset` = next round; `CurrentPreset` = set during `AddGamePresetRules()`.

`RoundStartAttemptEvent` (`GameTicker.RoundFlow.cs:928-942`): raised after preset rules start, before
player spawn; cancellable. `GameRuleSystem<T>` cancels the preset if `players < GameRule.MinPlayers`
and `CancelPresetOnTooFewPlayers` (default true), otherwise `ForceEndSelf`. On cancel, the ticker tries
`game.fallbackpreset` (default `Traitor,Extended` — rule-less here) or restarts with a delay.

## Rule machinery

- `GameRuleComponent` (`Content.Shared/GameTicking/Components/GameRuleComponent.cs:12-38`):
  `ActivatedAt`, `MinPlayers`, `CancelPresetOnTooFewPlayers`, `MinMax? Delay`.
  There is **no `MaxTime`/`EndDelay` on this component**; use `MaxTimeRestartRuleComponent`
  (`Content.Server/GameTicking/Rules/Components/MaxTimeRestartRuleComponent.cs:14-21`).
- Markers: `ActiveGameRuleComponent`, `EndedGameRuleComponent`, `DelayedStartRuleComponent`.
- Events: `GameRuleAddedEvent`, `GameRuleStartedEvent`, `GameRuleEndedEvent`.
- `GameRuleSystem<T>` (`Content.Server/GameTicking/Rules/GameRuleSystem.cs:10-145`) subscribes:
  `RoundStartAttemptEvent`, `T+GameRuleAddedEvent` → virtual `Added`, `Started`, `Ended`,
  `RoundEndTextAppendEvent` → `AppendRoundEndText`, and `Update` → `ActiveTick` for active rules.
  `GameRuleSystem.Utility.cs` provides `QueryActiveRules`, `QueryDelayedRules`, `QueryAllRules`,
  `TryGetRandomStation`, `TryFindRandomTile`, `ForceEndSelf`.
- `GameTicker.GameRule.cs`: `AddGameRule` (:70), `StartGameRule` (:121, handles `Delay`),
  `EndGameRule` (:179, marks ended, does **not** delete entities), commands `addgamerule`,
  `endgamerule <NetEntity>`, `cleargamerules`, `listgamerules` (:330-438).
- `GetAllGameRulePrototypes()` enumerates non-abstract `EntityPrototype`s with `GameRuleComponent`
  (`GameTicker.GameRule.cs:304-314`).

### Lifecycle

```
RestartRound -> PreRoundLobby
  votes / setgamepreset / forcepreset -> Preset = ...
StartRoundInternal
  LoadMaps -> AddGamePresetRules(): CurrentPreset = Preset; Spawn each rule -> GameRuleAddedEvent
  StartGamePresetRules(): StartGameRule each -> delayed OR Active + GameRuleStartedEvent
  RoundStartingEvent
  StartPreset -> RoundStartAttemptEvent (min players; may cancel -> fallback)
  SpawnPlayers -> RulePlayerSpawningEvent -> jobs -> RulePlayerJobsAssignedEvent
  RunLevel = InRound; RoundStartedEvent (NF)
InRound
  UpdateGameRules starts delayed rules; rule logic runs; schedulers add events
  rules end via EndGameRule / ForceEndSelf / RoundEndSystem.EndRound
PostRound
  RoundEndTextAppendEvent (scoreboard)
RestartRound
  RoundRestartCleanupEvent -> ClearGameRules (EndGameRule for all) -> FlushEntities
```

## Traced examples

- **`NFAdventure` (fork, default):** preset `_NF/game_presets.yml:1-33` → entity
  `_NF/GameRules/roundstart.yml:1-6` (`parent: BaseGameRule`, `NFAdventureRule` component) → C#
  `Content.Server/_NF/GameRule/NfAdventureRuleSystem.cs:33` (`Started` generates POIs/markets via
  `PointOfInterestSystem`, raises `StationsGeneratedEvent`; no self-end).
- **`DeathMatch31` (upstream, disabled):** `Resources/Prototypes/GameRules/roundstart.yml:46-73` →
  `DeathMatchRuleSystem` (`Content.Server/GameTicking/Rules/DeathMatchRuleSystem.cs:22`) hooks
  `PlayerBeforeSpawnEvent`, kill reports, ends the round at `KillCap`; `RespawnDeadRule` +
  `RespawnTracker` via `RespawnRuleSystem`.
- **`MaxTimeRestart`:** `Content.Server/GameTicking/Rules/MaxTimeRestartRuleSystem.cs:9-77` — good
  template for a self-ending round; fork variant `NFMaxTimeRestart` (`_NF/GameRules/roundstart.yml:153-159`).

## Recipe — new gamemode `PSDeathmatch`

### Step 1 — rule component (server)

`Content.Server/_PS/GameTicking/Rules/Components/PSDeathmatchRuleComponent.cs`:
`[RegisterComponent, Access(typeof(PSDeathmatchRuleSystem))]` with `[DataField]` fields
(e.g. `KillCap`, `RestartDelay`). Server-only unless the client must see it.

### Step 2 — rule system (server)

`Content.Server/_PS/GameTicking/Rules/PSDeathmatchRuleSystem.cs`:
```csharp
public sealed class PSDeathmatchRuleSystem : GameRuleSystem<PSDeathmatchRuleComponent>
{
    public override void Initialize()
    {
        base.Initialize();   // REQUIRED: min-player check + Added/Started/Ended/ActiveTick
        SubscribeLocalEvent<KillReportedEvent>(OnKillReported);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }
    // override Started/Ended/AppendRoundEndText as needed
    // on kill cap: _roundEnd.EndRound(RestartDelay) or ForceEndSelf(uid)
}
```
Use `QueryActiveRules()` rather than raw queries. Clean system statics on `RoundRestartCleanupEvent`;
keep rule-scoped state on the rule component and reset in `Ended`.

### Step 3 — rule prototype

`Resources/Prototypes/_PS/GameRules/roundstart.yml`:
```yaml
- type: entity
  id: PSDeathmatch
  parent: BaseGameRule          # supplies the required GameRule component
  categories: [ HideSpawnMenu ]
  components:
  - type: GameRule
    minPlayers: 2
    delay: { min: 15, max: 30 } # optional
  - type: PSDeathmatchRule
    killCap: 25
```
`BaseGameRule` is at `Resources/Prototypes/GameRules/roundstart.yml:1-5`. Without `GameRule` the
prototype will not be listed or startable.

### Step 4 — preset prototype

`Resources/Prototypes/_PS/game_presets.yml`:
```yaml
- type: gamePreset
  id: PSDeathmatch
  alias: [ psdm, psdeathmatch ]
  name: ps-deathmatch-title
  description: ps-deathmatch-description
  showInVote: true
  maxPlayers: 20
  rules:
  - PSDeathmatch
  - NFBasicStationEventScheduler   # reuse existing scheduler IDs
  - NFRoundstartVariation
```

### Step 5 — locale

`Resources/Locale/en-US/_PS/game_presets.ftl`: `ps-deathmatch-title`, `ps-deathmatch-description`
(missing keys render as raw keys/errors).

### Step 6 — select and test

- Vote (`showInVote: true`), server config `game.defaultpreset PSDeathmatch`, admin
  `setgamepreset PSDeathmatch` then `startround`, or `forcepreset PSDeathmatch` in the lobby.
- Midround: `addgamerule PSDeathmatch` (auto-starts when `InRound`), `listgamerules`,
  `endgamerule <net id>`, `cleargamerules`.
- Scheduler debugging: `stationevent.simulate`, `stationevent.lsprob`, `stationevent.prob <scheduler> <event>`.
- Integration test coverage: `Content.IntegrationTests/Tests/GameRules/StartEndGameRulesTest.cs:16-49`
  starts every `GameRule` prototype and clears them — make `Started`/`Ended`/`Update` robust with no map/players.

## Pitfalls

1. Rule entity must have `GameRule` (parent `BaseGameRule`).
2. `GameRuleSystem<T>.Initialize()` must call `base.Initialize()`.
3. `Added` ≠ `Started`; `AddGameRule` midround leaves the rule pending unless started
   (station events auto-start themselves; custom systems do not).
4. `CancelPresetOnTooFewPlayers` default true aborts the whole preset; set false to self-end instead.
5. Min players live on the rule, not the preset; preset `minPlayers`/`maxPlayers` only filter the vote.
6. Fallback presets are rule-less in this fork — a failed preset becomes an empty round.
7. `EndGameRule` does not delete entities; don't act on ended rules (`AllEntityQuery<T>` doesn't filter).
8. `RoundRestartCleanupEvent` fires **before** rules are ended/flushed — reset both system statics and
   rule state.
9. Delayed start is one-shot (`DelayedStartRuleComponent` removed on first start).
10. Loc keys required for `name`/`description`.
11. `supportedMaps` rerolls the map if the current one isn't eligible; leave unset to inherit the pool.
12. `endgamerule` takes a **NetEntity**, not a prototype ID.
13. `NFAdventure` filters POIs by `_ticker.CurrentPreset?.ID` — a custom preset without `NFAdventure`
    gets no POIs; with it, only POIs whose `SpawnGamePreset` is empty or lists your preset ID spawn.

## Unknowns

- No `_PS` gamemode precedent exists; naming/layout above mirrors `_NF`.
- Missing loc-key failure mode (raw key vs error) not verified.
- Upstream-documented `GameRule.MaxTime`/`EndDelay` do not exist in this revision.

## Source anchors

`Content.Server/GameTicking/{GameTicker.GamePreset.cs,GameTicker.GameRule.cs,Presets/GamePresetPrototype.cs}`,
`Content.Server/GameTicking/Rules/GameRuleSystem*.cs`, `Content.Shared/GameTicking/Components/GameRuleComponent.cs`,
`Content.Server/StationEvents/{EventManagerSystem,BasicStationEventSchedulerSystem}.cs`,
`Content.Server/_NF/GameRule/NfAdventureRuleSystem.cs`, `Resources/Prototypes/GameRules/`,
`Resources/Prototypes/_NF/GameRules/`.
