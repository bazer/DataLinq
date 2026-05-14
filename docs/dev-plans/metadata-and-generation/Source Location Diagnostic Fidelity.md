> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Source Location Diagnostic Fidelity

**Status:** Draft implementation plan.
**Goal:** Every DataLinq validation, CLI, and source-generator diagnostic should point to the most precise source location DataLinq can honestly know. Prefer exact line and column ranges for source-authored model errors. Fall back to file-level locations when the file is known but the exact syntax span is not. Use object paths, not fake file locations, for provider/database facts that do not come from source files.

## Executive Position

Accurate source locations are not polish. They are the difference between a tool that feels trustworthy and one that makes users hunt through generated or handwritten model code by smell.

The current source-generator path is already better than average: model declarations, model attributes, property declarations, property attributes, and default-value expressions often carry exact spans, and several tests assert the exact highlighted source. But the location story is not complete enough yet. CLI output does not consistently format line and column, aggregate failures collapse to one "most relevant" location, provider/config/tooling errors generally have no source location, and some metadata paths still fall back to broad definition or file locations even when a better attribute or property span exists.

The target rule should be blunt:

- If the failing thing is represented by C# syntax, report the exact syntax span.
- If the failing thing belongs to a known file but the syntax span is gone, report the file.
- If the failing thing is provider metadata, report the database object path, not a made-up file location.
- If the failing thing is an internal generator exception, report the nearest known user source location and include enough context to file a bug.

## Current Location Inventory

### Source Location Primitives

Current primitives live under `src/DataLinq.SharedCore/Metadata/PropertySourceInfo.cs`:

- `SourceTextSpan`
  - stores absolute character `Start` and `Length`
  - does not store line or column
- `SourceLocation`
  - stores `CsFileDeclaration File`
  - optionally stores `SourceTextSpan Span`
  - `ToString()` prints file plus character offsets, not line/column
- `PropertySourceInfo`
  - stores the whole property span
  - stores the default-value expression span when a `[Default(...)]` argument exists
  - can produce property-level or default-expression source locations

`DatabaseDefinition`, `ModelDefinition`, and `PropertyDefinition` store source spans and attribute source spans. `TableDefinition` and `ColumnDefinition` only implement `IDefinition.CsFile` indirectly through their model, so raw table/column failures are file-level unless helper code maps them back to a table or column attribute.

### Roslyn Model Parsing

`src/DataLinq.SharedCore/Factories/SyntaxParser.cs` captures good C# source spans:

- type declarations get `SourceSpan`
- model/database attributes get attribute spans
- property declarations get `PropertySourceInfo`
- property attributes get attribute spans
- `[Default(...)]` captures the argument expression span, not just the attribute span
- parser failures from `FailAttribute(...)`, `FailProperty(...)`, and `FailType(...)` carry exact syntax-node spans when the syntax tree has a file path

This is the strongest part of the system.

### Metadata Draft Conversion and Copying

`MetadataTypedDraftConverter` preserves database, model, property, and attribute spans when lowering typed drafts into mutable metadata.

`MetadataDefinitionSnapshot` copies source spans and attribute spans when cloning metadata graphs.

`MetadataTransformer` currently preserves some source metadata during `create-models` source merge, especially value-property source info, but it does not appear to preserve every database/model source span and attribute-span association when source attributes are copied onto provider-derived metadata. That can detach metadata from the exact source syntax that originally created it.

### Metadata Validation Failures

`IDLOptionFailure` can carry a `SourceLocation`, and `GetMostRelevantSourceLocation()` walks inner failures to find one.

Many `MetadataFactory` helpers already prefer precise locations:

- index errors prefer the `[Index]` attribute, then model/property fallback
- duplicate table names prefer `[Table]` or `[View]`
- duplicate column names prefer `[Column]`, then the property
- unresolved relation errors prefer `[Relation]`, then the relation property
- foreign-key errors prefer `[ForeignKey]`, then the value property
- missing primary key prefers `[Table]` or `[View]`
- default/property errors often use the property span

The weak spots are raw `DLOptionFailure.Fail(..., definition)` calls. `DLOptionFailure.GetDefinitionLocation(...)` only knows exact locations for `DatabaseDefinition` and `ModelDefinition`. For other `IDefinition` types it falls back to `definition.CsFile`, which is file-level. This means table, column, and property failures can silently lose line/column unless they go through a specialized helper first.

### Source Generator Diagnostics

`src/DataLinq.Generators/ModelGenerator.cs` maps `SourceLocation` to Roslyn `Location` by finding the matching syntax tree and applying the stored character span.

Current behavior:

- no `SourceLocation` -> `Location.None`
- file path not found in the compilation -> `Location.None`
- file-level location with no span -> first position in the syntax tree
- exact span -> exact Roslyn diagnostic span

`DefaultValueCompatibilityValidator` bypasses the metadata `SourceLocation` conversion for `DLG003` and reports directly on the resolved default expression syntax. This is excellent and should be the model for semantic validations that can resolve concrete syntax.

`DLG000` unhandled generator exceptions intentionally use `Location.None`; that is defensible for initialization failures, but production generator execution failures should usually attach to the nearest known database/model/table source when possible.

### CLI and Tooling Output

CLI validation currently prints failures and drift differences as text. It does not have a shared formatter that converts `SourceLocation` character spans into line and column ranges.

`SchemaDifference` carries `ModelDefinition` and `DatabaseDefinition` references, but CLI text/JSON output does not surface source locations for differences. If a model-side difference references a source-authored model definition, the CLI should be able to tell the user where that model table or column came from.

Path and file-read failures from `DataLinq.Tools` are string-only failures today. They should at least be file-level path locations when the path is known.

### Config and JSON Input

`ConfigReader` strips comments and deserializes with `System.Text.Json`. Current config failures are string-only and do not consistently carry `datalinq.json` file locations, much less JSON line and column.

Because comment stripping changes the text unless a position map is maintained, exact config line/column is not currently reliable. File-level config fallback is achievable immediately; exact JSON property locations require a different reader strategy.

### Provider Metadata

Provider metadata comes from SQLite pragmas or MySQL/MariaDB information schema. It generally has no C# source file.

Provider-reader failures should report database object identity:

- provider
- database/schema
- table or view
- column
- index
- relation or constraint

They should not claim a source file unless the failure is tied to a configured source include or model comparison result.

## Desired Behavior

### Location Precision Levels

Use a visible precision model internally and in tests:

1. **ExactSpan**
   - file path plus start/end line and column
   - used for C# syntax nodes and JSON properties once supported
2. **File**
   - file path only
   - used for missing spans, unreadable files, config file errors without line/column, and external source files
3. **ObjectPath**
   - provider/database/table/column/index/relation identity without a file
   - used for provider metadata and live schema facts
4. **None**
   - only for process-level/internal failures where DataLinq has no meaningful user input location

`None` should be rare and suspicious in tests.

### Line and Column Semantics

For human and JSON output:

- line and column should be 1-based
- ranges should include start line/column and end line/column
- offsets may still be included for debugging, but should not be the primary user-facing location

For Roslyn diagnostics:

- keep using Roslyn `Location` and `TextSpan`
- use zero-based positions internally as Roslyn expects

### Fallback Rules

Preferred fallbacks:

- attribute-specific issue -> attribute span
- default-value expression issue -> expression span
- table identity issue -> `[Table]` or `[View]` span, then model declaration span, then file
- column identity issue -> `[Column]` span, then property declaration span, then file
- relation issue -> `[Relation]` or `[ForeignKey]` span, then property declaration span, then file
- database identity issue -> `[Database]` span, then database class span, then file
- generated file rendering issue -> model declaration span, then file
- source file read issue -> file path only
- provider issue -> object path only

## Proposed Types and Helpers

Keep `SourceLocation` as the compact persisted metadata primitive. Add a resolved display shape for reporting:

```csharp
public enum DiagnosticLocationPrecision
{
    None,
    ObjectPath,
    File,
    ExactSpan
}

public sealed record DiagnosticSourceLocation(
    DiagnosticLocationPrecision Precision,
    string? FilePath,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn,
    int? StartOffset,
    int? Length,
    string? ObjectPath);
```

Add a helper that resolves `SourceLocation` into line/column without making every caller reinvent text indexing:

```csharp
public interface ISourceLocationResolver
{
    DiagnosticSourceLocation Resolve(SourceLocation? location, string? objectPath = null);
}
```

Implementation notes:

- Cache line maps by full path and last-write timestamp for CLI runs.
- In source generator code, prefer Roslyn `SourceText.Lines` from the current syntax tree.
- For file-level locations, set file path and precision `File`; do not manufacture line `1`, column `1` in JSON unless the consumer explicitly needs a display anchor.
- For missing files, still allow file-level location if the path is the subject of the error.

## Implementation Plan

### Phase 1: Centralize Location Resolution

- Add `DiagnosticLocationPrecision` and `DiagnosticSourceLocation`.
- Add a resolver/formatter for converting `SourceLocation` offsets to 1-based line/column ranges.
- Update CLI text output to print locations like:

```text
Models/Employee.cs:23:10-23:24
Models/Employee.cs
employees.orders.user_id
```

- Update CLI JSON payloads to include structured location data for validation issues and, where possible, schema differences.
- Add tests for line/column conversion across:
  - LF and CRLF files
  - single-line spans
  - multi-line spans
  - zero-length/file-level spans
  - missing files

### Phase 2: Flatten Failures Without Losing Locations

- Add an `IDLOptionFailure` leaf enumerator that preserves every inner failure's location.
- Stop relying on `GetMostRelevantSourceLocation()` for aggregate CLI output.
- In the source generator, report each leaf failure when the failure is recoverable and source-located.
- Keep `GetMostRelevantSourceLocation()` only as a compatibility fallback.

This phase pairs directly with the aggregated diagnostics plan. Accurate locations are wasted if aggregate reporting collapses them back to one arbitrary child.

### Phase 3: Replace Raw Definition Fallbacks

- Introduce centralized location helpers for definitions:
  - `GetBestDatabaseLocation(...)`
  - `GetBestModelLocation(...)`
  - `GetBestTableLocation(...)`
  - `GetBestColumnLocation(...)`
  - `GetBestValuePropertyLocation(...)`
  - `GetBestRelationPropertyLocation(...)`
  - `GetBestAttributeLocation(...)`
- Update `DLOptionFailure.GetDefinitionLocation(...)` or add a new resolver that understands table, column, and property locations instead of returning file-level locations for every non-database/non-model definition.
- Replace raw `DLOptionFailure.Fail(..., table)`, `Fail(..., column)`, and `Fail(..., property)` calls in metadata validation with the centralized helpers.
- Add unit tests for common metadata failures that currently rely on broad fallbacks:
  - duplicate table model property
  - empty table/view name
  - empty column name
  - invalid existing table/column binding
  - invalid default metadata
  - invalid generated relation property name

### Phase 4: Preserve Source Spans Through Transformations

- Audit `MetadataTransformer` and ensure source metadata copied from source models to provider-derived metadata includes:
  - database `CsFile`
  - database source span
  - database attribute spans
  - model source spans
  - model attribute spans
  - property source info
  - property attribute spans
- Add tests where `create-models` source merge preserves a source `[Table]`, `[Column]`, `[Default]`, and property span after transformation.
- Ensure `MetadataDefinitionSnapshot.Copy(...)` remains the reference behavior for span preservation.

This matters because CLI model generation and validation often move metadata through snapshots and transformations before an error is reported.

### Phase 5: Config File Locations

- Start with file-level locations for:
  - missing config
  - unreadable config
  - failed deserialization
  - ambiguous or missing configured database/connection
- Add exact JSON line/column later by replacing comment-stripping deserialization with a location-aware config reader.
- If comments must remain supported, maintain a mapping from stripped JSON positions back to original file positions.
- Include config locations in CLI JSON output.

Do not pretend current comment-stripped JSON positions are accurate. They are not unless we explicitly maintain a position map.

### Phase 6: Provider and Drift Locations

- Add object-path location fields to provider metadata issues.
- For schema differences:
  - include model-side source location when `ModelDefinition` or `ColumnDefinition` can be traced to source
  - include provider-side object path for live database definitions
  - prefer model-side exact location for errors the user can fix in model code
  - use provider object path for extra database objects that have no model source
- Update `SchemaDifference` output shape or CLI projection to include both `modelLocation` and `databaseLocation`.

### Phase 7: Source Generator Location Hardening

- Update `ResolveSourceLocation(...)` to optionally create a Roslyn external-file `Location` when the file path exists but the syntax tree is not in the current compilation.
- For file-level source locations, intentionally anchor to the first token or start of file and document that behavior.
- For `DLG002` model-file generation failures, report the table/model declaration span when possible.
- Keep `DLG000` as `Location.None` for true initialization failures, but avoid using it for failures that can be tied to user model syntax.
- Add generator tests that assert not only highlighted source text but also exact line/column positions for representative diagnostics.

## Test Inventory

Existing useful coverage:

- source draft preserves database, table, property, attribute, and default expression spans
- generator diagnostics point at invalid metadata attributes
- generator diagnostics point at duplicate table and column attributes
- generator diagnostics point at relation and foreign-key attributes
- generator diagnostics point at invalid default expressions

Needed coverage:

- CLI validation emits structured file/line/column for source model errors
- CLI validation emits file-level location for source file read errors
- CLI validation emits object-path location for provider metadata errors
- CLI diff emits model-side source locations for drift differences
- aggregate failures retain every child location
- metadata transformations preserve all source spans
- raw metadata validation fallbacks prefer attribute/property spans over file-level locations
- config errors at least include the config file path
- JSON config exact positions are either correct or deliberately omitted

## Non-Goals

- Do not invent line/column for provider metadata. Use object paths.
- Do not report stale line/column after comment stripping unless a position map proves the mapping.
- Do not require all runtime exceptions to have source locations. Runtime query/mutation failures are a different diagnostic domain.
- Do not make generated code locations point into generated files when the actionable fix is in user model source.
- Do not replace Roslyn `Location` with custom location records inside source-generator diagnostics. The custom shape is for CLI/tooling output.

## Recommended First Slice

The highest-value, lowest-risk first slice is:

1. Add a source-location resolver that converts existing `SourceLocation` spans to 1-based line/column.
2. Add CLI text/JSON location output for validation failures.
3. Add failure flattening so aggregate locations are not collapsed.
4. Add tests against existing model parser failures.

That gets users accurate line/column for many current errors without first rewriting metadata construction. The deeper work is then replacing raw definition fallbacks and preserving spans through every metadata transformation.
