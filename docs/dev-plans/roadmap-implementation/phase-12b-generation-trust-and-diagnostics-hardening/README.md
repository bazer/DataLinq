> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 12B: Generation Trust and Diagnostics Hardening

**Status:** Complete as of 2026-05-14.

## Purpose

Phase 12B tightens the boundary where DataLinq reads model source, validates metadata, writes generated model files, and emits source-generator output.

The user-facing rule is simple: DataLinq should tell users every independent problem it can find, point at the most precise source location it honestly knows, avoid unsafe partial filesystem writes, and make generated files clearly self-describing.

This is not a continuation of Phase 12 cache-memory work. The `12B` name is a practical insertion point so the existing Phase 13 and later numbering does not churn.

## Execution Boundary

In scope:

- aggregate validation diagnostics for CLI and source-generator paths
- safe CLI generation behavior: validate everything reachable, then write nothing when errors exist
- partial source-generator output for valid databases/tables while reporting all errors
- line/column source-location fidelity where source spans exist, with honest file-level fallback
- generated C# file banners that mark files as DataLinq-generated and not hand-editable
- optional CLI version and UTC generation timestamp stamping
- nullable-reference-type generation default change with generated `#nullable` directives
- source-generator nullable-context recognition from model files, not only project defaults

Out of scope:

- migration execution
- relation-aware joins or query API expansion
- broad metadata architecture rewrite beyond what diagnostics and partial generation require
- changing SQL script header policy beyond existing diff-script warnings
- making CLI generation best-effort on disk when validation errors exist

## Source Plans

- [Validation Diagnostics and Partial Generation](../../metadata-and-generation/Validation%20Diagnostics%20and%20Partial%20Generation.md)
- [Source Location Diagnostic Fidelity](../../metadata-and-generation/Source%20Location%20Diagnostic%20Fidelity.md)
- [Generated File Headers and Stamping](../../metadata-and-generation/Generated%20File%20Headers%20and%20Stamping.md)
- [Nullable Reference Type Generation Defaults](../../metadata-and-generation/Nullable%20Reference%20Type%20Generation%20Defaults.md)

## Recommended Order

1. Add the diagnostic/source-location contracts and tests first.
2. Convert CLI validation and diff output to report all reachable issues without writing artifacts on failure.
3. Make source generation report multiple diagnostics and emit valid scoped output without secondary compiler noise.
4. Add generated-file preambles: stable banner, nullable directive, and optional CLI stamp.
5. Change nullable-reference-type defaults and teach the source generator to follow file-level nullable context.
6. Update user-facing docs only after behavior lands.

## Exit Criteria

Phase 12B is done when:

- CLI `validate` and `diff` report all reachable validation issues and preserve the no-write-on-error rule
- CLI `create-models` validates and renders before writing, leaving existing files unchanged when errors exist
- source-generator diagnostics are precise, multiple, and do not collapse unrelated failures into one blob
- source generation emits valid files for unaffected databases/tables while suppressing unsafe incomplete bootstraps
- diagnostics prefer exact line/column spans, fall back to file-level only when necessary, and use database object paths for provider facts
- generated C# files have a stable DataLinq generated-file banner
- `create-models --stamp-generated-header` adds CLI version and one UTC timestamp per generation run
- nullable reference type generation is on by default, explicit opt-out remains supported, and generated files declare their nullable context
- source-generator output follows model-file nullable directives
