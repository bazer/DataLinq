> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 10: AOT Query Coverage and Fallback Fencing

**Status:** Implemented for the selected 0.8 constrained smoke subset.

Execution record: [Implementation Plan](Implementation%20Plan.md).

## Purpose

Phase 10 expands the constrained-platform smoke path from "representative query proof" to "credible documented-subset proof".

The parser and query plan are now DataLinq-owned. The AOT/browser support boundary should prove the same supported shapes the docs claim, and it should fail loudly if a constrained route drifts into reflection-heavy compatibility fallback.

## Scope

In scope:

- add constrained smoke coverage for selected rows from the LINQ support matrix
- cover scalar aggregates, paging/result operators, relation predicates, explicit joins, local membership, nullable predicates, row-local projections, and unsupported diagnostics where practical
- keep `AotStrict` parser/projection options on constrained paths
- add guard tests for fallback paths that are not part of the AOT support boundary
- capture query-hot-path benchmark history after coverage changes

Out of scope:

- expanding the public LINQ support matrix as part of AOT proof
- broad join feature work
- client-side predicate fallback
- reflection-discovered model support as an AOT claim

## Exit Criteria

- constrained smokes cover the documented query subset selected for 0.8 support
- unsupported shapes fail with focused diagnostics under constrained execution
- no constrained-platform route requires `Expression.Compile()`, dynamic invocation, or reflection-only projection fallback
- query-hot-path benchmark evidence is refreshed with heavy-profile history or comparison artifacts
