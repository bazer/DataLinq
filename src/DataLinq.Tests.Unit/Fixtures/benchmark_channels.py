import copy
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

spec = importlib.util.spec_from_file_location("channel_policy", "scripts/benchmark_channels.py")
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
policy.load_channels("public/release-channels.json")
channels = {"SchemaVersion": 1, "Stable": {"Branch": "master", "Label": "Stable (master)"},
            "Development": {"Branch": "v0.10", "Label": "0.10 (development)"}}

def run(identifier, branch, at="2024-01-01T00:00:00Z", runtime=".NET 10.0.12", profile="default"):
    return {"RunId": identifier, "GeneratedAtUtc": at,
            "Metadata": {"Branch": branch, "RuntimeDescription": runtime,
                         "Profile": profile, "Filter": "*EmployeesBenchmarks*"},
            "Rows": [{"MeanMicroseconds": 42}]}

old = [run("stable-old", "master"), run("dev-old", "v0.10"),
       run("runtime-old", "master", runtime=".NET 8.0.1"),
       run("profile-old", "master", profile="heavy"),
       run("archived-old", "v0.9"),
       run("stable-newer", "master", "2024-01-02T00:00:00Z")]
original = copy.deepcopy(old)
latest = run("latest", "v0.10", "2026-09-16T00:00:00Z")
history = policy.update_history({"Runs": old}, latest, channels)
ids = {item["RunId"] for item in history["Runs"]}
assert ids == {"dev-old", "runtime-old", "profile-old", "archived-old", "stable-newer", "latest"}, ids
assert old == original
assert policy.update_history(history, latest, channels) == history
try:
    policy.update_history(history, {**latest, "Rows": []}, channels)
    raise AssertionError("Run identity reassignment accepted")
except ValueError:
    pass
rollover = {**channels, "Development": {"Branch": "v0.11", "Label": "0.11 (development)"}}
rolled = policy.update_history(history, run("next", "v0.11", "2026-09-17T00:00:00Z"), rollover)
assert next(item for item in rolled["Runs"] if item["RunId"] == "latest") == latest
try:
    policy.update_history(rolled, run("retired", "v0.10"), rollover)
    raise AssertionError("Retired branch accepted")
except ValueError:
    pass
delayed = policy.update_history(rolled, run("delayed", "master", "2026-09-01T00:00:00Z"), rollover)
assert delayed["GeneratedAtUtc"] == rolled["GeneratedAtUtc"]

def resolve(branch, config):
    environment = {key: value for key, value in os.environ.items() if key != "GITHUB_OUTPUT"}
    result = subprocess.run([sys.executable, "scripts/benchmark_channels.py", "resolve", "--branch", branch, "--config", str(config)],
                            text=True, capture_output=True, check=True, env=environment)
    return dict(line.split("=", 1) for line in result.stdout.splitlines())

with tempfile.TemporaryDirectory() as directory:
    path = Path(directory) / "channels.json"
    path.write_text(json.dumps(channels))
    assert resolve("master", path)["badge_directory"] == ".github/badges"
    assert resolve("v0.10", path)["badge_directory"] == ".github/badges/branches/v0.10"
    assert resolve("v0.9", path)["publish"] == "false"
    assert resolve("codex/0.10-feature", path)["badge_directory"] == ""
    for branch in ("../escape", "master\npublish=true", "master"):
        invalid = {**channels, "Development": {"Branch": branch, "Label": "invalid"}}
        path.write_text(json.dumps(invalid))
        try:
            policy.load_channels(path)
            raise AssertionError(f"Invalid or duplicate branch accepted: {branch}")
        except ValueError:
            pass
print("Retention, run immutability, delayed publication, rollover and channel isolation checks passed.")
