import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path
from benchmark_channels import update_history

latest_run_path = Path("artifacts/benchmarks/ci/latest-history.json")
baseline_path = Path("artifacts/benchmarks/ci/baseline-same-profile.json")
comparison_path = Path("artifacts/benchmarks/ci/latest-comparison.json")
expected_profile = os.environ.get("BENCHMARK_PROFILE", "default")
expected_filter = os.environ["BENCHMARK_FILTER"]
expected_branch = os.environ["GITHUB_REF_NAME"]
channels = json.loads(os.environ["RELEASE_CHANNELS"])

def fail(message):
    raise SystemExit(message)

def reject_duplicate_properties(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON property: {key}")
        result[key] = value
    return result

def reject_non_finite(value):
    raise ValueError(f"Non-finite JSON number: {value}")

def load_json_object(path, label):
    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=reject_duplicate_properties,
            parse_constant=reject_non_finite)
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        fail(f"{label} is not valid strict JSON: {error}")
    if not isinstance(value, dict):
        fail(f"{label} must be a JSON object.")
    return value

def require_int(container, key, label, expected=None):
    value = container.get(key)
    if type(value) is not int or value < 0:
        fail(f"{label}.{key} must be a non-negative integer.")
    if expected is not None and value != expected:
        fail(f"{label}.{key} must be {expected}, got {value}.")
    return value

def require_bool(container, key, label, expected=None):
    value = container.get(key)
    if type(value) is not bool:
        fail(f"{label}.{key} must be a boolean.")
    if expected is not None and value is not expected:
        fail(f"{label}.{key} must be {expected}, got {value}.")
    return value

def require_text(container, key, label):
    value = container.get(key)
    if not isinstance(value, str) or not value.strip():
        fail(f"{label}.{key} must be a non-empty string.")
    return value

def target_id(target, label):
    if not isinstance(target, dict):
        fail(f"{label} must be an object.")
    provider, method = target_key(target, label)
    category = require_text(target, "Category", label)
    return f"{category}|{provider}|{method}"

def target_key(target, label):
    if not isinstance(target, dict):
        fail(f"{label} must be an object.")
    return (
        require_text(target, "ProviderName", label),
        require_text(target, "Method", label))

def validate_latest_history(report):
    require_int(report, "SchemaVersion", "history", 3)
    if report.get("SchemaId") != "v0.9.benchmark-history.v3":
        fail("history.SchemaId is not v0.9.benchmark-history.v3.")

    run_id = require_text(report, "RunId", "history")
    if re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,255}", run_id) is None:
        fail("history.RunId contains unsafe filename characters.")

    outcome = report.get("Outcome")
    if outcome not in ("Passed", "ReviewRequired"):
        fail(f"history.Outcome must be Passed or ReviewRequired, got {outcome!r}.")
    require_int(report, "OverallExitCode", "history", 0)
    require_bool(report, "IsCompleteForInvocation", "history", True)
    require_bool(report, "ArtifactsComplete", "history", True)
    require_bool(report, "ValidForEvidence", "history", False)
    review_required = require_bool(report, "ReviewRequired", "history")
    if report.get("Failure") is not None:
        fail("history.Failure must be null for a complete run.")

    metadata = report.get("Metadata")
    invocation = report.get("Invocation")
    if not isinstance(metadata, dict) or not isinstance(invocation, dict):
        fail("history Metadata and Invocation must be objects.")
    if metadata.get("Branch") != expected_branch or metadata.get("Commit") != os.environ["GITHUB_SHA"]:
        fail("History must identify the actual workflow branch and commit.")
    if metadata.get("Profile") != expected_profile or metadata.get("Filter") != expected_filter:
        fail("history Metadata does not match this workflow's profile and filter.")
    if not require_text(metadata, "RuntimeDescription", "history.Metadata").startswith(".NET 10."):
        fail("history.Metadata.RuntimeDescription must identify .NET 10.")
    for key in (
            "RunnerOs", "RunnerArchitecture", "RuntimeDescription",
            "ProcessorIdentifier", "BenchmarkDotNetVersion"):
        require_text(metadata, key, "history.Metadata")
    processor_count = require_int(metadata, "ProcessorCount", "history.Metadata")
    if processor_count <= 0:
        fail("history.Metadata.ProcessorCount must be positive.")
    if (invocation.get("Command") != "run" or
            invocation.get("Profile") != expected_profile or
            invocation.get("Filter") != expected_filter):
        fail("history Invocation does not match this workflow's run command, profile, and filter.")
    require_bool(
        invocation, "ReleaseEvidenceIntent", "history.Invocation", False)
    require_bool(invocation, "NoBuild", "history.Invocation", False)
    require_bool(invocation, "KeepFiles", "history.Invocation", False)
    require_bool(invocation, "Verbose", "history.Invocation", False)
    require_bool(invocation, "ArgumentsRedacted", "history.Invocation", False)
    if invocation.get("ExpectedJob") != {
            "default": "ShortRun", "heavy": "MediumRun"}[expected_profile]:
        fail("history.Invocation.ExpectedJob does not match the workflow profile.")
    if invocation.get("SelectedCategory") is not None:
        fail("The diagnostic workflow must not claim a canonical release lane.")
    if invocation.get("ConfiguredProviderIds") != ["sqlite-memory"]:
        fail("history.Invocation.ConfiguredProviderIds does not match the diagnostic provider set.")
    if invocation.get("AdditionalArguments") != [
            "--anyCategories", "stable", "macro-readwrite", "macro-bulk"]:
        fail("history.Invocation.AdditionalArguments does not match the diagnostic category selection.")

    rows = report.get("Rows")
    expected_targets = report.get("ExpectedTargets")
    observed_targets = report.get("ObservedTargets")
    warnings = report.get("Warnings")
    summary = report.get("Summary")
    if not isinstance(rows, list) or not rows:
        fail("history.Rows must be a non-empty array.")
    for index, row in enumerate(rows):
        if not require_text(row, "Runtime", f"history.Rows[{index}]").startswith(".NET 10."):
            fail(f"history.Rows[{index}].Runtime must identify .NET 10.")
    if not isinstance(expected_targets, list) or not isinstance(observed_targets, list):
        fail("history target collections must be arrays.")
    if not isinstance(warnings, list) or not isinstance(summary, dict):
        fail("history Warnings must be an array and Summary must be an object.")

    row_ids = [target_id(row, f"history.Rows[{index}]") for index, row in enumerate(rows)]
    if len(row_ids) != len(set(row_ids)):
        fail("history.Rows contains duplicate benchmark targets.")
    row_keys = [(row["ProviderName"], row["Method"]) for row in rows]
    if len(row_keys) != len(set(row_keys)):
        fail("history.Rows contains duplicate provider and method identities.")
    expected_ids = [
        target_id(target, f"history.ExpectedTargets[{index}]")
        for index, target in enumerate(expected_targets)
    ]
    observed_ids = [
        target_id(target, f"history.ObservedTargets[{index}]")
        for index, target in enumerate(observed_targets)
    ]
    if observed_ids != sorted(row_ids):
        fail("history.ObservedTargets is inconsistent with history.Rows.")

    expected_count = require_int(summary, "ExpectedTargetCount", "history.Summary")
    observed_count = require_int(summary, "ObservedTargetCount", "history.Summary")
    measured_count = require_int(summary, "MeasuredRowCount", "history.Summary")
    invalid_count = require_int(summary, "InvalidRowCount", "history.Summary")
    telemetry_count = require_int(summary, "TelemetryRowCount", "history.Summary")
    warning_count = require_int(summary, "WarningCount", "history.Summary")
    expected_scope_known = require_bool(summary, "ExpectedScopeKnown", "history.Summary")
    exact_target_set = require_bool(summary, "ExactTargetSet", "history.Summary")
    rows_complete = require_bool(summary, "RowsComplete", "history.Summary")

    if expected_count != len(expected_ids):
        fail("history.Summary.ExpectedTargetCount is inconsistent with ExpectedTargets.")
    if observed_count != len(observed_ids) or observed_count != len(rows):
        fail("history.Summary.ObservedTargetCount is inconsistent with ObservedTargets or Rows.")
    if measured_count + invalid_count != len(rows):
        fail("history measured and invalid row counts do not total Rows.")
    if telemetry_count != sum(row.get("TelemetryDelta") is not None for row in rows):
        fail("history.Summary.TelemetryRowCount is inconsistent with Rows.")
    if warning_count != len(warnings):
        fail("history.Summary.WarningCount is inconsistent with Warnings.")
    expected_rows_complete = bool(rows) and invalid_count == 0 and len(row_ids) == len(set(row_ids))
    if rows_complete is not expected_rows_complete or not rows_complete:
        fail("history.Summary.RowsComplete is inconsistent or false.")
    if expected_scope_known is not (expected_count > 0):
        fail("history.Summary.ExpectedScopeKnown is inconsistent with ExpectedTargets.")
    if expected_targets or expected_scope_known or exact_target_set:
        fail("The filtered diagnostic workflow must retain an unknown/noncanonical expected scope.")
    expected_exact_set = expected_count > 0 and expected_ids == observed_ids
    if exact_target_set is not expected_exact_set:
        fail("history.Summary.ExactTargetSet is inconsistent with the target collections.")
    if review_required is not (warning_count > 0):
        fail("history.ReviewRequired is inconsistent with Summary.WarningCount.")
    expected_outcome = "ReviewRequired" if review_required else "Passed"
    if outcome != expected_outcome:
        fail("history.Outcome is inconsistent with ReviewRequired.")

    generated_at_text = require_text(report, "GeneratedAtUtc", "history")
    try:
        generated_at = datetime.fromisoformat(generated_at_text.replace("Z", "+00:00"))
    except ValueError:
        fail("history.GeneratedAtUtc is not a valid timestamp.")
    if generated_at.tzinfo is None:
        fail("history.GeneratedAtUtc must include a UTC offset.")
    return run_id, generated_at.astimezone(timezone.utc)

def validate_comparison(report, latest_run, selected_baseline):
    require_int(report, "SchemaVersion", "comparison", 3)
    if report.get("SchemaId") != "v0.9.benchmark-comparison.v3":
        fail("comparison.SchemaId is not v0.9.benchmark-comparison.v3.")

    outcome = report.get("Outcome")
    if outcome not in ("Passed", "ReviewRequired"):
        fail(f"comparison.Outcome must be Passed or ReviewRequired, got {outcome!r}.")
    require_int(report, "OverallExitCode", "comparison", 0)
    require_bool(report, "IsComplete", "comparison", True)
    require_bool(report, "ArtifactsComplete", "comparison", True)
    require_bool(report, "Comparable", "comparison", True)
    require_bool(report, "ValidForEvidence", "comparison", False)
    review_required = require_bool(report, "ReviewRequired", "comparison")
    if report.get("Failure") is not None:
        fail("comparison.Failure must be null for a complete comparison.")

    latest_run_id = latest_run["RunId"]
    baseline_run_id = require_text(selected_baseline, "RunId", "selected baseline")
    if report.get("CandidateRunId") != latest_run_id:
        fail("comparison.CandidateRunId does not match the current history RunId.")
    if report.get("BaselineRunId") != baseline_run_id:
        fail("comparison.BaselineRunId does not match the selected baseline RunId.")

    baseline_metadata = report.get("Baseline")
    candidate_metadata = report.get("Candidate")
    invocation = report.get("Invocation")
    baseline_artifact = report.get("BaselineArtifact")
    candidate_artifact = report.get("CandidateArtifact")
    if not all(isinstance(value, dict) for value in (
            baseline_metadata, candidate_metadata, invocation,
            baseline_artifact, candidate_artifact)):
        fail("comparison metadata, invocation, and artifact references must be objects.")
    require_bool(
        invocation, "ReleaseEvidenceIntent", "comparison.Invocation", False)
    if (baseline_metadata.get("Profile") != expected_profile or
            candidate_metadata.get("Profile") != expected_profile or
            baseline_metadata.get("Filter") != expected_filter or
            candidate_metadata.get("Filter") != expected_filter):
        fail("comparison profile or filter does not match this workflow invocation.")
    if baseline_artifact.get("RunId") != baseline_run_id:
        fail("comparison.BaselineArtifact.RunId does not match the selected baseline.")
    if candidate_artifact.get("RunId") != latest_run_id:
        fail("comparison.CandidateArtifact.RunId does not match the current history.")
    require_int(candidate_artifact, "SchemaVersion", "comparison.CandidateArtifact", 3)
    if candidate_artifact.get("SchemaId") != "v0.9.benchmark-history.v3":
        fail("comparison.CandidateArtifact does not reference a v3 history artifact.")
    current_metadata = latest_run["Metadata"]
    for key in (
            "RunnerOs", "RunnerArchitecture", "RuntimeDescription",
            "ProcessorIdentifier", "BenchmarkDotNetVersion", "ProcessorCount"):
        if candidate_metadata.get(key) != current_metadata.get(key):
            fail(f"comparison.Candidate.{key} does not match current history metadata.")
        if candidate_artifact.get(key) != current_metadata.get(key):
            fail(f"comparison.CandidateArtifact.{key} does not match current history metadata.")
    candidate_source_review = require_bool(
        candidate_artifact, "ReviewRequired", "comparison.CandidateArtifact")
    if candidate_source_review is not latest_run["ReviewRequired"]:
        fail("comparison.CandidateArtifact.ReviewRequired does not match current history.")
    selected_schema = require_int(
        selected_baseline, "SchemaVersion", "selected baseline")
    if selected_schema < 1 or selected_schema > 3:
        fail("selected baseline has an unsupported schema version.")
    require_int(
        baseline_artifact, "SchemaVersion", "comparison.BaselineArtifact", selected_schema)
    baseline_legacy = require_bool(
        baseline_artifact, "LegacySchema", "comparison.BaselineArtifact")
    if baseline_legacy is not (selected_schema < 3):
        fail("comparison.BaselineArtifact.LegacySchema is inconsistent with the selected baseline.")
    baseline_source_review = require_bool(
        baseline_artifact, "ReviewRequired", "comparison.BaselineArtifact")
    if selected_schema == 3 and baseline_source_review is not selected_baseline.get("ReviewRequired"):
        fail("comparison.BaselineArtifact.ReviewRequired does not match the selected baseline.")
    if selected_schema == 3:
        selected_metadata = selected_baseline.get("Metadata")
        if not isinstance(selected_metadata, dict):
            fail("selected v3 baseline Metadata must be an object.")
        for key in (
                "RunnerOs", "RunnerArchitecture", "RuntimeDescription",
                "ProcessorIdentifier", "BenchmarkDotNetVersion", "ProcessorCount"):
            if selected_metadata.get(key) != current_metadata.get(key):
                fail(f"comparison v3 environment mismatch for {key}.")
            if baseline_metadata.get(key) != selected_metadata.get(key):
                fail(f"comparison.Baseline.{key} does not match the selected baseline.")
            if baseline_artifact.get(key) != selected_metadata.get(key):
                fail(f"comparison.BaselineArtifact.{key} does not match the selected baseline.")

    rows = report.get("Rows")
    counts = report.get("StatusCounts")
    if not isinstance(rows, list) or not rows:
        fail("comparison.Rows must be a non-empty array.")
    if not isinstance(counts, dict):
        fail("comparison.StatusCounts must be an object.")

    status_names = {
        "stable": "Stable",
        "improved": "Improved",
        "warning": "Warning",
        "noisy": "Noisy",
        "missing-baseline": "MissingBaseline",
        "missing-candidate": "MissingCandidate",
        "profile-mismatch": "ProfileMismatch",
        "scope-mismatch": "ScopeMismatch",
        "invalid": "Invalid",
    }
    recomputed = {name: 0 for name in status_names.values()}
    latency_warnings = 0
    allocation_warnings = 0
    telemetry_changes = 0
    row_ids = []
    for index, row in enumerate(rows):
        row_ids.append(target_id(row, f"comparison.Rows[{index}]"))
        status = row.get("Status")
        if status not in status_names:
            fail(f"comparison.Rows[{index}].Status is invalid: {status!r}.")
        recomputed[status_names[status]] += 1
        latency_warnings += row.get("LatencyStatus") == "warning"
        allocation_warnings += row.get("AllocationStatus") == "warning"
        telemetry_changes += row.get("TelemetryStatus") == "changed"
    if len(row_ids) != len(set(row_ids)):
        fail("comparison.Rows contains duplicate benchmark targets.")
    row_keys = [(row["ProviderName"], row["Method"]) for row in rows]
    if len(row_keys) != len(set(row_keys)):
        fail("comparison.Rows contains duplicate provider and method identities.")
    baseline_rows = selected_baseline.get("Rows")
    if not isinstance(baseline_rows, list) or not baseline_rows:
        fail("selected baseline Rows must be a non-empty array.")
    baseline_ids = [
        target_key(row, f"selected baseline.Rows[{index}]")
        for index, row in enumerate(baseline_rows)
    ]
    comparison_keys = sorted((row["ProviderName"], row["Method"]) for row in rows)
    candidate_keys = sorted((row["ProviderName"], row["Method"]) for row in latest_run["Rows"])
    if comparison_keys != candidate_keys or comparison_keys != sorted(baseline_ids):
        fail("comparison.Rows does not exactly cover the baseline and candidate targets.")

    require_int(counts, "Total", "comparison.StatusCounts", len(rows))
    for key, value in recomputed.items():
        require_int(counts, key, "comparison.StatusCounts", value)
    require_int(counts, "LatencyWarnings", "comparison.StatusCounts", latency_warnings)
    require_int(counts, "AllocationWarnings", "comparison.StatusCounts", allocation_warnings)
    require_int(counts, "TelemetryChanges", "comparison.StatusCounts", telemetry_changes)

    overall_total = sum(recomputed.values())
    if overall_total != len(rows):
        fail("comparison overall status counts do not total Rows.")
    for key in ("MissingBaseline", "MissingCandidate", "ProfileMismatch", "ScopeMismatch", "Invalid"):
        if counts[key] != 0:
            fail(f"comparison.StatusCounts.{key} must be zero for a comparable report.")
    warning_count = require_int(report, "WarningCount", "comparison", counts["Warning"])
    expected_review = (
        baseline_legacy or baseline_source_review or candidate_source_review or
        warning_count > 0 or counts["Noisy"] > 0 or telemetry_changes > 0)
    if review_required is not expected_review:
        fail("comparison.ReviewRequired is inconsistent with its baseline and status counts.")
    expected_outcome = "ReviewRequired" if review_required else "Passed"
    if outcome != expected_outcome:
        fail("comparison.Outcome is inconsistent with ReviewRequired.")

if not latest_run_path.exists():
    raise SystemExit("Missing latest benchmark history artifact.")

latest_run = load_json_object(latest_run_path, "latest benchmark history")
latest_run_id, latest_generated_at = validate_latest_history(latest_run)

selected_baseline = None
comparison = None
if baseline_path.exists():
    selected_baseline = load_json_object(baseline_path, "selected benchmark baseline")
    if not comparison_path.exists():
        fail("A baseline was selected but the comparison artifact is missing.")
    comparison = load_json_object(comparison_path, "latest benchmark comparison")
    validate_comparison(comparison, latest_run, selected_baseline)
elif comparison_path.exists():
    fail("A comparison artifact exists even though no baseline was selected.")

data_root = Path("benchmark-data-worktree/benchmarks")
runs_root = data_root / "runs"
data_root.mkdir(parents=True, exist_ok=True)
runs_root.mkdir(parents=True, exist_ok=True)

def write_json(path, value):
    temporary_path = path.with_name(f".{path.name}.tmp")
    temporary_path.write_text(
        json.dumps(value, indent=2, allow_nan=False) + "\n",
        encoding="utf-8")
    temporary_path.replace(path)

history_path = data_root / "history.json"
if history_path.exists():
    history = load_json_object(history_path, "published aggregate benchmark history")
else:
    history = {
        "SchemaVersion": 1,
        "GeneratedAtUtc": latest_run["GeneratedAtUtc"],
        "Runs": []
    }

history = update_history(history, latest_run, channels)
write_json(history_path, history)
branch_root = data_root / "branches" / expected_branch
branch_root.mkdir(parents=True, exist_ok=True)
write_json(branch_root / "latest.json", latest_run)
if expected_branch == channels["Stable"]["Branch"]:
    write_json(data_root / "latest.json", latest_run)

timestamp = latest_generated_at.strftime("%Y%m%dT%H%M%S.%fZ")
run_file_name = f"{timestamp}-{latest_run_id}.json"
if (runs_root / run_file_name).exists():
    if load_json_object(runs_root / run_file_name, "existing immutable run") != latest_run:
        fail("An immutable published run cannot be overwritten.")
write_json(runs_root / run_file_name, latest_run)

published_comparison_path = branch_root / "latest-comparison.json"
if comparison is not None:
    write_json(published_comparison_path, comparison)
else:
    published_comparison_path.unlink(missing_ok=True)

if expected_branch == channels["Stable"]["Branch"]:
    stable_comparison_path = data_root / "latest-comparison.json"
    if comparison is not None:
        write_json(stable_comparison_path, comparison)
    else:
        stable_comparison_path.unlink(missing_ok=True)
