#!/usr/bin/env python3

"""
Sends updates to a Discord webhook for new changelog entries since the last
GitHub Actions publish run.

Iterates over the four Zona-14 category YAMLs (Zona 14, Admin, Maps, Rules) and
groups the new entries under category headers in a single Discord message.

Automatically figures out the last successful run and the file contents at that
SHA via the GitHub API.
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
    last_sha = _resolve_last_publish_sha(session)
    if last_sha is None:
        print("No prior successful publish run found — skipping discord send (first publish).")
        return

    github_repository = os.environ["GITHUB_REPOSITORY"]
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


def _resolve_last_publish_sha(session: requests.Session) -> str | None:
    github_repository = os.environ["GITHUB_REPOSITORY"]
    github_run = os.environ["GITHUB_RUN_ID"]
    most_recent = get_most_recent_workflow(session, github_repository, github_run)
    if most_recent is None:
        return None
    sha = most_recent.get("head_commit", {}).get("id")
    print(f"Last successful publish job was {most_recent['id']}: {sha}")
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


def get_most_recent_workflow(
    sess: requests.Session, github_repository: str, github_run: str
) -> Any:
    workflow_run = get_current_run(sess, github_repository, github_run)
    past_runs = get_past_runs(sess, workflow_run)
    for run in past_runs["workflow_runs"]:
        # First past successful run that isn't our current run.
        if run["id"] == workflow_run["id"]:
            continue
        return run
    return None


def get_current_run(
    sess: requests.Session, github_repository: str, github_run: str
) -> Any:
    resp = sess.get(
        f"{GITHUB_API_URL}/repos/{github_repository}/actions/runs/{github_run}"
    )
    resp.raise_for_status()
    return resp.json()


def get_past_runs(sess: requests.Session, current_run: Any) -> Any:
    """Get all successful workflow runs before our current one."""
    params = {"status": "success", "created": f"<={current_run['created_at']}"}
    resp = sess.get(f"{current_run['workflow_url']}/runs", params=params)
    resp.raise_for_status()
    return resp.json()


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
