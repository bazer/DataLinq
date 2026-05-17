> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: CLI Init Wizard

**Status:** Draft implementation plan.
**Goal:** Add `datalinq config init`, an interactive setup guide that creates or completes `datalinq.json` and `datalinq.user.json` for new projects and new developer environments.

## Executive Position

`datalinq config init` is a high-leverage CLI feature. DataLinq onboarding is not hard because the generation command is hard to type. It is hard because users need to know:

- which file should contain shared project structure
- which file should contain local connection details and secrets
- what model directory path to choose
- what provider-specific connection details are required
- how to avoid committing local secrets
- what to do when cloning a project that already has `datalinq.json` but no `datalinq.user.json`

An interactive guide is the right tool for that.

The wizard should be boring, explicit, and safe. It should not try to be a magic project generator. Its job is to ask the few questions that matter, write predictable config files, and explain what it did.

Recommended command:

```bash
datalinq config init
```

## Scope for V1

Support one command:

```bash
datalinq config init
```

Do not add separate commands yet:

- `add-database`
- `configure`
- `test-connection`
- `doctor`

Those may be useful later, but command sprawl is premature. `config init` can be smart enough to handle the common setup states.

## Current Code Audit

### Config Discovery and Merge

`DataLinqConfig.FindAndReadConfigs(...)` currently:

1. resolves `datalinq.json`
2. reads it
3. looks for a sibling `.user.json` file by replacing `.json` with `.user.json`
4. reads the user file if present
5. merges the user config over the main config by database name

This is the right two-file model.

### Current Config Shape

Current docs and config use:

```json
"SourceDirectories": [ "Models/Source" ],
"DestinationDirectory": "Models/Generated"
```

but current dev plans propose:

```json
"ModelDirectory": "Models"
```

`config init` should use the new vocabulary once `ModelDirectory` exists. New users should not learn `DestinationDirectory` if it is only a compatibility alias.

### User File Merge Sharp Edge

`ConfigFileDatabase.Connections` currently defaults to an empty list. `DataLinqDatabaseConfig.MergeConfig(...)` replaces the effective connections when a database entry exists in the user file.

Practically, this means a user-file database override should include the full `Connections` array it intends to use. A database entry with only `"Name"` can accidentally clear inherited connections.

`config init` should avoid this footgun by always writing complete connection entries in `datalinq.user.json`.

## Desired User Experience

### New Project

When neither file exists:

```bash
datalinq config init
```

should guide the user through:

1. project/config location
2. database config name
3. database C# type
4. namespace
5. model directory
6. provider
7. local data source name
8. local connection string
9. nullable/reference-generation preferences
10. file-scoped namespace preference
11. whether to add `datalinq.user.json` to `.gitignore`
12. whether to test the connection
13. whether to run `generate models`

Default output:

- write shared structure to `datalinq.json`
- write local connection details to `datalinq.user.json`
- do not store passwords or machine-local connection strings in `datalinq.json`

Example generated shared config:

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "ModelDirectory": "Models",
      "UseNullableReferenceTypes": true,
      "UseFileScopedNamespaces": true
    }
  ]
}
```

Example generated user config:

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "Connections": [
        {
          "Type": "SQLite",
          "DataSourceName": "app.local.db",
          "ConnectionString": "Data Source=app.local.db;Cache=Shared;"
        }
      ]
    }
  ]
}
```

### Existing Main Config, Missing User Config

This is a first-class scenario:

```text
repo has datalinq.json
repo does not have datalinq.user.json
new developer needs local connection settings
```

`datalinq config init` should detect this and switch into "complete local setup" mode.

Behavior:

1. read `datalinq.json`
2. list configured databases
3. ask which databases to configure locally
4. infer provider/data-source defaults from any existing shared connection entries when available
5. prompt for missing local connection details
6. write `datalinq.user.json`
7. optionally add it to `.gitignore`
8. optionally test connections
9. optionally run `validate` or `generate models`

This mode should not rewrite `datalinq.json`.

That matters because existing `datalinq.json` may contain comments and careful formatting. The config reader strips comments before deserialization, but rewriting the file with `System.Text.Json` would destroy them.

### Existing Both Files

When both `datalinq.json` and `datalinq.user.json` exist, `config init` should not blindly rewrite either file.

Recommended V1 behavior:

- read and summarize both files
- report whether each configured database has a usable connection after merge
- offer optional connection testing
- if the user wants changes, print a clear message that editing existing configs is not implemented yet, or offer to regenerate `datalinq.user.json` only after explicit confirmation

V1 should be conservative here. A tool that mangles a commented config file during "init" loses trust instantly.

### Missing Main Config, Existing User Config

This is probably an accidental state.

Behavior:

- warn that `datalinq.user.json` has no matching `datalinq.json`
- offer to create a new main config
- do not assume the user file is authoritative without confirmation

## State Matrix

| `datalinq.json` | `datalinq.user.json` | `config init` mode |
| --- | --- | --- |
| missing | missing | New project bootstrap |
| present | missing | New developer/local environment setup |
| present | present | Inspect and optionally test existing setup |
| missing | present | Repair orphaned user config |

This state detection is what makes `config init` useful beyond first-time project creation.

## Prompt Design

Prompts should be plain and concrete.

Good:

```text
Database config name [AppDb]:
Provider [SQLite/MySQL/MariaDB]:
Model directory [Models]:
Store local connection details in datalinq.user.json? [Y/n]:
```

Bad:

```text
Choose your metadata persistence strategy:
```

Use clear defaults and show the resulting files before writing.

For V1, plain console prompts are acceptable. Do not add a prompt dependency only for this command unless the rest of `DataLinq.CLI` is being moved deliberately from `CommandLineParser` to another command framework.

If password-specific prompts are added, hide password input. If the wizard asks for a full connection string, assume it is visible input and warn that it will be written to `datalinq.user.json`.

## Provider-Specific Guidance

### SQLite

Ask for:

- database file path
- optional cache mode, default `Cache=Shared`

Generate:

```text
Data Source=<path>;Cache=Shared;
```

Default `DataSourceName` can match the file path.

### MySQL and MariaDB

Ask for:

- server/host
- port, with provider default
- database name
- user id
- password
- optional SSL mode if supported/commonly needed

Generate a normal connection string.

The wizard should not try to validate every provider-specific connection-string option in V1. It should produce a sane baseline and allow advanced users to edit the result.

## Gitignore Behavior

If `datalinq.user.json` is created, `config init` should offer to ensure it is ignored by Git.

Recommended behavior:

1. find the nearest `.git` root if available
2. find or create `.gitignore` at that root
3. add a narrow ignore entry for the config-relative user file path

Examples:

```text
datalinq.user.json
src/MyApp/datalinq.user.json
```

Do not add a broad `*.user.json` pattern unless the user explicitly asks. Broad ignores can hide unrelated files.

If no Git repository is detected, say so and skip this step.

## Connection Testing

After writing or previewing config, offer:

```text
Test connection now? [Y/n]
```

Testing should:

- read the merged config through the same config path normal commands use
- use the selected provider
- perform the lightest reliable metadata read available
- avoid writing model files

For SQLite, opening the database file may create a new file depending on provider behavior. The wizard should warn before testing a SQLite path that does not exist.

If testing fails, do not delete the written config. Print the failure and tell the user they can edit `datalinq.user.json` and rerun `datalinq config init` or a future test command.

## Optional Model Generation

After successful config writing and optional connection testing, offer:

```text
Run generate models now? [Y/n]
```

If the batch/recursive plan has landed, this should call the same generation path as `generate models`, not duplicate generation logic.

If the model-directory regeneration plan has landed, `config init` should use `ModelDirectory` and respect `--fresh` semantics if the user chooses a fresh generation.

For V1, it is acceptable to leave generation as a follow-up instruction instead of running it directly if the command plumbing would make `config init` too large.

## File Writing Rules

### New Files

When creating new files, write formatted JSON with stable two-space indentation.

Use explicit fields rather than relying on every default, because onboarding config should teach the shape:

- `Name`
- `CsType`
- `Namespace`
- `ModelDirectory`
- `UseNullableReferenceTypes`
- `UseFileScopedNamespaces`

Do not include `Connections` in the shared file by default.

### Existing Main Config

Do not rewrite existing `datalinq.json` in V1.

Reason: comments and formatting are part of user trust. Since the reader strips comments, the writer cannot round-trip safely without a comment-preserving JSON editor.

### Existing User Config

Do not rewrite existing `datalinq.user.json` without explicit confirmation.

If overwriting is confirmed, show a preview first.

### Dry Preview

Before writing, show:

- files that will be created
- files that will be modified
- database entries that will be added
- whether `.gitignore` will be modified

Then ask for confirmation.

## Suggested Command Options

V1 can be interactive-only, but a few options are useful:

```text
datalinq config init
datalinq config init -c path-or-directory
```

Use existing global `-c` semantics to choose the config file or directory.

Optional future flags:

```text
--no-gitignore
--no-test
--no-generate-models
--force
```

Do not add these until there is real demand. Too many flags make an init wizard feel like a second config language.

## Implementation Plan

### 1. Add `config init` Command

Add a nested command under the `config` group:

```text
datalinq config init
```

If this lands after the System.CommandLine migration, implement it as the `init` subcommand of the `config` command. Do not expose a root-level `init` command.

Keep it separate from generation and target-selection options; `config init` should not require database selection options.

### 2. Resolve Config Paths Without Requiring Existing Files

Current config reading expects `datalinq.json` to exist.

`config init` needs a resolver that can answer:

- intended main config path
- intended user config path
- whether each exists
- base directory

without failing just because the main config is missing.

### 3. Detect Init Mode

Use the state matrix:

- no files: bootstrap
- main only: local setup
- both files: inspect/test
- user only: repair

### 4. Build Prompt Models

Create small DTOs for the wizard output:

```csharp
InitMainConfigPlan
InitUserConfigPlan
InitGitignorePlan
```

The prompt phase should produce a plan. The write phase should execute the plan.

This keeps prompts, validation, preview, and writing from becoming one long procedural method.

### 5. Generate New Config Files

For new projects:

- create `ConfigFile`
- create shared `ConfigFileDatabase` without connections
- create user `ConfigFile` with matching database name and full connection entry
- serialize both

Use `ModelDirectory`, not `DestinationDirectory`, once that config field exists.

If `ModelDirectory` has not landed yet, either sequence this feature after it or use `DestinationDirectory` temporarily with a clear migration note. Sequencing after `ModelDirectory` is cleaner.

### 6. Generate Missing User File From Existing Main Config

For main-only setup:

- parse main config
- for each selected database, ask for local connection entries
- write `datalinq.user.json` containing only database names and full `Connections` arrays

If the main config contains a connection entry, use its provider and data source as defaults. Do not copy a shared password or machine-local connection string blindly; show what is being used as a default.

### 7. Optional Gitignore Update

Implement a small safe helper:

```csharp
GitignorePlan TryCreateGitignorePlan(string userConfigPath)
```

It should:

- detect Git root
- detect existing ignore entry
- append only if missing
- preserve existing file contents

### 8. Optional Connection Test

Reuse provider metadata reading rather than inventing a separate connection stack.

This can be added after file writing so the test exercises exactly what normal commands will read.

### 9. Optional Generate Models

Call the normal `generate models` path if the user opts in.

Do not duplicate generation logic inside `config init`.

### 10. Tests

Add focused tests for:

- config path resolution when no files exist
- state detection matrix
- new project plan creates main and user config
- existing main config plus missing user file creates only user config
- existing main config is not rewritten
- user config connection entries are complete
- `.gitignore` plan uses narrow paths and avoids duplicate entries
- preview reports created/modified files
- `config init` refuses to overwrite existing files without confirmation

For interactive prompts, put business logic behind testable services and keep `Console.ReadLine` thin.

## Documentation Plan

After implementation, update:

- `docs/CLI Documentation.md`
- `docs/Configuration files.md`
- `docs/getting-started/Configuration and Model Generation.md`

Add a short getting-started path:

```bash
datalinq config init
datalinq generate models --database AppDb
```

Also document the new-developer path:

```bash
git clone ...
datalinq config init
```

and explain that when `datalinq.json` already exists, `config init` creates the missing local `datalinq.user.json`.

Do not document `config init` before it ships.

## Non-Goals

- Do not create a full project template system.
- Do not add separate `add-database` or `configure` commands in V1.
- Do not rewrite existing commented `datalinq.json` files.
- Do not store secrets in `datalinq.json` by default.
- Do not add a broad `*.user.json` ignore pattern by default.
- Do not require a database connection test to complete initialization.
- Do not make `config init` depend on solution files.

## Acceptance Criteria

- `datalinq config init` can create `datalinq.json` and `datalinq.user.json` for a new project.
- `datalinq config init` detects an existing `datalinq.json` with missing `datalinq.user.json` and creates the missing user file for local connection settings.
- `datalinq config init` does not rewrite existing `datalinq.json` in V1.
- Generated shared config uses `ModelDirectory` once that field exists.
- Generated user config contains complete connection entries and no shared structural fields beyond database names.
- The wizard offers to add the user config file to `.gitignore` with a narrow path.
- The wizard previews planned writes before applying them.
- Optional connection testing uses the normal provider/config path.
- User-facing docs explain both new-project setup and new-developer local setup.
