> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8 follow-up work. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 13: Source-Slot Join Follow-Up

**Status:** Backlog / 0.8.x follow-up after AOT release gates.

## Purpose

Phase 13 resumes join work after the query plan exists and after the 0.8 AOT/browser release gates are satisfied. This is where the old Phase 13 and Phase 14 plans become useful again, but rebased on DataLinq source slots instead of Remotion query-source identities.

This phase used to be the 0.8 Phase 8 follow-up. It moved to the back of the queue because 0.8 should prioritize making browser AOT actually run, report, and deploy at sensible sizes before broadening join composition.

Start from the current [LINQ Parser Architecture](../../../../internals/LINQ%20Parser%20Architecture.md), not from the older Remotion-shaped join notes. The existing source slots, `JoinedRowLocal` projection path, primary-key based joined materialization, and current join exclusions are the baseline to extend.

## Scope

In scope:

- preserve and strengthen the current narrow explicit `Join(...)` baseline
- add multi-source plan tests
- support filtering, ordering, paging, `Any`, and `Count` over joined row shapes
- keep joined materialization on provider-key components
- prepare relation-aware join APIs on top of the same source-slot model

Out of scope for the first follow-up slice:

- left joins before inner-join composition is stable
- relation-aware syntax that hides a weak explicit join engine
- arbitrary composite-key and grouped join shapes unless deliberately added

## Source Plans

- [Old Phase 13 Explicit Multi-Join Composition](../../phase-13-explicit-multi-join-composition/README.md)
- [Old Phase 14 Relation-Aware Joins and Left Joins](../../phase-14-relation-aware-joins-and-left-joins/README.md)
- [Relation-Aware Join API](../../../query-and-runtime/Relation-Aware%20Join%20API.md)

## Exit Criteria

- explicit joins compose through source-slot-aware plan nodes
- unsupported join shapes fail with focused diagnostics
- user docs and the support matrix describe only shipped join behavior
