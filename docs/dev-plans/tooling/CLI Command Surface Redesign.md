> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: CLI Command Surface Redesign

**Status:** Draft implementation plan.
**Goal:** Redesign the DataLinq CLI command surface before 1.0 so command names, option names, nested command groups, and parser infrastructure are consistent, understandable, and ready for planned features.

## Executive Position

Do this before 1.0. The current CLI is small enough that a breaking cleanup is still cheap, and the planned features make the old flat command shape awkward.

The main issue is vocabulary:

- `create-models` really generates C# model files.
- `create-sql` really generates a SQL script.
- `create-database` really mutates or creates a database.
- `--type` really means provider.
- `--name` really means configured database name.
- `--output` means format for `validate`, but file path for `diff` and `create-sql`.
- planned `secrets set/list/remove` and `config schema/validate` are naturally nested commands.

The right move is to switch `DataLinq.CLI` to `System.CommandLine`, align it with the other repo CLIs, and use a command grammar that distinguishes generated artifacts from database mutation.

Recommended primary surface:

```text
datalinq init
datalinq list [--recursive]

datalinq generate models [target] [--all] [--recursive] [--fresh] [--overwrite-types] [--stamp-generated-header]
datalinq generate sql [target] --output path
datalinq database create [target]

datalinq validate [target] [--all] [--recursive] [--format text|json]
datalinq diff [target] [--output path]

datalinq config schema [--output path]
datalinq config validate [--format text|json]

datalinq secrets list
datalinq secrets set <name>
datalinq secrets remove <name>
```

This is a better shape than `datalinq create models` because "generate" is the right verb for model files and SQL scripts. Reserve "create" for the database mutation command.

## Current CLI Surface

`src/DataLinq.CLI/Program.cs` currently uses `CommandLineParser` and flat verbs:

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

This works, but it is too flat for:

- `config schema`
- `config validate`
- `secrets list/set/remove`
- `generate models`
- `generate sql`
- `database create`

## Parser Direction

Switch `DataLinq.CLI` from `CommandLineParser` to `System.CommandLine`.

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

Do not keep `CommandLineParser` just to avoid a package change. The rest of the repo has already made the better parser choice.

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
datalinq config schema
datalinq config validate
```

Rationale:

- plain `datalinq schema` is too ambiguous in an ORM CLI
- users may think "schema" means database schema
- `config schema` is precise: it prints the JSON Schema for config files
- `config validate` can validate `datalinq.json`/`datalinq.user.json` without connecting to a database

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

Keep root-level:

```text
datalinq init
datalinq list
datalinq validate
datalinq diff
```

Rationale:

- `init` is a top-level onboarding action
- `list` is a top-level inventory action
- `validate` and `diff` are core workflows, not merely config or model subcommands
- pushing everything under nouns would make common commands longer without adding clarity

## Target Option Names

Use target vocabulary consistently:

```text
-c, --config path
-n, --database name
-p, --provider SQLite|MySQL|MariaDB
--data-source name
```

Compatibility aliases:

```text
--name        -> --database
--type        -> --provider
-t            -> --provider
--datasource  -> --data-source
-d            -> --data-source
```

Do not document the old aliases as primary. They can remain hidden or shown as deprecated depending on `System.CommandLine` support.

### Why `--database`

`--name` is too generic. It means the database entry name in `datalinq.json`, so `--database` is more intuitive.

Keep `-n` as the short alias because `-d` is already overloaded historically and `-n` still means "configured database name."

### Why `--provider`

`--type` is vague. The value is not a .NET type or model type; it is the database provider.

Use `-p` as the new short alias.

### Why `--data-source`

`--datasource` should become `--data-source` for normal CLI spelling.

Do not assign a new short alias in docs. Keep `-d` only as a compatibility alias.

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

Compatibility alias:

```text
validate --output text|json -> validate --format text|json
```

with a deprecation warning.

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
--skip-source -> --fresh
```

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

Deprecated alias:

```text
create-sql
```

### `datalinq database create`

Replaces:

```text
datalinq create-database
```

Options:

```text
target options
```

Deprecated alias:

```text
create-database
```

### `datalinq validate`

Options:

```text
target options
--all
--recursive
--format text|json
```

Deprecated alias:

```text
--output text|json -> --format text|json
```

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

### `datalinq config validate`

Options:

```text
-c, --config path
--format text|json
```

Validates config file shape and semantic config rules without connecting to configured databases.

This command should not replace `validate`, which checks model/database schema drift.

## Compatibility Policy

Because DataLinq is pre-1.0, breaking cleanup is acceptable. Still, compatibility aliases are cheap and humane.

Recommended policy:

1. Implement the new command surface as primary.
2. Keep old flat commands as deprecated aliases for one compatibility window.
3. Print one warning per invocation:

```text
warning DeprecatedCommand: create-models is deprecated. Use generate models.
```

4. Remove old commands in 1.0 or the first explicit breaking release, depending on project policy.

If keeping old aliases makes implementation messy, prefer the clean new surface. Pre-1.0 is exactly when to pay this cost.

## Help Output

Help text should teach the new grammar.

Root help should group commands:

```text
Setup:
  init

Generate:
  generate models
  generate sql

Database:
  database create

Validation:
  validate
  diff

Configuration:
  config schema
  config validate

Secrets:
  secrets list
  secrets set
  secrets remove

Inventory:
  list
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
InitCommand.cs
ListCommand.cs
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
- `list`

Then add planned command stubs or full commands depending on which feature lands first:

- `init`
- `config schema`
- `config validate`
- `secrets list/set/remove`

If a planned command is not implemented yet, do not expose it as a stub that fails. It is better for help output to show only working commands.

### 5. Add Deprecated Aliases

Add old flat commands as compatibility aliases or hidden commands:

- `create-models`
- `create-sql`
- `create-database`

Add deprecated option aliases:

- `--skip-source`
- `--name`
- `--type`
- `--datasource`
- `--output` for validate format

Emit warnings through the new diagnostic style.

### 6. Preserve Exit Codes

Keep current semantics:

- success: `0`
- validation drift: `1`
- operational/config failures: `2`

New parser failures should also return `2`.

### 7. Update Tests

Add tests for:

- new command parsing
- old command aliases
- deprecated option warnings
- target option aliases
- `validate --format json`
- `validate --output json` compatibility
- `generate models --fresh`
- `create-models --skip-source` compatibility
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

## Acceptance Criteria

- `DataLinq.CLI` uses `System.CommandLine`.
- Primary model generation command is `datalinq generate models`.
- Primary SQL script generation command is `datalinq generate sql`.
- Primary database creation command is `datalinq database create`.
- Config schema command is `datalinq config schema`.
- Config validation command is `datalinq config validate` when implemented.
- Secrets commands use `datalinq secrets list/set/remove` when implemented.
- Target options use `--database`, `--provider`, and `--data-source` as primary names.
- `validate` uses `--format` as the primary format option.
- Old flat commands and old option names are either removed deliberately before 1.0 or kept as deprecated aliases with warnings.
- User-facing docs use the new command surface.
