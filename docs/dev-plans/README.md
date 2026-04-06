# Development Plans

> [!WARNING]
> This folder contains roadmap, design, migration, and audit material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.

## Purpose

`docs/dev-plans` is where DataLinq keeps internal planning notes, architectural drafts, migration records, and performance ideas that are useful to contributors but should stay clearly separate from user-facing docs.

The point of this folder is not to look tidy. The point is to stop roadmap material from leaking into documentation that claims to describe current behavior.

## Structure

### Architecture

- `architecture/Applications patterns.md`

### Documentation

- `documentation/Documentation Overhaul.md`

### Metadata and generation

- `metadata-and-generation/Metadata Architecture.md`
- `metadata-and-generation/Source Generator Optimizations.md`

### Performance

- `performance/Memory management.md`
- `performance/Memory Optimization and Deduplication.md`
- `performance/Performance Benchmarking.md`

### Providers and features

- `providers-and-features/In-Memory Provider.md`
- `providers-and-features/JSON Data Type Support.md`
- `providers-and-features/Migrations and Validation.md`

### Query and runtime

- `query-and-runtime/Batched mutations.md`
- `query-and-runtime/Projections and Views.md`
- `query-and-runtime/Query Pipeline Abstraction.md`
- `query-and-runtime/Result set caching.md`
- `query-and-runtime/Sql Generation Optimization.md`

### Testing

- `testing/Test Infrastructure CLI.md`
- `testing/Test Suite Audit.md`
- `testing/Test Suite Parity Checklist.md`
- `testing/test-suite-parity.json`
- `testing/Testing and Infrastructure.md`

## Notes

- The `testing/` folder is the most operationally relevant part of `dev-plans` right now. It contains the migration record, parity checklist, and machine-readable manifest for the TUnit cutover.
- Some documents in this folder describe ideas that are still valid but not implemented. That is fine. The real mistake is presenting those ideas as current product behavior.
