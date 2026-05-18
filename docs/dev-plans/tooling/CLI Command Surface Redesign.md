> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: CLI Command Surface Redesign

**Status:** Implemented with two deliberate follow-ups.
**Goal:** Keep the DataLinq CLI command surface consistent, understandable, and ready for planned features before 1.0.

## Executive Position

This cleanup has landed and should stay landed before 1.0. The old flat command vocabulary was too vague, and carrying it forward would have made every later CLI feature harder to explain.

The main issue is vocabulary:

- `create-models` really generates C# model files.
- `create-sql` really generates a SQL script.
- `create-database` really mutates or creates a database.
- `--type` really means provider.
- `--name` really means configured database name.
- `--output` means format for `validate`, but file path for `diff` and `create-sql`.
- `list` lists config entries, not live server databases.
- `init` initializes config files, not a global DataLinq environment.
- planned `secrets set/list/remove` and `config schema/validate` are naturally nested commands.

The right move is to switch `DataLinq.CLI` to `System.CommandLine`, align it with the other repo CLIs, and use a command grammar that distinguishes generated artifacts from database mutation.

Current primary surface:

```text
datalinq generate models [target] [--all] [--recursive] [--fresh] [--overwrite-types] [--stamp-generated-header]
datalinq generate sql [target] --output path
datalinq database create [target]

datalinq validate [target] [--all] [--recursive] [--format text|json]
datalinq diff [target] [--output path]

datalinq config init
datalinq config list [--recursive]
datalinq config schema [--output path]

datalinq secrets list
datalinq secrets set <name>
datalinq secrets remove <name>
```

This is a better shape than `datalinq create models` because "generate" is the right verb for model files and SQL scripts. Reserve "create" for the database mutation command. `init` and `list` belong under `config` because they create, complete, or inspect DataLinq config files.

## Current Implementation Status

Implemented:

- `src/DataLinq.CLI` now uses `System.CommandLine`.
- Primary commands are nested as `generate models`, `generate sql`, `database create`, `config init`, `config list`, `config schema`, and `secrets list/set/remove`.
- `validate` and `diff` remain root workflow commands.
- Target options now use `--database`, `--provider`, and `--data-source`; `-n` and `-p` are retained as the useful short aliases.
- `validate` uses `--format text|json`; old `validate --output json` is rejected.
- The only kept flat compatibility command is `create-models`, and it warns as deprecated.
- User-facing docs now describe the new command surface.

Not implemented, and still worth doing:

- `datalinq config validate`
  This should validate config shape and semantic rules without opening a database. The JSON Schema catches editor-time shape errors, but the CLI still needs an offline command that checks merged `datalinq.json`/`datalinq.user.json`, provider names, model-directory conflicts, missing names, connection-entry sanity, and secret-reference resolution behavior.
- `Program.cs` decomposition
  The parser migration landed, but the command wiring and command handlers still live mostly in one large `Program.cs`. Split it when doing the next CLI slice, not as busywork: `GenerateCommand`, `DatabaseCommand`, `ValidateCommand`, `DiffCommand`, `ConfigCommand`, `SecretsCommand`, and shared target/output helpers would be the obvious shape.

## Legacy CLI Surface At Plan Creation

Before this work, `src/DataLinq.CLI/Program.cs` used `CommandLineParser` and flat verbs:

```text
create-database
create-sql
create-models
validate
diff
list
```

Current shared target options:

```text
-c, --config
-n, --name
-t, --type
-d, --datasource
```

Current command-specific options:

```text
create-models --skip-source
create-models --overwrite-types
create-models --stamp-generated-header
validate --output text|json
diff -o, --output path
create-sql -o, --output path
```

This works, but it is too flat and too vague for:

- `config init`
- `config list`
- `config schema`
- `config validate`
- `secrets list/set/remove`
- `generate models`
- `generate sql`
- `database create`

## Parser Direction

Implemented: `DataLinq.CLI` switched from `CommandLineParser` to `System.CommandLine`.

Reasons:

- `System.CommandLine` already exists in repo CLIs:
  - `src/DataLinq.Dev.CLI`
  - `src/DataLinq.Testing.CLI`
  - `src/DataLinq.Benchmark.CLI`
- nested command groups are natural
- aliases are straightforward
- option validation is easier to centralize
- help output is more flexible
- planned command families map cleanly to subcommands

Do not reintroduce `CommandLineParser` just to avoid parser cleanup. The rest of the repo has already made the better parser choice.

## Command Grammar

### Generate Group

Use:

```text
datalinq generate models
datalinq generate sql
```

Rationale:

- model files are generated artifacts
- SQL script files are generated artifacts
- "generate" avoids implying database mutation
- both commands can share artifact-output language in docs

Do not use:

```text
datalinq create models
datalinq create sql
```

That makes `create database` look equivalent to file generation, which is misleading.

### Database Group

Use:

```text
datalinq database create
```

Rationale:

- this command mutates or creates an actual database
- grouping under `database` makes the risk clearer
- future database-focused commands can live nearby if needed

### Config Group

Use:

```text
datalinq config init
datalinq config list
datalinq config schema
```

Rationale:

- `config init` creates or completes `datalinq.json` and `datalinq.user.json`
- `config list` lists configured database entries and connections
- plain `datalinq schema` is too ambiguous in an ORM CLI
- users may think "schema" means database schema
- `config schema` is precise: it prints the JSON Schema for config files
- `config validate` remains the right future name for offline config validation, but it is not currently exposed

### Secrets Group

Use:

```text
datalinq secrets list
datalinq secrets set <name>
datalinq secrets remove <name>
```

Rationale:

- this is naturally a noun command group
- nesting avoids ugly flat names like `secrets-set`
- it aligns with common CLI conventions

### Root Commands

Keep only the workflow commands root-level:

```text
datalinq validate
datalinq diff
```

Rationale:

- `validate` and `diff` are core model/database workflows
- `init` is config initialization, so it belongs under `config`
- `list` is config inventory, so it belongs under `config`
- keeping only actual workflows root-level makes root help cleaner

## Target Option Names

Use target vocabulary consistently:

```text
-c, --config path
-n, --database name
-p, --provider SQLite|MySQL|MariaDB
--data-source name
```

### Why `--database`

`--name` is too generic. It means the database entry name in `datalinq.json`, so `--database` is more intuitive.

Use `-n` as the short option because `-d` is already overloaded historically and `-n` still means "configured database name."

### Why `--provider`

`--type` is vague. The value is not a .NET type or model type; it is the database provider.

Use `-p` as the short option.

### Why `--data-source`

`--datasource` should become `--data-source` for normal CLI spelling.

Do not assign a short option for this in V1. Do not keep `--datasource` or `-d`; the new spelling is `--data-source`.

## Output Options

Separate file output from format output.

Use:

```text
--output path
```

only for file paths.

Use:

```text
--format text|json
```

for output format.

Changes:

- `validate --output text|json` becomes `validate --format text|json`
- `diff --output path` stays file output
- `generate sql --output path` stays file output
- `config validate --format text|json`

## Planned Command Details

### `datalinq generate models`

Replaces:

```text
datalinq create-models
```

Options:

```text
target options
--all
--recursive
--fresh
--overwrite-types
--stamp-generated-header
```

Deprecated aliases:

```text
create-models
```

`create-models` is the only old command alias worth keeping initially because it is the current core generation command and is likely to appear in early scripts and docs.

### `datalinq generate sql`

Replaces:

```text
datalinq create-sql
```

Options:

```text
target options
-o, --output path
```

No deprecated `create-sql` alias in the new pre-1.0 surface.

### `datalinq database create`

Replaces:

```text
datalinq create-database
```

Options:

```text
target options
```

No deprecated `create-database` alias in the new pre-1.0 surface.

### `datalinq validate`

Options:

```text
target options
--all
--recursive
--format text|json
```

No deprecated `--output` format alias in the new pre-1.0 surface. `--output` should mean file path everywhere it appears.

### `datalinq diff`

Keep root command.

Options:

```text
target options
-o, --output path
```

No batch/recursive support in the first batch-target slice.

### `datalinq config schema`

Options:

```text
-o, --output path
```

Prints or writes the JSON Schema for DataLinq config files.

### Future `datalinq config validate`

Options:

```text
-c, --config path
--format text|json
```

Validates config file shape and semantic config rules without connecting to configured databases.

This command should not replace `validate`, which checks model/database schema drift.

## Compatibility Policy

Because DataLinq is pre-1.0, breaking cleanup is acceptable and preferable to carrying confusing aliases.

Recommended policy:

1. Implement the new command surface as primary.
2. Keep only `create-models` as a deprecated alias for `generate models`.
3. Do not keep root `init`, root `list`, `create-sql`, `create-database`, or old option names.
4. Print one warning when the `create-models` alias is used:

```text
warning DeprecatedCommand: create-models is deprecated. Use generate models.
```

5. Remove the `create-models` alias in 1.0 or the first explicit breaking release, depending on project policy.

If keeping the `create-models` alias makes implementation messy, prefer the clean new surface. Pre-1.0 is exactly when to pay this cost.

## Help Output

Help text should teach the new grammar.

Root help should group commands:

```text
Generate:
  generate models
  generate sql

Database:
  database create

Validation:
  validate
  diff

Configuration:
  config init
  config list
  config schema
  config validate   (future)

Secrets:
  secrets list
  secrets set
  secrets remove

```

If `System.CommandLine` default help cannot group commands this way easily, keep the default help for the first code slice and document the grouped surface in docs. Do not overbuild custom help before the command behavior is stable.

## Implementation Plan

### 1. Add System.CommandLine to DataLinq.CLI

Update:

```text
src/DataLinq.CLI/DataLinq.CLI.csproj
```

Remove:

```text
CommandLineParser
```

Add:

```text
System.CommandLine
```

Use the same package style as the other repo CLIs.

### 2. Split Program.cs

Do not cram the new command tree into one giant `Program.cs`.

Recommended files:

```text
Program.cs
CommandOptions.cs
TargetOptions.cs
GenerateCommand.cs
DatabaseCommand.cs
ValidateCommand.cs
DiffCommand.cs
ConfigCommand.cs
SecretsCommand.cs
```

This is not abstraction theater. A nested command surface in one file will become unreadable fast.

### 3. Centralize Target Binding

Create shared target options and binding helpers:

```csharp
TargetOptions
TargetSelection
TargetResolver
```

Target options should be declared once and reused by:

- `generate models`
- `generate sql`
- `database create`
- `validate`
- `diff`

### 4. Implement New Commands First

Wire new primary commands:

- `generate models`
- `generate sql`
- `database create`
- `validate`
- `diff`
- `config init`
- `config list`

Then add planned command stubs or full commands depending on which feature lands first:

- `config validate`

If a planned command is not implemented yet, do not expose it as a stub that fails. It is better for help output to show only working commands.

### 5. Add the One Deprecated Alias

Add exactly one old flat command as a deprecated compatibility command:

- `create-models`

Emit warnings through the new diagnostic style.

Do not add deprecated aliases for the old option names. Since the CLI is pre-1.0, the cleaner behavior is to fail and show the new option names in help.

### 6. Preserve Exit Codes

Keep current semantics:

- success: `0`
- validation drift: `1`
- operational/config failures: `2`

New parser failures should also return `2`.

### 7. Update Tests

Add tests for:

- new command parsing
- `create-models` deprecated command alias
- missing old option aliases fail with useful parser output
- `validate --format json`
- `generate models --fresh`
- command help includes new commands

Existing `CliDiagnosticWriterTests` should be updated by the diagnostics-output plan.

### 8. Update Docs After Implementation

Update:

- `docs/CLI Documentation.md`
- `docs/Configuration files.md` where command examples appear
- getting-started docs
- backend docs with `create-models` examples
- dev plans that would otherwise teach old command names

Before implementation lands, dev-plan docs may continue mentioning old commands as "current state." After implementation, user-facing docs must use the new surface.

## Non-Goals

- Do not add batch/recursive support for `diff`, `generate sql`, or `database create` in this slice.
- Do not implement cloud secret providers in this slice.
- Do not build fully custom help rendering before basic command behavior is stable.
- Do not keep `CommandLineParser` after nested commands are introduced.
- Do not rename `validate` or `diff`; they are already clear.
- Do not use `datalinq schema`; use `datalinq config schema`.
- Do not keep root `init` or root `list`; use `config init` and `config list`.
- Do not keep `create-sql`, `create-database`, or old target option aliases in the new pre-1.0 surface.

## Acceptance Criteria

- `DataLinq.CLI` uses `System.CommandLine`.
- Primary model generation command is `datalinq generate models`.
- Primary SQL script generation command is `datalinq generate sql`.
- Primary database creation command is `datalinq database create`.
- Config schema command is `datalinq config schema`.
- Config validation command is `datalinq config validate` when implemented.
- Config initialization command is `datalinq config init`.
- Config inventory command is `datalinq config list`.
- Secrets commands use `datalinq secrets list/set/remove`.
- Target options use `--database`, `--provider`, and `--data-source` as primary names.
- `validate` uses `--format` as the primary format option.
- Only `create-models` is kept as a temporary deprecated alias for `generate models`.
- Old option names are removed instead of carried as aliases.
- User-facing docs use the new command surface.
