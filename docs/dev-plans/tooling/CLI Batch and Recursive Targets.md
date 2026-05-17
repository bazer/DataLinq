> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: CLI Batch and Recursive Targets

**Status:** Draft implementation plan.
**Goal:** Let the DataLinq CLI run the most useful read/generation workflows across many configured databases: `create-models --all/--recursive`, `validate --all/--recursive`, and `list --recursive`.

## Executive Position

This is worth doing, but it needs a real target-expansion layer instead of sprinkling loops into each command.

The useful user stories are simple:

```bash
datalinq create-models --all
datalinq validate --all
datalinq validate --recursive
datalinq list --recursive
```

The implementation should not make those simple commands behave like a pile of single-target commands taped together. Batch mode needs consistent discovery, target naming, output grouping, continuation after errors, and summary exit codes.

The most important behavioral rule:

```text
Batch create-models should keep checking every target, but should not write any model files unless every target can be generated successfully.
```

That makes `create-models --all` and `create-models --recursive` safe enough to use as "refresh the whole repo" commands. If one sample database is broken, the command should report that target and still inspect the rest, but it should leave all model directories untouched.

## Scope for This Slice

Implement batch/recursive support for:

- `create-models --all`
- `create-models --recursive`
- `validate --all`
- `validate --recursive`
- `list --recursive`

Do not implement batch support for:

- `diff`
- `create-sql`
- `create-database`

Those commands have extra output or mutation semantics. They can be designed later instead of forcing this first slice to solve every command family.

## Current Code Audit

### Single-Target Selection

`src/DataLinq.CLI/Program.cs` uses `ConfigFile.GetConnection(...)` for single-target command resolution.

Current rules:

- if the config has more than one database, callers must pass `-n`
- if the selected database has more than one connection type, callers must pass `-t`
- resolved output is one `(DataLinqDatabaseConfig db, DataLinqDatabaseConnection connection)` pair

That is right for single-target commands, but too narrow for batch commands.

### `create-models`

`create-models` currently:

1. reads one config
2. resolves one connection
3. creates one `ModelGenerator`
4. calls `CreateModels(...)`
5. writes files during the generator flow

The generator now materializes rendered files before writing for one target, but the command still writes target by target. Batch mode needs a preflight/render phase for every target before any target writes.

### `validate`

`validate` already has a clean helper shape:

```csharp
private static bool TryValidateSchema(...)
```

and text/JSON output helpers:

```csharp
WriteValidationText(...)
WriteValidationJson(...)
```

Batch validation should reuse the validation engine but produce grouped per-target output and a final summary.

### `list`

`list` currently prints databases and connections, but it also creates a `ModelReader` and reads model/database metadata inside the command. That is surprising for a command named `list`.

For this slice, `list` should become pure config inventory:

- read config files
- print configured databases and connections
- do not connect to databases
- do not parse model files
- do not validate metadata

That makes `list --recursive` cheap and safe.

## Concepts

### Config Discovery

Single-config mode keeps current behavior:

- default config path is `./datalinq.json`
- `-c` / `--config` can be a file or directory
- `datalinq.user.json` is discovered beside each config by existing config-reading rules

Recursive mode:

```bash
datalinq validate --recursive
datalinq create-models --recursive
datalinq list --recursive
```

starts from:

- the directory passed to `--config`, if `--config` is a directory
- the parent directory of the file passed to `--config`, if `--config` is a file
- the current working directory, if `--config` is omitted

It then finds every file named exactly:

```text
datalinq.json
```

under that tree.

Discovery should ignore obvious generated/noisy directories:

- `.git`
- `.vs`
- `.idea`
- `bin`
- `obj`
- `node_modules`
- `artifacts`
- `_site`

Order should be deterministic: sort config paths by normalized full path using ordinal comparison.

`datalinq.user.json` should not be discovered as a standalone config. It remains an override file for its matching `datalinq.json`.

### Command Target

Add an internal target model, for example:

```csharp
public sealed record CliCommandTarget(
    string ConfigPath,
    string ConfigBasePath,
    DataLinqDatabaseConfig Database,
    DataLinqDatabaseConnection Connection,
    string DataSourceName,
    int ConfigIndex,
    int TargetIndex);
```

The exact shape can differ, but every target needs enough information for:

- display names
- logging
- config-relative paths
- command execution
- summary output

Target display should include at least:

```text
relative/path/to/datalinq.json :: AppDb [SQLite] (app.db)
```

For single-config `--all`, the config path can be omitted in normal output if it is visually noisy. For recursive output, always include it.

## Option Semantics

### `--all`

Add `--all` to:

- `create-models`
- `validate`

Meaning:

```text
Run this command for every applicable database target in the selected config.
```

`--all` cannot be combined with `-n` / `--name` in the first implementation. That combination is a filter language, not "all", and it can wait.

`-t` / `--type` may be combined with `--all` as a provider filter.

### `--recursive`

Add `--recursive` to:

- `create-models`
- `validate`
- `list`

Meaning:

```text
Find every datalinq.json under the search root and run the command across them.
```

For `create-models` and `validate`, `--recursive` implies `--all`.

`--recursive` cannot be combined with `-n` / `--name` in the first implementation.

`-t` / `--type` may be combined with `--recursive` as a provider filter.

### Connection Expansion Rules

Connection expansion needs to be deliberately different for `validate` and `create-models`.

#### `validate --all`

Validation is read-only, so it should validate every configured connection by default.

Rules:

- no `-t`: every database/connection pair is a target
- with `-t`: only matching connection types are targets
- if a database has no matching connection under the filter, report a target-selection issue

This is what "validate everything" should mean.

#### `create-models --all`

Model generation writes to the model directory, so multiple connections for the same logical database can conflict.

Rules:

- no `-t`: databases with exactly one connection become targets
- no `-t`: databases with multiple connections produce an ambiguity issue and are not rendered
- with `-t`: select the matching connection for each database
- with `-t`: databases with no matching connection produce a target-selection issue

This preserves the current single-target safety rule while still making the ordinary one-connection-per-database case pleasant.

## Output Behavior

For now, output everything directly to the console. Do not add artifact directories, output templates, or file manifests in this slice.

### Batch Text Output

Each target should have a compact heading:

```text
== src/App/datalinq.json :: AppDb [SQLite] (app.db) ==
```

Then command-specific output follows.

For validation:

```text
Validation target: AppDb [SQLite] (app.db)
Model tables: 12; database tables: 12
No schema drift detected.
```

or existing drift output.

For create-models preflight:

```text
Rendered 14 model files.
```

During the final write phase:

```text
Writing 14 model files.
```

If any target fails before writing:

```text
One or more create-models targets failed. No model files were written.
```

### Summary

Every batch command should end with a summary:

```text
Summary:
  Configs: 4
  Targets: 9
  Succeeded: 7
  Drift: 1
  Failed: 1
```

Use categories that fit the command:

- `list`: configs read, configs failed, databases, connections
- `validate`: succeeded, drift, failed
- `create-models`: rendered, skipped due to errors, written

### JSON Output for Batch Validate

`validate` already supports `--output json`. Do not print multiple independent JSON documents.

For batch mode, emit one aggregate JSON object:

```json
{
  "recursive": true,
  "hasIssues": true,
  "hasDifferences": true,
  "targets": [
    {
      "configPath": "src/App/datalinq.json",
      "database": "AppDb",
      "databaseType": "SQLite",
      "dataSource": "app.db",
      "status": "ok",
      "issues": [],
      "differences": []
    }
  ],
  "summary": {
    "configs": 4,
    "targets": 9,
    "succeeded": 7,
    "drift": 1,
    "failed": 1
  }
}
```

If this is too much for the first code slice, reject `validate --all --output json` and `validate --recursive --output json` with a clear message. But the better product behavior is aggregate JSON.

## Exit Codes

Use the existing single-target meaning where possible:

- `0`: all targets succeeded and no validation drift was found
- `1`: all targets ran successfully, but one or more validation targets had schema drift
- `2`: one or more configs or targets had operational/config/metadata failures

Precedence:

- any failure means exit `2`
- otherwise any validation drift means exit `1`
- otherwise exit `0`

For `create-models`, failures return `2`. There is no drift exit code.

For `list --recursive`, unreadable or invalid config files return `2`, but the command should still list every config it can read.

## Create-Models Write Safety

The desired batch behavior is:

```text
Continue evaluating all targets.
Write nothing unless every target can be generated successfully.
```

To implement that, split generation into two phases.

### Phase 1: Render Plans

Add a non-writing generation path, for example:

```csharp
Option<GeneratedModelWritePlan, IDLOptionFailure> CreateModelWritePlan(...)
```

where the plan contains:

- target display metadata
- generated database metadata
- complete list of `(path, contents)` files
- file encoding

Batch `create-models` should call this for every target and collect every failure.

If any target fails in phase 1:

- print every failure
- print "No model files were written."
- return exit code `2`

### Phase 2: Write Plans

Only after every target has a successful write plan should the command write files.

Single-target `create-models` can use the same path:

```text
plan -> write
```

That avoids preserving two different implementations.

### Brutal Accuracy About Write Failures

Rendering everything before writing guarantees no writes happen when known target/config/rendering failures exist.

It does not magically make the filesystem transactional. A disk permission error, antivirus lock, process crash, or full disk during phase 2 can still leave partial writes unless the writer uses staging and atomic replacement.

The first implementation should at least guarantee "no writes before all targets pass preflight." A stronger later implementation can stage every target's output and then commit via manifests.

## Implementation Plan

### 1. Add Batch Options

Add:

```csharp
public bool All { get; set; }
public bool Recursive { get; set; }
```

to `CreateModelsOptions` and `ValidateOptions`.

Add:

```csharp
public bool Recursive { get; set; }
```

to `ListOptions`.

Do not add these options to `diff`, `create-sql`, or `create-database`.

### 2. Add Config Discovery

Create a helper that returns discovered config paths for a command:

```csharp
IReadOnlyList<DiscoveredConfig> DiscoverConfigs(Options options, bool recursive)
```

Each discovered config should include:

- full config path
- display path
- parsed `DataLinqConfig` or failure

Continue after config failures in recursive mode.

### 3. Add Target Expansion

Add a target-expansion helper that accepts:

- command kind
- discovered configs
- `--all`
- `--recursive`
- optional type filter

and returns:

- valid targets
- target-selection issues

Do not use `DataLinqConfig.GetConnection(...)` for batch expansion. That method is intentionally single-target and should remain that way.

### 4. Refactor `create-models` Into Plan and Write

Refactor `ModelGenerator.CreateModels(...)` so there is a non-writing path.

Possible shape:

```csharp
public Option<GeneratedModelWritePlan, IDLOptionFailure> CreateModelWritePlan(...)
public Option<DatabaseDefinition, IDLOptionFailure> WriteModelPlan(...)
```

or keep writing in `ModelGenerator` but add an internal render method that batch mode can call. The important bit is that batch mode can render every target without writing.

### 5. Implement Batch `create-models`

For `create-models --all` and `create-models --recursive`:

1. discover configs
2. expand targets
3. render every target into write plans
4. print every target result
5. if any failure exists, write nothing and return `2`
6. otherwise write every plan
7. return `0` if all writes succeed, else `2`

Do not stop at the first target failure.

### 6. Implement Batch `validate`

For `validate --all` and `validate --recursive`:

1. discover configs
2. expand targets
3. validate every target
4. print each target result
5. print summary
6. return aggregate exit code

Do not stop at the first validation failure.

### 7. Implement `list --recursive`

Make `list` pure config inventory first.

For normal `list`, print the selected config's databases and connections.

For `list --recursive`, print each discovered config and its configured databases/connections.

Do not use `ModelReader` from `list`.

### 8. Update Tests

Add focused tests for:

- recursive discovery ignores `.git`, `bin`, `obj`, `node_modules`, `artifacts`, and `_site`
- `--recursive` implies all target expansion
- `create-models --all` renders all targets before writing any target
- `create-models --all` writes nothing when any target render fails
- `create-models --all -t SQLite` filters connections
- `create-models --all` reports ambiguity for a database with multiple connections and no `-t`
- `validate --all` validates all connections when no type filter is supplied
- `validate --all -t MariaDB` filters validation targets
- `validate --all` continues after one target failure
- `list --recursive` continues after one config read failure
- batch exit-code precedence: failure beats drift; drift beats success

Prefer unit tests around discovery and target expansion, plus a small number of CLI-level smoke tests.

### 9. Update Docs After Implementation

Update:

- `docs/CLI Documentation.md`
- `docs/Configuration files.md` if recursive config discovery needs a cross-reference
- getting-started docs only if examples would benefit

Do not document these as shipped behavior before implementation.

## Non-Goals

- Do not add recursive/batch support for `diff` in this slice.
- Do not add recursive/batch support for `create-sql` in this slice.
- Do not add recursive/batch support for `create-database` in this slice.
- Do not add output directories, artifact manifests, or output filename templates.
- Do not add a solution-file parser.
- Do not call this `--solution`.
- Do not make recursive discovery depend on `.sln` files.
- Do not stop batch commands at the first target failure.

## Acceptance Criteria

- `create-models --all` runs model generation for all unambiguous database targets in the selected config.
- `create-models --recursive` discovers all `datalinq.json` files under the search root and implies `--all`.
- Batch `create-models` continues rendering after target failures but writes no model files unless every target renders successfully.
- `validate --all` validates all configured connections in the selected config, subject to optional `-t` filtering.
- `validate --recursive` discovers all `datalinq.json` files under the search root and implies `--all`.
- Batch `validate` continues after target failures and returns aggregate exit codes.
- `list --recursive` lists every readable `datalinq.json` under the search root and reports unreadable/invalid configs without stopping discovery.
- Batch output is grouped by target and ends with a clear summary.
- Single-target command behavior remains unchanged when `--all` and `--recursive` are not supplied.
