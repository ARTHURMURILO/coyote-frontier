# Guide: Changelogs (Manual Authoring + Commit Drafts)

> Answer to: "How do I get a change into the in-game changelog?"
> Facts from source; inferred items marked. Branch `palm3`, engine `feb9e1db6`. Prefer symbol names
> as anchors; line numbers may drift.

## TL;DR

The in-game changelog is **client-side YAML only**. `ChangelogManager` reads every direct
`Resources/Changelog/*.yml` and renders one tab per file. **Nothing scans git history** — a commit
shows up in-game only if someone writes an entry into one of those files. Palmtree entries live in
`Resources/Changelog/Palmtree.yml` (tab "Palmtree Station").

Draft entries from your commits with:

```
python3 Tools/_PS/generate_commit_changelog.py --from HEAD~10   # add --dry-run to preview
```

Review/edit the file, commit it, and a dev client shows the entries immediately (open the changelog
window; it re-reads the files every time). Players get them when a new client build ships.

The old GitHub bot (PR `:cl:` → changelog + Discord publishing) was **removed on purpose**; see
[Removed automation](#removed-automation) below.

## How the client reads changelogs

- `Content.Client/Changelog/ChangelogManager.cs`
  - `LoadChangelog()` enumerates `/Changelog/`, keeps direct `*.yml` children only (so `Parts/` is
    ignored), and deserializes each file into `Changelog` (`Name`, `Entries`, `AdminOnly`, `Order`).
  - If `Name` is empty it falls back to the filename (`Palmtree.yml` → `Palmtree`).
  - Tabs are sorted ascending by `Order`; the window calls
    `Loc.GetString($"changelog-tab-title-{Name}")`, so a tab needs a locale key to look nice.
  - `SaveNewReadId()` writes `changelog_last_seen_<ServerId>` into client user data; `MaxId` /
    `LastReadId` drive the "(new!)" button badge and the "new changes" divider.
- `Content.Client/Changelog/ChangelogWindow.xaml.cs`
  - `Opened()` → `PopulateChangelog()` → **reloads the YAML from disk on every window open**.
  - `AdminOnly` tabs are hidden for non-admins.
  - The `changelog` console command opens the same window.
- `Content.Client/Changelog/ChangelogTab.xaml.cs`
  - Entries are grouped by local date (newest first) and then by author; today's date renders as
    "Today", yesterday as "Yesterday".

### Changelog files in this fork

| File | Tab | Notes |
|---|---|---|
| `Changelog.yml` | "Upstream" (`Order: 1`, Frontier patch) | Wizden SS14 history, third-party |
| `Frontier.yml` | "Frontier" | Frontier Station history, frozen/legacy |
| `Coyote.yml` | "Coyote Sector" | Coyote/ARF history, frozen |
| `Maps.yml` | "Maps" (`Order: 1`) | Mapping changes |
| `Admin.yml` | "Admin" (`AdminOnly: true`, `Order: 2`) | Hidden from players |
| `Palmtree.yml` | "Palmtree Station" | **Our changelog** — new entries go here |

Do not append Palmtree changes to the upstream history files; keep them in `Palmtree.yml`.

## Adding entries

### Option A — draft from commits (recommended)

`Tools/_PS/generate_commit_changelog.py` is a local-only replacement for the removed GitHub bot. It
scans a commit range, drafts one entry per non-merge commit, and appends them to `Palmtree.yml`.

```
# Preview the last 10 commits as entries (writes nothing)
python3 Tools/_PS/generate_commit_changelog.py --from HEAD~10 --dry-run

# Write them, then review/edit and commit
python3 Tools/_PS/generate_commit_changelog.py --from HEAD~10
```

How the range works:

- `--from REV` is **exclusive**, `--to REV` defaults to `HEAD`.
- If `--from` is omitted, the script uses `Tools/_PS/changelog_state.json` (the last processed
  commit) if it exists, otherwise `HEAD~1`. After a real run it records `--to` in that state file, so
  subsequent runs only pick up new commits. Commit the state file together with the changelog.
- `--from <unknown-sha>` fallback: pass `--from` explicitly if the state points at a rebased/removed
  commit.

What it does to each commit:

- One entry per commit: `author`, `time` (commit author date, converted to UTC), `url`
  (`https://github.com/xTheLifex/coyote-frontier/commit/<sha>`), and one change.
- Type heuristics: conventional prefixes (`feat:`/`fix:`/`tweak:`/`remove:`, scopes and `!`
  allowed) and leading verbs (`Adds`, `Fix`, `Removes`, `Update`, ...). Unknown subjects use
  `--default-type` (default `Tweak`).
- Skips by default: merge commits, subjects starting with `chore`, `ci`, `build`, `docs`, `doc`,
  `style`, `test`, `tests`, `meta` (use `--include-all` to keep them), and messages already present
  in the changelog.
- Other flags: `--author NAME` override, `--repo-url URL`, `--no-state`, `--dry-run`.

The output is a **draft**: entries like `slop folder` or WIP subjects are what commit messages
produce, so edit or delete them before committing.

### Option B — `manual_changelog.py`

`Tools/manual_changelog.py` is the upstream interactive tool ("for when you don't want to bother
setting up the bot"):

```
python3 Tools/manual_changelog.py \
    --infile Resources/Changelog/Palmtree.yml \
    --outfile Resources/Changelog/Palmtree.yml
```

Paste a block and press Ctrl-D (EOF):

```
:cl: YourName
- add: Added a thing
- fix: Fixed a bug
```

It appends entries with `id = last id + 1` and a UTC timestamp, preserves any other top-level keys,
and prunes the file to the newest 500 entries. Empty files (`Entries: []`) are supported (fixed in
this fork).

### Option C — hand-edit the YAML

```yaml
Entries:
- author: TheLife
  changes:
  - type: Add
    message: Added a thing
  id: 17
  time: '2026-10-03T12:00:00.0000000+00:00'
  url: https://github.com/xTheLifex/coyote-frontier/commit/abc1234
```

| Key | Required | Notes |
|---|---|---|
| `author` | yes | Shown above the changes; free text |
| `changes[].type` | yes | Exactly `Add`, `Remove`, `Fix`, or `Tweak`; anything else fails deserialization and the tab won't load |
| `changes[].message` | yes | Shown as plain text; `[` / `]` are safe (unformatted), other markup is not parsed |
| `id` | yes | Integer; keep strictly increasing. Drives the "(new!)" badge / read divider |
| `time` | yes | ISO-8601; displayed as Today / Yesterday / local date |
| `url` | optional | Ignored by the client; kept for history/scripts |

## Verifying in-game (dev)

1. Make sure `Resources/Changelog/Palmtree.yml` contains your entry.
2. Run `./runclient.sh` (or `dotnet run --project Content.Client`).
3. Open **Changelog** from the main menu, or run the `changelog` command in the console.
4. Click the **Palmtree Station** tab. A freshly added entry appears under "Today".

No rebuild or client restart is needed for content changes: a non-`FULL_RELEASE` build mounts the
repo's `Resources/` directory live (`RobustToolbox/Robust.Shared/ProgramShared.cs:DoMounts()`), and
the window re-reads the YAML every time it opens.

## Shipping to players

The changelog is a **client resource**, like other `Resources/**` YAML. The server does not send
changelog data. Players see new entries when a new client build containing the updated
`Resources/Changelog/Palmtree.yml` is packaged and published (`Content.Packaging`, then the usual
release/publish workflow). Editing the file on the server machine alone does nothing for players.

## Gotchas

1. **Commits are not scanned.** Neither the game nor any remaining workflow reads git history. The
   draft generator is a local tool you run yourself; it is not a git hook.
2. **"(new!)" badge is unreliable.** `ChangelogManager.UpdateChangelogs()` computes `MaxId` from
   `changelogs[0]` — the first tab after sorting by `Order` — not from a named main changelog. With
   several `Order: 0` files (`Coyote`, `Frontier`, `Palmtree`) the winner depends on unsorted
   filesystem enumeration. Entries still display correctly; only the badge/divider can misbehave.
   Fixing it means editing `UpdateChangelogs` (use the intended changelog) — not done here.
3. **Both Python tools rewrite the whole file.** `PyYAML` drops comments and reformats; keep custom
   formatting out of the file.
4. **`id` matters for read state.** `SaveNewReadId()` stores the max id; if you reuse or reorder ids
   the divider can look wrong. `manual_changelog.py` prunes to 500 entries — don't prune below ids
   players have already seen unless you want the divider to reset.
5. **New tabs need locale keys.** A file named `Foo.yml` (or `Name: Foo`) renders as the raw string
   unless `changelog-tab-title-Foo` exists. Ours is in
   `Resources/Locale/en-US/_PS/changelog/changelog-window.ftl`.
6. **`:cl:` blocks in PRs do nothing now.** The validator workflow still checks their syntax, but
   nothing consumes them. Remove the block from your PR or ignore it; use the tools above instead.
7. **Parked scripts.** `Tools/_NF/changelog/changelog.js` + `validate_changelog.js`,
   `Tools/actions_changelogs_since_last_run.py` (Discord diffing, reads `Coyote.yml`), and
   `Tools/actions_changelog_rss.py` (Wizden-specific SSH/RSS feed) still exist but have no workflow.
   `Tools/update_changelog.py` + `Resources/Changelog/Parts/` (parts-based system) are dormant.
   Don't wire them up without reading them first.
8. **`AdminOnly`/`Order`/`Name`** live in the YAML. `manual_changelog.py` preserves them; the draft
   generator re-dumps the whole file and preserves them too (they are top-level keys); the old JS
   bot stripped them, but it is no longer used.

## Removed automation

`.github/workflows/changelog.yml` (PR merged → `Tools/_NF/changelog/changelog.js` → `Coyote.yml` +
git commit/push) and `.github/workflows/publish-changelog.yml` (daily Discord posting) were deleted
because Palmtree wanted manual, review-first changelogs and no GitHub-side behavior. Reasons it was
never reliable here anyway: the fork's default branch is `master` while all PRs target `palm3`, and
since December 2025 `pull_request_target` workflows always run from the default branch, so generated
entries would have been committed to `master` instead of `palm3`.

`.github/workflows/nf-validate-changelog.yml` was kept; it only syntax-checks a `:cl:` block if one
is present.

If automation is ever wanted again, the minimum would be: explicit `ref: palm3` on checkout, a
`git pull --rebase origin palm3` before pushing, removal of `continue-on-error`, and a `palm3`-safe
`CHANGELOG_DIR` (and the publisher pointing at `palm3` too).

## Quick reference

| Task | Command |
|---|---|
| Draft entries from commits | `python3 Tools/_PS/generate_commit_changelog.py --from <rev>` |
| Preview drafts | add `--dry-run` |
| Interactive entries | `python3 Tools/manual_changelog.py --infile Resources/Changelog/Palmtree.yml --outfile Resources/Changelog/Palmtree.yml` |
| Check YAML | `python3 -c "import yaml; yaml.safe_load(open('Resources/Changelog/Palmtree.yml'))"` |
| See it in-game | `./runclient.sh` → Changelog → "Palmtree Station" |
