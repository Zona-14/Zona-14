#!/usr/bin/env python3

"""
Sends updates to a Discord webhook for new changelog entries since the last
successful publish.

Iterates over the four Zona-14 category YAMLs (Zona 14, Admin, Maps, Rules) and
groups the new entries under category headers in a single Discord message.

Baseline is the commit pointed to by the `changelog-last-published` tag, which
the publish workflow force-updates after a successful Discord post. This
captures the rolled-up state at end-of-run, unlike a workflow run's head_commit
(which is the trigger SHA, before the in-run rollup commit).
"""

import itertools
import os
from pathlib import Path
from typing import Any, Iterable

import requests
import yaml
import time

DEBUG = False
GITHUB_API_URL = os.environ.get("GITHUB_API_URL", "https://api.github.com")
BASELINE_TAG = "changelog-last-published"

# https://discord.com/developers/docs/resources/webhook
DISCORD_SPLIT_LIMIT = 2000
DISCORD_WEBHOOK_URL = os.environ.get("DISCORD_WEBHOOK_URL")

# (display_label, repo path).
CHANGELOG_FILES: list[tuple[str, str]] = [
    ("Zona 14", "Resources/Changelog/Zona14.yml"),
    ("Admin",   "Resources/Changelog/Admin.yml"),
    ("Maps",    "Resources/Changelog/Maps.yml"),
    ("Rules",   "Resources/Changelog/Rules.yml"),
]

TYPES_TO_EMOJI = {"Fix": "🐛", "Add": "🆕", "Remove": "❌", "Tweak": "⚒️"}

ChangelogEntry = dict[str, Any]


def main():
    if not DISCORD_WEBHOOK_URL:
        print("No discord webhook URL found, skipping discord send")
        return

    session = _build_session()
    github_repository = os.environ["GITHUB_REPOSITORY"]
    last_sha = _resolve_baseline_sha(session, github_repository)
    if last_sha is None:
        print(
            f"No `{BASELINE_TAG}` tag found — skipping discord send (first publish)."
        )
        return
    print(f"Baseline `{BASELINE_TAG}` -> {last_sha}")

    all_lines: list[str] = []

    for label, path in CHANGELOG_FILES:
        cur = _load_local_yaml(path)
        if cur is None:
            print(f"{path}: not present in working tree, skipping")
            continue

        old = _load_remote_yaml(session, github_repository, last_sha, path)
        diff = list(diff_changelog(old, cur))
        if not diff:
            continue

        all_lines.append(f"\n__**{label}**__\n")
        all_lines.extend(changelog_entries_to_message_lines(diff))

    if not all_lines:
        print("No new changelog entries since last publish.")
        return

    send_message_lines(all_lines)


def _build_session() -> requests.Session:
    session = requests.Session()
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        session.headers["Authorization"] = f"Bearer {token}"
    session.headers["Accept"] = "application/vnd.github+json"
    session.headers["X-GitHub-Api-Version"] = "2022-11-28"
    return session


def _resolve_baseline_sha(session: requests.Session, repo: str) -> str | None:
    """Return the commit SHA that `BASELINE_TAG` points to, or None if absent."""
    resp = session.get(
        f"{GITHUB_API_URL}/repos/{repo}/git/ref/tags/{BASELINE_TAG}"
    )
    if resp.status_code == 404:
        return None
    resp.raise_for_status()
    obj = resp.json().get("object", {})
    sha = obj.get("sha")
    if obj.get("type") == "tag" and sha:
        # Annotated tag; dereference to the commit it points at.
        tag_resp = session.get(
            f"{GITHUB_API_URL}/repos/{repo}/git/tags/{sha}"
        )
        tag_resp.raise_for_status()
        sha = tag_resp.json().get("object", {}).get("sha")
    return sha


def _load_local_yaml(path: str) -> dict[str, Any] | None:
    p = Path(path)
    if not p.exists():
        return None
    with p.open("r", encoding="utf-8-sig") as f:
        return yaml.safe_load(f) or {"Entries": []}


def _load_remote_yaml(
    session: requests.Session, repo: str, sha: str, path: str
) -> dict[str, Any]:
    """Fetch a file at a SHA. Returns {Entries: []} if the file didn't exist there."""
    headers = {"Accept": "application/vnd.github.raw"}
    resp = session.get(
        f"{GITHUB_API_URL}/repos/{repo}/contents/{path}",
        headers=headers,
        params={"ref": sha},
    )
    if resp.status_code == 404:
        return {"Entries": []}
    resp.raise_for_status()
    return yaml.safe_load(resp.text) or {"Entries": []}


def diff_changelog(
    old: dict[str, Any], cur: dict[str, Any]
) -> Iterable[ChangelogEntry]:
    """Find all entries in cur whose id isn't present in old."""
    old_entry_ids = {e["id"] for e in old.get("Entries", [])}
    return (e for e in cur.get("Entries", []) if e["id"] not in old_entry_ids)


def get_discord_body(content: str):
    return {
        "content": content,
        "allowed_mentions": {"parse": []},
        "flags": 1 << 2,  # SUPPRESS_EMBEDS
    }


def send_discord_webhook(lines: list[str]):
    content = "".join(lines)
    body = get_discord_body(content)
    retry_attempt = 0

    try:
        response = requests.post(DISCORD_WEBHOOK_URL, json=body, timeout=10)
        while response.status_code == 429:
            retry_attempt += 1
            if retry_attempt > 20:
                print("Too many retries on a single request despite following retry_after header... giving up")
                exit(1)
            retry_after = response.json().get("retry_after", 5)
            print(f"Rate limited, retrying after {retry_after} seconds")
            time.sleep(retry_after)
            response = requests.post(DISCORD_WEBHOOK_URL, json=body, timeout=10)
        response.raise_for_status()
    except requests.exceptions.RequestException as e:
        print(f"Failed to send message: {e}")
        exit(1)


def changelog_entries_to_message_lines(entries: Iterable[ChangelogEntry]) -> list[str]:
    """Process structured changelog entries into a list of lines making up a formatted message."""
    message_lines: list[str] = []

    for contributor_name, group in itertools.groupby(entries, lambda x: x["author"]):
        message_lines.append("\n")
        message_lines.append(f"**{contributor_name}** updated:\n")

        for entry in group:
            url = entry.get("url")
            if url and not url.strip():
                url = None

            for change in entry["changes"]:
                emoji = TYPES_TO_EMOJI.get(change["type"], "❓")
                message = change["message"]

                if len(message) > DISCORD_SPLIT_LIMIT:
                    message = message[: DISCORD_SPLIT_LIMIT - 100].rstrip() + " [...]"

                if url is not None:
                    pr_number = url.split("/")[-1]
                    line = f"{emoji} - {message} ([#{pr_number}]({url}))\n"
                else:
                    line = f"{emoji} - {message}\n"

                message_lines.append(line)

    return message_lines


def send_message_lines(message_lines: list[str]):
    """Join message lines into chunks below Discord's per-message length limit, send in order."""
    chunk_lines: list[str] = []
    chunk_length = 0

    for line in message_lines:
        line_length = len(line)
        new_chunk_length = chunk_length + line_length

        if new_chunk_length > DISCORD_SPLIT_LIMIT:
            print("Split changelog and sending to discord")
            send_discord_webhook(chunk_lines)
            new_chunk_length = line_length
            chunk_lines = []

        chunk_lines.append(line)
        chunk_length = new_chunk_length

    if chunk_lines:
        print("Sending final changelog to discord")
        send_discord_webhook(chunk_lines)


if __name__ == "__main__":
    main()
