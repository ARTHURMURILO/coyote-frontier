# UNKNOWN

Questions that could not be answered confidently from the working tree. Treat these as
"verify before relying on" items. Each entry says what is known and what is missing.

## Deployment / operations

1. **Production `server_config.toml` is not in the repo.** Actual values for `config.presets`,
   `game.map`, `game.defaultpreset`, `auth.mode`, `database.engine`, admin API token and
   `nf.server_auth_list` are unknown. The Coyote preset exists (`Resources/ConfigPresets/_CS/coyote_sector.toml`)
   but whether it is used in production is not verifiable here.
2. **Live server topology.** `MiniAuthManager` implies multiple servers (Palmtree network), but how many,
   which addresses, and whether `nf.allow_multi_connect` is enabled are not in-repo.
3. **Postgres in production?** SQLite is the code default; no evidence either way in-repo.
4. **Whether `_PS` code is complete/accepted.** Only one server system + a few prototypes; empty
   directories suggest work-in-progress. Intent cannot be determined from code.
5. **`_Offbrand`**: textures exist for a medical/surgery module but all code/prototype/locale folders are
   empty. Whether the module is planned, stripped, or intentionally dropped is unknown.
6. **`identifier.sqlite`** (0 bytes, tracked): origin and intended use unknown. Not referenced anywhere.
7. **`ToggleableClothingExample.md`** at repo root: documentation for a feature; whether the documented
   behavior matches the current `ToggleableClothing` implementation was not verified.

## Runtime behavior hidden in the engine

8. **Engine internals not line-traced**: handshake/encryption internals, PVS budget algorithms, physics
   solver details, map chunk serialization, UI layout/rendering. The engine submodule is the source of
   truth if these matter.
9. **Exact analyzer behavior** (RA00xx) is only summarized; `Robust.Analyzers.Tests` is the authoritative
   reference for diagnostic semantics.
10. **Sandbox whitelist coverage**: `ContentPack/Sandbox.yml` lists many BCL APIs; whether a specific API
    is allowed on the client was not exhaustively checked.
11. **Engine upgrade risk**: which content code depends on undocumented engine behavior
    (e.g. auto-state generator internals, `AllEntityQueryEnumerator` semantics) is not fully enumerable
    without an upgrade attempt.

## Content/gameplay reachability

12. **Which upstream content is reachable under the live NF presets.** Upstream presets have no rules, but
    individual systems, objectives, spells and station events can still be attached by admins or NF
    event schedulers. No end-to-end reachability audit was performed.
13. **Upstream cargo on non-default maps**: `CargoSystem` compiles and would run for maps using
    `BaseStationCargo`; which maps in `NFMapPool`/admin maps do so is unknown.
14. **Antag preference storage vs disabled UI**: profiles still store antag preferences and
    `AntagSelectionDefinition` can preselect by them; whether any live config uses this path is unknown.
15. **Objective condition systems**: which of the many `*ConditionSystem` classes are referenced by any
    active objective prototype was not exhaustively enumerated.
16. **`_WF` Autopilot, `_White` ExaminableCharacter, `_CD` Engraveable, `_Mono` armor plates**: imported
    systems with no obvious live users were not traced to prototypes; some may be dormant.

## Moderation / consent

17. **Data protection posture**: consent freetext is readable by any client through
    `CharacterInfoSystem` with no access control; whether that is intended is unknown (documented in
    `.ai/HAZARDS.md`). No privacy flag exists.
18. **`floof.consent_rules` CVar** is defined but never read — dead config, purpose unknown.
19. **`Hypno` and `NoClone` consent toggles** have no enforcement callers; intended behavior unknown.
20. **Censor implementation** (`SimpleCensor`, `RegexCensor`) exists but isn't wired; whether the
    moderation V2 is planned or abandoned is unknown.

## Repo/tooling

21. **`bors.toml` "Build & Test Release" status**: required but no matching workflow exists in this repo
    (possibly inherited from the parent fork). Whether CI actually gates on it is unknown.
22. **`Resources/Changelog/Frontier.yml`** is legacy/frozen history: no tool writes it, the client
    loads it as the "Frontier" tab. Palmtree entries go to `Resources/Changelog/Palmtree.yml`
    (see `.ai/guides/changelogs.md`).
23. **`Content.Docfx`** is not in the solution; only the weekly workflow uses it; its output/docs coverage
    was not assessed.
24. **`Content.Tools`** in this fork only contains the map merge driver; upstream subcommands are absent.
    Whether other Tools commands are expected is unknown.
25. **`_PS` empty directories** (`Interactions`, `UI`) and `Resources/Prototypes/_PS/{InteractionVerbs,Recipes}`
    may indicate deleted UI or planned work; unknown.
26. **Map renderer viewer JSON consumers** (`map.json`) — used by external tooling not in this repo.

## Documentation gaps

27. **Line numbers in `.ai/` docs** are valid for this checkout (branch `palm3`, commit `4bfdad813c`) and
    will drift.
28. **The `RobustToolbox` submodule revision cannot be changed safely from content docs**; any engine
    question should be checked against the pinned commit.

## Added during the procedural-guide pass

29. **`!type:DungeonSpawnGroup` loose resolution** for bluespace dungeon events was not verified at
    runtime; if those events never fire, the tag may need to be `BluespaceDungeonSpawnGroup`.
30. **Are the commented-out salvage-magnet asteroid configs intentional removal or a fork regression?**
    `SharedSalvageSystem.Magnet.cs` still references them.
31. **NF drydock manual re-initialization**: whether double `MapInitEvent` causes duplicate-component
    issues in practice is unverified in-game (the code skips bulbs/items/docking explicitly).
32. **No `_PS` gamemode/admin-command/species precedent exists**; guide layouts mirror `_NF`/`_CS`.
33. **Failure modes not tested**: missing locale keys (raw key vs error), `HideSpawnMenu` behavior in
    fork admin UIs, and exact organ/damage behavior when overriding a species on a non-matching mob base.
34. **No automated tests exist for** dungeon configs, species prototypes, or admin permission behavior
    (only YAMLLinter and the generic GameRules start/end test).
