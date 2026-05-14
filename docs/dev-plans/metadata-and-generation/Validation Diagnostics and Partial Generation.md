> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Validation Diagnostics and Partial Generation

**Status:** Draft implementation plan.
**Goal:** Make DataLinq validation and generation report every independent error they can find, while keeping output safe: CLI commands must not write generated files or scripts when validation has errors, and the source generator must still emit useful generated code for valid databases or tables without producing secondary compiler noise.

## Executive Position

This is the right direction. Fail-fast validation is cheap for the implementation, but it is a hostile user experience for schema and model work. A user fixing ten broken attributes, two duplicate columns, and one bad relation should not need thirteen build or CLI runs to discover the list.

The nuance is that "all errors" cannot mean literally every possible error after every possible failure. Some failures destroy the context needed for later checks. A missing config file, an unreadable source directory, a failed database connection, or a database class that cannot be identified is a branch-level blocker. But once DataLinq has enough structure to keep walking tables, columns, attributes, indexes, and relations, it should collect all scoped errors before returning.

The intended behavior is therefore:

- **Collect all independent errors within the reachable branch.**
- **Do not mutate the filesystem when validation or rendering has errors.**
- **Do not suppress valid source-generator output for unrelated databases or unrelated tables.**
- **Do not emit generated code that causes avoidable secondary C# errors.**

## Current Code Audit

The schema drift comparer already behaves the way we want after both metadata graphs exist. `SchemaComparer.Compare(...)` accumulates differences into a list instead of returning on the first missing table, column, index, relation, check, or comment mismatch.

The fail-fast behavior is mostly before or after that comparer:

- `src/DataLinq.CLI/Program.cs`
  - `create-models` calls `ModelGenerator.CreateModels(...)` and exits on the first returned failure.
  - `validate` and `diff` call `TryValidateSchema(...)`; if metadata loading fails, the command prints one failure object and returns exit code `2`.
  - `diff` writes the script only after validation succeeds, which is good, but it cannot currently return a structured list of metadata issues.
- `src/DataLinq.Tools/SchemaValidator.cs`
  - `GetModelPaths(...)` aggregates missing path failures, but model metadata parsing, provider metadata parsing, and metadata build failures still return as soon as one stage fails.
- `src/DataLinq.Tools/ModelGenerator.cs`
  - provider metadata parsing, source model parsing, name matching, transformation, rendering, and file writes are one sequential path.
  - `File.WriteAllText(...)` happens while enumerating generated files. If rendering or I/O fails after earlier files have been written, the output directory can be left partially updated.
- `src/DataLinq.Tools/Factories/MetadataFromFileFactory.cs`
  - file reading returns on the first unreadable path or file-processing exception.
  - Roslyn model parsing can aggregate failures from `MetadataFromModelsFactory`, but the result is still a single failure returned to callers.
- `src/DataLinq.SharedCore/Factories/Generator/MetadataFromModelsFactory.cs`
  - parsing can aggregate invalid attributes and properties within a model.
  - a database still becomes one `Option<DatabaseDefinition, IDLOptionFailure>`, so any database-level failure prevents generation for all tables in that database.
- `src/DataLinq.SharedCore/Factories/MetadataDefinitionFactory.cs`
  - `BuildCore(...)` is a long fail-fast chain. Every validation or resolution step returns immediately on its first failure.
  - several checks are naturally aggregatable, such as duplicate tables, duplicate columns, invalid names, invalid defaults, duplicate attributes, missing primary keys, invalid indexes, and unresolved relations.
  - some steps mutate the graph, such as name normalization, index parsing, relation parsing, column indexing, and freezing. Those need care before we can run later checks after earlier failures.
- `src/DataLinq.MySql/MySql/MetadataFromMySqlFactory.cs`, `src/DataLinq.MySql/MariaDB/MetadataFromMariaDBFactory.cs`, and `src/DataLinq.SQLite/MetadataFromSQLiteFactory.cs`
  - table and column parsing already use `Transpose()` and can aggregate table or column parse failures.
  - index parsing, relation parsing, missing include checks, and final metadata build are still stage-fail-fast.
- `src/DataLinq.Generators/ModelGenerator.cs`
  - the incremental generator loops over all database metadata results, so one broken database does not inherently stop unrelated databases.
  - failed metadata produces one `DLG001` diagnostic for the aggregate failure.
  - generated file creation is wrapped in one database-wide `try/catch`; a table rendering failure becomes one `DLG002` and aborts the rest of that database.
  - `AddSource(...)` happens as files are enumerated, so earlier generated tables may already be emitted before a later table throws.
- `src/DataLinq.Generators/Validation/DefaultValueCompatibilityValidator.cs`
  - this is the closest current example of the desired behavior: it reports every invalid default it can find and suppresses only the bad generated default assignment.

## Desired Behavior

### CLI `validate`

`datalinq validate` should report all reachable issues and all schema drift differences:

1. Read config and resolve the requested connection.
2. Parse configured source and generated model paths, collecting all path and file-read failures.
3. Parse all reachable model metadata, collecting all model issues.
4. Read live provider metadata when the connection branch is usable, collecting all provider issues.
5. Build model and provider metadata reports.
6. If both sides have valid metadata, compare them and report all drift differences.
7. Return:
   - `0` when no errors and no drift differences exist.
   - `1` when metadata is valid but schema drift differences exist.
   - `2` when validation issues prevent a trustworthy comparison.

Text output should group issues before drift:

```text
Validation target: Employees [MySQL] (employees)
Validation errors: 4

Model errors:
Error InvalidAttribute Models/Employee.cs:23 [Employee.BirthDate]: Invalid DefaultAttribute value ...
Error DuplicateColumn Models/Employee.cs:41 [Employee.Name]: Duplicate column definition for 'name' ...

Provider errors:
Error UnsupportedColumnType employees.audit.payload: Unsupported MySQL column type 'geometry' ...

Schema drift was not compared because validation errors were found.
```

JSON output should add an `issues` array alongside the existing `differences` array. Do not force downstream tooling to scrape formatted `IDLOptionFailure.ToString()` output.

### CLI `diff`

`datalinq diff` should share the same validation runner as `validate`.

If validation issues exist, it must report all issues and write no script. If metadata is valid, it should keep the current conservative behavior: generate script output from the full difference set.

### CLI `create-models`

`datalinq create-models` has a stronger safety requirement than `validate` because it writes files.

Required behavior:

- collect provider metadata issues and source merge issues before rendering
- render every candidate output into memory first
- collect all rendering failures before writing
- if any error exists, print all errors and write nothing
- write files only after validation and rendering have succeeded

The current implementation writes during enumeration. That is too easy to corrupt a generated model directory. The first implementation can simply materialize `CreateModelFiles(...)` into an in-memory list before writing. A later hardening pass should write to a staging directory and then replace files from a manifest, because disk errors can still happen mid-write.

Warnings are different. Unsupported provider details that are intentionally skipped, such as unsupported expression indexes, should remain warnings and should not block generation unless they make the generated model misleading.

### Source Generator

The source generator should maximize useful output while keeping the compilation clean.

Required behavior:

- process every discovered database metadata result
- report every metadata issue as a separate Roslyn diagnostic when it has a useful source location
- continue generating unrelated valid databases
- continue generating valid table-level outputs inside a partially invalid database when the valid table graph is sufficient
- avoid generated C# errors caused by broken tables, missing generated files, or incomplete metadata bootstraps

Database-level bootstrap output needs a clear rule. My recommendation is strict:

- generate table files for valid tables when possible
- suppress the database metadata bootstrap when the database has table-level or relation-level errors that make the whole graph incomplete
- generate the bootstrap only when the database graph is coherent enough for runtime initialization

That is less magical and safer than emitting a database bootstrap with a silent subset of tables. Since generator diagnostics are errors, the compile still fails, but the IDE can still show generated code for the valid tables and the user does not get a pile of secondary `CS0246` or `CS0534` style noise.

## Error Model

Add a normalized issue layer instead of making every caller understand nested `IDLOptionFailure` strings.

Proposed shape:

```csharp
public enum ValidationIssueSeverity
{
    Info,
    Warning,
    Error
}

public enum ValidationIssueScope
{
    Config,
    File,
    Database,
    Table,
    Column,
    Property,
    Index,
    Relation,
    Output
}

public sealed record ValidationIssue(
    string Code,
    ValidationIssueSeverity Severity,
    ValidationIssueScope Scope,
    string Message,
    SourceLocation? SourceLocation,
    string? DatabaseName = null,
    string? TableName = null,
    string? ColumnName = null,
    string? PropertyName = null);
```

This does not have to replace `IDLOptionFailure` immediately. The pragmatic path is:

1. Add flattening helpers for `IDLOptionFailure`:
   - enumerate all leaf failures
   - preserve the best source location
   - preserve the parent aggregate message when useful
2. Add conversion from `IDLOptionFailure` to `ValidationIssue`.
3. Teach CLI and generator reporting to use flattened issues.
4. Add richer issue producers incrementally as validators are refactored.

## Metadata Build Strategy

`MetadataDefinitionFactory.BuildCore(...)` is the central bottleneck. Changing only CLI and source-generator output will improve formatting, but it will not expose all model errors because `BuildCore(...)` currently exits at the first failed stage.

The safer implementation is to add report-style APIs beside the current `Option` APIs:

```csharp
public sealed record MetadataBuildReport(
    DatabaseDefinition? Database,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<TableBuildReport> Tables);

public sealed record TableBuildReport(
    TableModel? Table,
    IReadOnlyList<ValidationIssue> Issues);
```

Keep existing callers on `Build(...)` initially by adapting `Build(...)` to return the first error from the report. That avoids a risky flag day.

Recommended sequencing inside the report builder:

1. Convert drafts into the construction graph.
2. Run non-mutating shape and annotation validators, collecting issues.
3. If database-level fatal issues exist, stop that database branch.
4. Remove or mark invalid table branches before relation/index resolution.
5. Run relation and index resolution against the remaining valid graph, collecting scoped issues.
6. Generate relation properties only for coherent relation edges.
7. Validate generated symbols after generated relation properties are known.
8. Index columns and freeze only when a coherent `DatabaseDefinition` can be returned.

This is a real architecture change. The cheap version is to flatten existing aggregate failures, but the valuable version requires `MetadataDefinitionFactory` to stop being a first-error pipeline.

## Recoverable vs Fatal

Use these rules to avoid pretending the tool can keep going when it cannot.

Fatal for the whole command:

- config file cannot be found or parsed
- requested connection cannot be resolved
- database connection cannot be opened
- output directory is not writable when generation is otherwise ready

Fatal for one database:

- database model class cannot be identified
- duplicate database identity makes ownership ambiguous
- database C# type or namespace is invalid
- database-level metadata is internally inconsistent

Usually recoverable at table/property scope:

- invalid attribute argument on one table, column, property, index, or relation
- duplicate table name where only the duplicate table needs to be suppressed
- duplicate column name where the duplicate column or table can be marked invalid
- unsupported provider column type on one column
- missing primary key on one table
- unresolved foreign key or relation edge
- invalid default value on one property
- rendering failure for one table file

Some duplicate errors are deceptively hard. For example, duplicate table names are recoverable for source-generator table output if the duplicate table is suppressed, but they are fatal for generating a complete database bootstrap. That distinction should be explicit in the result model.

## Implementation Plan

### Phase 1: Reporting Surface

- Add `ValidationIssue`, severity, scope, and deterministic ordering.
- Add `IDLOptionFailure` flattening helpers.
- Update CLI text and JSON formatting to report flattened issues.
- Update source generator diagnostic reporting to emit one diagnostic per leaf issue instead of one aggregate blob when source locations are available.

This phase improves user output without trying to solve partial metadata construction yet.

### Phase 2: No-Write CLI Generation

- Change `ModelGenerator.CreateModels(...)` to separate:
  - metadata read and validation
  - source merge
  - file rendering
  - filesystem write
- Materialize all `ModelFileFactory.CreateModelFiles(...)` results before the first write.
- Report all rendering issues.
- Write no files when any error exists.
- Add tests proving existing files remain unchanged after generation errors.

This should be done before broad validator refactors because it closes the most dangerous current behavior.

### Phase 3: Aggregated Metadata Build Reports

- Add report-style APIs to `MetadataDefinitionFactory`.
- Convert pure validators to return multiple issues.
- Keep `Build(...)` as a compatibility wrapper over the report API.
- Start with high-value validators:
  - duplicate database attributes
  - duplicate table model properties
  - duplicate table names
  - duplicate column names
  - invalid C# symbols
  - missing primary keys
  - invalid defaults
  - invalid index definitions
  - unresolved relations
- Preserve source locations in each issue.

This is the phase that makes "report all model errors" substantially true.

### Phase 4: Provider Metadata Aggregation

- Change MySQL, MariaDB, and SQLite provider readers to collect all table, column, index, and relation issues in a provider metadata report.
- Keep unsupported-but-skipped features as warnings with stable issue codes.
- Fail only the affected table, column, index, or relation where possible.
- Keep connection failures and broken information-schema queries fatal for that provider branch.

This phase makes `create-models` and `validate` much more useful against imperfect real databases.

### Phase 5: Partial Source Generation

- Change source generation from database-wide `try/catch` to scoped generation:
  - database parse/build issues become diagnostics
  - valid table outputs are generated independently
  - rendering failures are diagnostics for that table
  - database bootstrap generation is suppressed when the graph is incomplete
- Refactor `GeneratorFileFactory` to expose independent table rendering and bootstrap rendering methods instead of one lazy `CreateModelFiles(...)` stream.
- Keep `DefaultValueCompatibilityValidator` behavior: report every invalid default and suppress only the broken assignment.
- Ensure generated output does not create avoidable secondary compiler diagnostics.

This phase directly satisfies the IDE and compile-time source-generator requirement.

### Phase 6: Tests and Compatibility

Add focused tests before attempting broad refactors:

- CLI validation reports multiple model errors in one run.
- CLI validation reports model issues and does not claim drift comparison was trustworthy when metadata is invalid.
- CLI diff writes no file when validation issues exist.
- CLI create-models leaves existing files unchanged when rendering fails.
- Source generator reports multiple `DLG001` diagnostics for multiple invalid attributes.
- Source generator reports table-scoped errors and still generates valid table files from the same database when safe.
- Source generator suppresses database bootstrap for incomplete database graphs.
- Source generator continues generating a second valid database when the first database is invalid.
- Provider reader tests aggregate multiple unsupported or malformed columns.
- Existing single-error tests remain meaningful but should stop asserting `Single(...)` where multi-diagnostic behavior is the new contract.

## Output and Ordering Rules

Ordering must be deterministic or the CLI will become irritating in CI.

Recommended order:

1. severity: Error, Warning, Info
2. database name
3. scope
4. table name
5. column or property name
6. source file path
7. source span start
8. issue code
9. message

For Roslyn diagnostics, use source location ordering naturally when possible. For CLI output, include file and line/column when `SourceLocation` exists, but keep database/table/column path even when file location does not exist.

## Non-Goals

- Do not implement migrations here. This plan improves validation and generation diagnostics; migration history still belongs to the migration plan.
- Do not make CLI generation "best effort" by writing valid files while errors exist. That is useful in the source generator, but it is wrong for filesystem output.
- Do not hide errors as warnings just to keep generation moving.
- Do not make `validate` return exit code `1` for metadata errors. Exit code `1` should remain schema drift with valid metadata; exit code `2` should mean the validation run itself failed.
- Do not generate a silently partial database bootstrap and pretend runtime metadata is complete.

## Open Questions

- Should `datalinq create-models` eventually use a staging directory and manifest replacement to protect against mid-write I/O failures, or is in-memory render-before-write enough for the first implementation?
- Should CLI JSON expose raw `DLFailureType` alongside normalized issue codes for compatibility with older tooling?
- Should source-generator diagnostics keep the current `DLG001`/`DLG002` IDs with richer messages, or should common validation categories get dedicated diagnostic IDs?
- For a database with table-level errors, should the source generator emit no bootstrap, or emit an explicitly incomplete bootstrap guarded by generated diagnostics? My recommendation is no bootstrap until the database graph is coherent.
