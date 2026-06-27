> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 5: Projection and Local Evaluation AOT Cleanup

**Status:** In progress.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)

## Purpose

Phase 5 makes the supported generated/AOT path credible after the parser replacement. Query parsing can inspect expression metadata, but supported execution should avoid dynamic code and reflection invocation in hot projection/local-evaluation paths where practical.

## Scope

In scope:

- inventory reflection invocation in projection and local evaluation
- replace row-member reads with generated metadata/accessor paths where practical
- implement an interpreter or generated-projector strategy for supported row-local projections
- keep compatibility fallbacks separate from the generated/AOT support boundary
- make unsupported projection shapes fail clearly

Out of scope:

- arbitrary client method execution inside provider predicates
- SQL-backed projection expansion as a broad feature
- generated projectors for every expression shape

## Design Rule

Projection support should stay honest:

- SQL handles filtering, ordering, paging, scalar results, and join key selection.
- Row-local projection can run after materialization for supported shapes.
- Relation-property projection inside provider `Select(...)` remains unsupported unless explicitly designed.

## Exit Criteria

- supported generated/AOT projection execution avoids `Expression.Compile()`
- reflection invocation in supported projection/local paths is removed or explicitly isolated
- unsupported projection expressions fail with focused diagnostics
- constrained-platform smoke projects can use the new parser path without reintroducing dynamic-code debt
