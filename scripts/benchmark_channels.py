"""Shared release-channel and retention policy for CI benchmark publication."""
import argparse
import json
import os
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path


def load_channels(path):
    channels = json.loads(Path(path).read_text(encoding="utf-8"))
    if channels.get("SchemaVersion") != 1:
        raise ValueError("Unsupported release channel schema.")
    branches = []
    for name in ("Stable", "Development"):
        channel = channels.get(name)
        if name == "Development" and channel is None:
            continue
        if not isinstance(channel, dict):
            raise ValueError(f"Missing {name} channel.")
        branch, label = channel.get("Branch"), channel.get("Label")
        if not isinstance(branch, str) or not re.fullmatch(r"master|v[0-9]+\.[0-9]+", branch):
            raise ValueError("Channel branches must be master or v<major>.<minor>.")
        if not isinstance(label, str) or not label.strip() or len(label) > 100 or any(ord(c) < 32 for c in label):
            raise ValueError("Channel labels must be bounded, nonempty single-line text.")
        branches.append(branch)
    if len(branches) != len(set(branches)):
        raise ValueError("Stable and development branches must be distinct.")
    return channels


def channel_for(channels, branch):
    return next((value for name, value in channels.items()
                 if name in ("Stable", "Development") and value and value["Branch"] == branch), None)


def parse_utc(value):
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return parsed.astimezone(timezone.utc) if parsed.tzinfo else None
    except (AttributeError, TypeError, ValueError):
        return None


def update_history(history, latest, channels):
    """Never merge branch/runtime histories or rewrite an archived run's identity."""
    published = history.get("Runs", [])
    if not isinstance(published, list) or not all(isinstance(run, dict) for run in published):
        raise ValueError("Invalid published Runs collection.")
    branch = latest["Metadata"]["Branch"]
    if channel_for(channels, branch) is None:
        raise ValueError("Only an active configured channel may publish.")
    for run in published:
        if run.get("RunId") == latest["RunId"] and run != latest:
            raise ValueError("A published run ID cannot be reassigned.")
    runs = [run for run in published if run.get("RunId") != latest["RunId"]] + [latest]
    runs.sort(key=lambda run: run.get("GeneratedAtUtc", ""))
    # A delayed publication must not move the retention clock backwards.
    newest = max(filter(None, (parse_utc(run.get("GeneratedAtUtc")) for run in runs)))
    retained, thinned = [], {}
    for run in runs:
        at = parse_utc(run.get("GeneratedAtUtc"))
        if at is None or at >= newest - timedelta(days=183):
            retained.append(run)
            continue
        metadata = run.get("Metadata") or {}
        identity = tuple(metadata.get(key, "unknown")
                         for key in ("Branch", "Profile", "RuntimeDescription", "Filter"))
        if at >= newest - timedelta(days=730):
            year, week, _ = at.isocalendar()
            key = ("week", *identity, year, week)
        else:
            key = ("month", *identity, at.year, at.month)
        thinned[key] = run
    result = dict(history)
    result.update(SchemaVersion=3, GeneratedAtUtc=newest.isoformat(),
                  Channels=channels,
                  Runs=sorted(retained + list(thinned.values()), key=lambda run: run.get("GeneratedAtUtc", "")))
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["resolve", "development"])
    parser.add_argument("--config", default="public/release-channels.json")
    parser.add_argument("--branch", default=os.environ.get("GITHUB_REF_NAME", ""))
    args = parser.parse_args()
    channels = load_channels(args.config)
    if args.command == "development":
        print((channels.get("Development") or {}).get("Branch", ""))
        return
    channel = channel_for(channels, args.branch)
    badge_directory = ".github/badges" if args.branch == channels["Stable"]["Branch"] else f".github/badges/branches/{args.branch}"
    values = {"publish": str(channel is not None).lower(), "branch": args.branch,
              "badge_directory": badge_directory if channel else "",
              "stable_branch": channels["Stable"]["Branch"],
              "channels": json.dumps(channels, separators=(",", ":"))}
    output = "\n".join(f"{key}={value}" for key, value in values.items()) + "\n"
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
            stream.write(output)
    else:
        print(output, end="")


if __name__ == "__main__":
    main()
