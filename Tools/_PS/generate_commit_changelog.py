#!/usr/bin/env python3
"""Draft Palmtree Station changelog entries from git commits.

Local replacement for the removed GitHub changelog bot. Scans a commit range,
drafts one entry per non-merge commit into Resources/Changelog/Palmtree.yml and
records the last processed commit in Tools/_PS/changelog_state.json so the next
run only looks at new commits.

Drafts are meant to be reviewed (and edited or deleted) before committing. See
.ai/guides/changelogs.md for the full workflow.
"""

from __future__ import annotations

import argparse
import datetime
import json
import re
import subprocess
import sys
from pathlib import Path

import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
CHANGELOG_PATH = REPO_ROOT / "Resources" / "Changelog" / "Palmtree.yml"
STATE_PATH = Path(__file__).resolve().parent / "changelog_state.json"
DEFAULT_REPO_URL = "https://github.com/xTheLifex/coyote-frontier"

# Commit subjects starting with these words are skipped by default (not player-visible).
SKIPPED_PREFIXES = {"chore", "ci", "build", "docs", "doc", "style", "test", "tests", "meta"}

# Prefix/verb (lowercase) -> changelog type.
TYPE_BY_PREFIX = {
    "feat": "Add",
    "feature": "Add",
    "add": "Add",
    "adds": "Add",
    "added": "Add",
    "implement": "Add",
    "implements": "Add",
    "fix": "Fix",
    "fixes": "Fix",
    "fixed": "Fix",
    "bugfix": "Fix",
    "repair": "Fix",
    "repairs": "Fix",
    "remove": "Remove",
    "removes": "Remove",
    "removed": "Remove",
    "delete": "Remove",
    "deletes": "Remove",
    "revert": "Remove",
    "reverts": "Remove",
    "tweak": "Tweak",
    "tweaks": "Tweak",
    "tweaked": "Tweak",
    "adjust": "Tweak",
    "adjusts": "Tweak",
    "change": "Tweak",
    "changes": "Tweak",
    "update": "Tweak",
    "updates": "Tweak",
    "balance": "Tweak",
    "balances": "Tweak",
    "perf": "Tweak",
    "refactor": "Tweak",
    "refactors": "Tweak",
    "polish": "Tweak",
    "improve": "Tweak",
    "improves": "Tweak",
}

CONVENTIONAL_RE = re.compile(r"^(?P<prefix>[A-Za-z]+)(?:\([^)]*\))?!?:\s*(?P<message>.+)$")
WORD_RE = re.compile(r"^([A-Za-z]+)")
LOG_FORMAT = "%H%x1f%an%x1f%aI%x1f%s%x1e"
ENTRY_TYPES = ("Add", "Remove", "Fix", "Tweak")


def git(*args: str) -> str:
    result = subprocess.run(
        ["git", "-C", str(REPO_ROOT), *args],
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or f"git {' '.join(args)} failed")
    return result.stdout


def resolve_rev(rev: str) -> str:
    out = git("rev-parse", "--verify", "--quiet", f"{rev}^{{commit}}")
    return out.strip()


def get_commits(from_sha: str, to_sha: str) -> list[tuple[str, str, str, str]]:
    out = git("log", "--no-merges", "--reverse", f"--format={LOG_FORMAT}", f"{from_sha}..{to_sha}")
    commits = []
    for record in out.split("\x1e"):
        record = record.strip("\n")
        if not record:
            continue
        sha, author, date, subject = record.split("\x1f")
        commits.append((sha, author, date, subject))
    return commits


def classify(subject: str, default_type: str) -> tuple[str, str]:
    """Return (changelog type, message) for a commit subject."""
    match = CONVENTIONAL_RE.match(subject)
    if match:
        entry_type = TYPE_BY_PREFIX.get(match.group("prefix").lower())
        if entry_type:
            return entry_type, match.group("message").strip()

    word = WORD_RE.match(subject)
    if word:
        entry_type = TYPE_BY_PREFIX.get(word.group(1).lower())
        if entry_type:
            # Keep the full subject: "Adds X" reads better than "X" in the changelog.
            return entry_type, subject.strip()

    return default_type, subject.strip()


def should_skip(subject: str, include_all: bool) -> bool:
    if include_all:
        return False
    word = WORD_RE.match(subject)
    return bool(word) and word.group(1).lower() in SKIPPED_PREFIXES


def format_time(iso: str) -> str:
    parsed = datetime.datetime.fromisoformat(iso).astimezone(datetime.timezone.utc)
    return parsed.strftime("%Y-%m-%dT%H:%M:%S.0000000+00:00")


def default_repo_url() -> str:
    try:
        url = git("remote", "get-url", "origin").strip()
    except RuntimeError:
        return DEFAULT_REPO_URL

    if url.startswith("git@"):
        url = "https://" + url[4:].replace(":", "/", 1)
    if url.endswith(".git"):
        url = url[:-4]
    return url or DEFAULT_REPO_URL


def read_state() -> str | None:
    if not STATE_PATH.exists():
        return None
    try:
        return json.loads(STATE_PATH.read_text(encoding="utf-8"))["last_commit"]
    except (json.JSONDecodeError, KeyError):
        print(f"warning: could not read {STATE_PATH}; ignoring it")
        return None


def write_state(sha: str) -> None:
    STATE_PATH.write_text(json.dumps({"last_commit": sha}, indent=2) + "\n", encoding="utf-8")


def load_changelog() -> tuple[dict, list]:
    if not CHANGELOG_PATH.exists():
        sys.exit(f"error: changelog file not found: {CHANGELOG_PATH.relative_to(REPO_ROOT)}")
    data = yaml.safe_load(CHANGELOG_PATH.read_text(encoding="utf-8")) or {}
    entries = data.get("Entries") or []
    data["Entries"] = entries
    return data, entries


def build_entries(
    commits: list[tuple[str, str, str, str]],
    entries: list,
    author_override: str | None,
    repo_url: str,
    default_type: str,
) -> list[dict]:
    existing_messages = {
        change.get("message")
        for entry in entries
        for change in (entry.get("changes") or [])
    }
    next_id = max((entry.get("id", 0) for entry in entries), default=0) + 1

    drafts = []
    for sha, author, date, subject in commits:
        entry_type, message = classify(subject, default_type)
        if not message:
            continue
        if message in existing_messages:
            print(f"skip (already in changelog): {subject}")
            continue

        drafts.append(
            {
                "author": author_override or author,
                "changes": [{"type": entry_type, "message": message}],
                "id": next_id,
                "time": format_time(date),
                "url": f"{repo_url}/commit/{sha}",
            }
        )
        next_id += 1

    return drafts


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--from",
        dest="from_rev",
        help="First commit of the range (exclusive). Defaults to Tools/_PS/changelog_state.json, "
        "or HEAD~1 when no state exists yet.",
    )
    parser.add_argument("--to", dest="to_rev", default="HEAD", help="Last commit of the range (default: HEAD).")
    parser.add_argument("--author", help="Override the author name for drafted entries (default: git author).")
    parser.add_argument("--repo-url", help=f"Repository URL used for commit links (default: origin remote, else {DEFAULT_REPO_URL}).")
    parser.add_argument("--default-type", choices=ENTRY_TYPES, default="Tweak", help="Type for commits with no recognizable prefix (default: Tweak).")
    parser.add_argument("--include-all", action="store_true", help=f"Also draft commits starting with {', '.join(sorted(SKIPPED_PREFIXES))}.")
    parser.add_argument("--dry-run", action="store_true", help="Print the drafts without writing the changelog or the state file.")
    parser.add_argument("--no-state", action="store_true", help="Do not record the last processed commit.")
    args = parser.parse_args()

    try:
        to_sha = resolve_rev(args.to_rev)
    except RuntimeError as exc:
        sys.exit(f"error: cannot resolve --to {args.to_rev!r}: {exc}")

    from_sha = None
    if args.from_rev:
        try:
            from_sha = resolve_rev(args.from_rev)
        except RuntimeError as exc:
            sys.exit(f"error: cannot resolve --from {args.from_rev!r}: {exc}")
    else:
        state = read_state()
        if state:
            try:
                from_sha = resolve_rev(state)
            except RuntimeError:
                sys.exit(f"error: state file points at unknown commit {state!r}; pass --from explicitly")
        else:
            try:
                from_sha = resolve_rev("HEAD~1")
                print("No --from and no state file; defaulting to HEAD~1 (one commit). Pass --from REV for a wider range.")
            except RuntimeError:
                sys.exit("error: no parent commit to default to; pass --from explicitly")

    if from_sha == to_sha:
        print(f"No new commits in {from_sha[:9]}..{to_sha[:9]}.")
        return

    try:
        commits = get_commits(from_sha, to_sha)
    except RuntimeError as exc:
        sys.exit(f"error: git log failed: {exc}")

    commits = [commit for commit in commits if not should_skip(commit[3], args.include_all)]
    if not commits:
        print(f"No draftable commits in {from_sha[:9]}..{to_sha[:9]}.")
        return

    data, entries = load_changelog()
    drafts = build_entries(commits, entries, args.author, args.repo_url or default_repo_url(), args.default_type)

    if not drafts:
        print("Nothing to add: all commits were filtered out or already present.")
        return

    if args.dry_run:
        print(yaml.safe_dump({"Entries": drafts}, sort_keys=False, allow_unicode=True))
        print(f"[dry run] {len(drafts)} entr{'y' if len(drafts) == 1 else 'ies'} not written.")
        return

    entries.extend(drafts)
    CHANGELOG_PATH.write_text(
        yaml.safe_dump(data, sort_keys=False, allow_unicode=True),
        encoding="utf-8",
    )
    if not args.no_state:
        write_state(to_sha)

    print(f"Added {len(drafts)} entr{'y' if len(drafts) == 1 else 'ies'} to {CHANGELOG_PATH.relative_to(REPO_ROOT)}.")
    print("Review the file (edit or remove drafts as needed), then commit it.")
    print("Verify in-game: runclient.sh -> Changelog -> 'Palmtree Station' tab.")


if __name__ == "__main__":
    main()
