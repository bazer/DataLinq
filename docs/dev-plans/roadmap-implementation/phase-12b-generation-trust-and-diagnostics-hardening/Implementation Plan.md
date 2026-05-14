> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 12B Implementation Plan: Generation Trust and Diagnostics Hardening

**Status:** Draft execution plan.

## Purpose

Phase 12B is about trust at the generation boundary.

Right now several DataLinq paths still fail too early, collapse multiple useful errors into one result, lose source-location precision in some cases, and generate C# files that do not fully identify their generated nature or nullable context. Each problem is tolerable in isolation. Together they make validation and generation feel less deterministic than they should.

The phase goal is to make DataLinq generation behavior boring in the best way:

- validation reports every independent problem it can honestly reach
- diagnostics point at the right source span when possible
- CLI commands do not write files when validation or rendering failed
- source generation still helps the IDE by emitting valid output for unaffected scopes
- generated files clearly state that they are generated
- generated files carry their nullable context instead of depending on project luck

## Phase-Start Baseline

Current behavior worth preserving:

- `SchemaComparer.Compare(...)` already accumulates schema differences once both metadata graphs exist.
- `diff` writes only after validation succeeds.
- source generation already processes multiple database metadata results rather than stopping the entire generator on the first database failure.
- `DefaultValueCompatibilityValidator` reports concrete diagnostics on resolved syntax and suppresses only the broken generated default assignment.
- `SourceLocation` can carry exact character spans, and the source generator can map those spans back to Roslyn `Location`.

Current behavior that this phase should change:

- `MetadataDefinitionFactory.BuildCore(...)` remains a long fail-fast pipeline.
- CLI metadata loading, validation, and generation still return early in many branch-local cases.
- `ModelGenerator.CreateModels(...)` writes files while enumerating generated output, so a later render/I/O failure can leave a partial output directory.
- source generation wraps rendering in one database-wide `try/catch`, so one table rendering failure can abort unrelated table output in the same database.
- aggregate failures are often printed as one blob rather than one useful issue per leaf failure.
- CLI output does not consistently print line/column locations.
- several table/column/property failures lose exact source spans and fall back to file-level locations.
- `ModelFileFactory` and `GeneratorFileFactory` do not emit generated-file banners.
- CLI-generated files do not emit `#nullable enable`, even when DataLinq emits nullable reference annotations.
- `UseNullableReferenceTypes` defaults to false.
- the source generator follows only project-level nullable settings and ignores file-level `#nullable enable` / `#nullable disable` directives.

## Goals

- collect all reachable validation issues in CLI and source-generator paths
- preserve safe filesystem behavior: no CLI-generated C# or SQL output when validation/rendering has errors
- emit partial source-generator output only where the generated graph remains coherent
- report diagnostics with the most precise source location DataLinq knows
- make generated C# files self-identifying and nullable-context explicit
- change nullable-reference-type generation to default-on while preserving explicit opt-out
- keep user-facing docs synchronized with shipped behavior, not roadmap intent

## Non-Goals

- migration execution
- a broad metadata architecture rewrite
- generating best-effort CLI files on disk when errors exist
- hiding validation failures as warnings to keep generation moving
- relation-aware joins, query API work, or Remotion isolation
- changing SQL script header policy beyond what is already present for diff scripts
- source-generator timestamps

## Workstream A: Diagnostic Issue Model and Source Location Fidelity

Goals:

- define the diagnostic unit that CLI and generator paths can both report
- make leaf errors printable without losing aggregate structure
- improve source-location formatting and fallback rules

Tasks:

1. Introduce a small structured issue shape, either by extending `IDLOptionFailure` traversal or by adding a `ValidationIssue` projection.
2. Define fields needed by both CLI and source generator:
   - severity
   - failure/category code
   - message
   - source location
   - database/table/column/property path
   - provider object path when no source file exists
3. Add helpers that flatten aggregate failures into ordered leaf issues without discarding parent context.
4. Add source-location formatting that prints exact line and column when a span maps to source text.
5. Add file-level fallback when a file is known but no span is honest.
6. Keep provider/database facts as object paths rather than fake source locations.
7. Audit raw `DLOptionFailure.Fail(..., definition)` calls that currently lose table, column, or property spans.
8. Add targeted helpers for common metadata failures:
   - database attributes
   - table/view attributes
   - column attributes
   - relation attributes
   - index attributes
   - default attributes and default expressions
9. Add tests for source-span to line/column conversion, file-level fallback, provider object paths, and aggregate issue ordering.

Design stance:

- Location precision is a correctness feature, not presentation polish.
- If exact syntax is unknown, say less. Bad precision is worse than file-level honesty.
- Provider facts should use provider object identity, not pretend to come from C# source.

Exit criteria:

- CLI and generator paths can render the same leaf issue list.
- line/column output is covered by tests
- aggregate failures no longer force a single "most relevant" diagnostic when multiple leaf locations exist
- common table/column/property failures retain specific source spans where the parser captured them

Implementation status, 2026-05-14:

- Added `DataLinqDiagnosticIssue` as the shared leaf-issue projection over `IDLOptionFailure`.
- Added deterministic issue ordering by source file/span, object path, failure type, then message.
- Added source-location line/column formatting that falls back to file-level output when source text is unavailable.
- Added object-path extraction for database, model, table, column, property, and index definitions.
- Improved `DLOptionFailure.Fail(..., IDefinition)` fallback locations for table, column, property, and index definitions.
- Added unit coverage for aggregate flattening, line/column formatting, and table/column source-location fallback.

## Workstream B: CLI Validation, Diff, and Safe File Writes

Goals:

- make CLI validation report all reachable errors
- keep `validate`, `diff`, and `create-models` safe when errors exist
- avoid partial filesystem mutation during model generation

Tasks:

1. Change `SchemaValidator` and related metadata-loading paths to return a validation report with issue collections, not only a single failure.
2. Keep hard blockers hard:
   - missing config
   - invalid target selection
   - unreadable database connection
   - no identifiable database model
3. Aggregate branch-local errors after enough context exists:
   - missing source paths
   - unreadable source files
   - invalid attributes
   - duplicate tables/columns/indexes
   - unresolved relations
   - provider table/column/index/relation parse failures
4. Update `validate` text output to group issues before drift differences.
5. Update `validate --output json` to expose structured issues without making consumers parse `ToString()` output.
6. Preserve exit-code intent:
   - `0` when valid and no drift
   - `1` when metadata is valid but drift exists
   - `2` when validation itself failed
7. Update `diff` so validation issues are printed and no SQL file is written.
8. Change `ModelGenerator.CreateModels(...)` to validate and render all output into memory before writing any file.
9. If any validation or rendering issue exists, return the issue report and leave existing files untouched.
10. Add tests proving existing generated files remain unchanged after validation or rendering failures.

Design stance:

- CLI file generation is not the place for best-effort partial writes. Filesystem output should be atomic at the command level.
- Drift comparison is meaningful only after both model and provider metadata are trustworthy.
- JSON output should be the automation surface; text output should be for humans.

Exit criteria:

- one CLI run reports multiple independent validation issues
- `diff` writes no output when validation issues exist
- `create-models` leaves existing files untouched when any validation/rendering error exists
- valid `create-models` behavior still writes all expected database/table/view model files

Implementation status, 2026-05-14:

- Started CLI issue output conversion by routing `IDLOptionFailure` values through the shared `DataLinqDiagnosticIssue` projection.
- Added line/column-aware text formatting for CLI failures when source text is available, with file-level fallback when it is not.
- Added structured `issues` output for `validate --output json` on validation failures and successful validation results.
- Changed `create-models` to render every generated model file into memory before replacing any target file.
- Added a staged generated-file writer that writes temporary files first and rolls back previously replaced files if a later replacement fails.
- Changed file metadata loading to aggregate every reachable path/read failure before returning.
- Changed provider index and relation parsing loops for SQLite, MySQL, and MariaDB to accumulate independent malformed metadata failures within the same phase.

## Workstream C: Source Generator Partial Output and Multi-Diagnostics

Goals:

- report all reachable generator errors
- emit valid generated source for unaffected databases/tables
- avoid avoidable secondary compiler diagnostics

Tasks:

1. Make metadata parsing/building produce issue collections that can become multiple Roslyn diagnostics.
2. Report one diagnostic per useful leaf issue when source locations exist.
3. Keep aggregate fallback only for truly unstructured internal failures.
4. Change generated file creation from one database-wide render operation to scoped operations:
   - table model output
   - database metadata bootstrap output
   - relation/bootstrap graph checks
5. Emit table-level generated files when the table metadata is coherent.
6. Suppress database metadata bootstrap when the database graph is incomplete or relation metadata cannot be trusted.
7. Suppress generated files that would cause predictable secondary C# errors.
8. Preserve the `DefaultValueCompatibilityValidator` pattern: report the semantic error and suppress only the broken generated fragment where possible.
9. Add tests for:
   - multiple invalid attributes producing multiple diagnostics
   - one broken table not suppressing unrelated valid table output
   - broken database graph suppressing bootstrap output
   - no avoidable `CS0246`, `CS0534`, or similar secondary noise

Design stance:

- Source generation can be partially useful because it is compiler-owned and diagnostics fail the build anyway.
- Partial output must never pretend the runtime metadata graph is complete when it is not.
- The IDE scenario matters: valid generated code for unaffected tables helps users inspect the shape while fixing errors.

Exit criteria:

- source generator emits multiple focused diagnostics for multiple source errors
- valid tables/databases still get generated output where safe
- incomplete graph bootstraps are suppressed rather than silently partial
- tests prove secondary compiler noise is not introduced for planned error cases

## Workstream D: Generated File Preamble and Nullable Defaults

Goals:

- make every generated C# file clearly generated
- make generated nullable context explicit
- change nullable-reference-type generation to default-on
- preserve explicit opt-out

Tasks:

1. Add a shared generated-file preamble renderer used by `ModelFileFactory` and `GeneratorFileFactory`.
2. Emit stable generated-file banner lines:
   - `// <auto-generated />`
   - `// This file was generated by DataLinq. Do not edit this file directly.`
3. Add CLI option `--stamp-generated-header`.
4. When stamping is enabled, capture CLI version and one UTC timestamp once per generation run.
5. Include stamp lines only in CLI-written files, never in source-generator output.
6. Change `UseNullableReferenceTypes` default from false to true.
7. Emit `#nullable enable` in generated files when nullable reference generation is enabled.
8. Emit `#nullable disable` when the user explicitly opts out with `UseNullableReferenceTypes: false`.
9. Keep nullable directives after the generated-file banner and before `using` statements.
10. Teach the source generator to follow source-level nullable context from model declarations.
11. Include nullable context in incremental generator inputs so directive-only changes refresh generated output.
12. Add tests for stable banners, stamped CLI headers, nullable directive placement, default nullable behavior, explicit opt-out, and source-generator file-level nullable recognition.

Design stance:

- The warning banner should be always on.
- Timestamps should be opt-in because default timestamps create junk diffs.
- Generated files should not depend on project nullable settings by accident.
- Source-generator output must stay deterministic.

Exit criteria:

- all generated C# files start with the DataLinq generated-file banner
- `create-models --stamp-generated-header` emits CLI version and one shared UTC timestamp
- unstamped generation remains deterministic for unchanged metadata
- nullable reference generation is default-on
- explicit opt-out remains supported
- source-generator output follows `#nullable enable` / `#nullable disable` in model files

## Workstream E: Documentation, Release Notes, and Support Boundary

Goals:

- document shipped behavior only after implementation
- warn users about intentional generated-file churn
- keep support matrices honest

Tasks:

1. Update `docs/Configuration files.md` after the nullable default changes.
2. Update `docs/CLI Documentation.md` after `--stamp-generated-header` exists.
3. Add release-note wording for:
   - generated-file banner first-time diffs
   - nullable-reference-type default change
   - explicit `UseNullableReferenceTypes: false` opt-out
4. Update any generated examples only after files are regenerated intentionally.
5. Keep roadmap/dev-plan documents separate from current product docs.
6. If diagnostic output JSON changes, document the new schema enough for automation users.

Exit criteria:

- user docs describe implemented behavior, not planned behavior
- generated examples match the new preamble and nullable policy
- compatibility notes explain the expected one-time source diffs

## Recommended Execution Order

1. Add characterization tests for current diagnostic and generated-file behavior.
2. Build the structured issue/source-location reporting layer.
3. Convert CLI `validate` and `diff` to multi-issue reporting.
4. Make CLI `create-models` render before writing and preserve no-write-on-error behavior.
5. Convert source-generator diagnostics from aggregate blobs to multiple leaf diagnostics.
6. Add scoped source-generator output and bootstrap suppression rules.
7. Add generated-file preamble rendering and the optional CLI stamp.
8. Change nullable-reference-type defaults and emit nullable directives.
9. Teach the source generator to follow file-level nullable context.
10. Update user-facing docs and release notes.
11. Run the verification matrix.

The only sequencing constraint I would treat as hard: source-location and issue flattening should come before the CLI and generator reporting changes. Otherwise every downstream change will invent its own diagnostic shape and Phase 12B will become a formatting cleanup instead of an architecture cleanup.

## Verification Matrix

Fast local checks:

- `src/DataLinq.Tests.Unit` for metadata, factories, validation, headers, and config defaults
- `src/DataLinq.Generators.Tests` for diagnostics, partial output, nullable context, and generated text shape
- targeted CLI smoke for `validate`, `diff`, and `create-models`

Broader checks before closeout:

- provider-backed validation/generation lanes for SQLite and MySQL/MariaDB where practical
- source-generator incremental behavior tests
- docs build when user-facing docs change
- generated-model digest or representative compile checks after preamble/nullability changes

Recommended targeted commands will depend on the exact slice, but the phase closeout should include at least:

```powershell
.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug
.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Generators.Tests\DataLinq.Generators.Tests.csproj -c Debug
```

Provider-backed commands should follow the existing Testing CLI workflow and use sandbox database host guidance when run inside Codex.

## Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Aggregating errors after partial metadata failure produces misleading follow-on errors | High | Define hard blockers and branch-local continuation rules before implementation |
| CLI generation leaves partial output after late render or I/O failure | High | Render all files before writing; optionally stage writes before replacing files |
| Source generator emits incomplete runtime bootstrap | High | Suppress bootstrap when graph coherence is not proven |
| Multiple diagnostics create noisy or unstable ordering | Medium | Sort by file path, span start, object path, then message/category |
| Line/column locations become fake precision | Medium | Prefer file-level fallback when exact span is not known |
| Nullable default change surprises users | Medium | Keep explicit `UseNullableReferenceTypes: false`, add release notes, and document first-time generated diffs |
| Header timestamps create constant source churn | Medium | Keep stamping opt-in and capture one timestamp per CLI run |
| Source-generator nullable context changes do not invalidate incrementally | High | Include nullable context in generator input snapshots and add directive-only incremental tests |

## Phase Closeout Criteria

Phase 12B can close when:

- the four source plans are either implemented or explicitly narrowed with follow-up notes
- CLI validation and generation behavior is covered by tests
- source-generator multi-diagnostic and partial-output behavior is covered by tests
- generated C# preambles and nullable directives are covered by tests
- user docs and release notes reflect shipped behavior
- the roadmap implementation index marks Phase 13 as the next priority after 12B
