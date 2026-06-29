> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 22 Implementation Plan

**Status:** Planned.

## Objective

Make the new LINQ parser's binding layer actually immutable at the plan boundary and remove the obvious allocation-heavy binding lookup from SQL rendering.

This phase exists because the parser architecture review found a narrow, defensible release-hardening slice:

- `QueryPlanBindingFrame` owns a mutable list and returns a new `ReadOnlyCollection<T>` wrapper on every `Bindings` access.
- `QueryPlanSqlValueRenderer.GetBinding(...)` searches bindings with LINQ and materializes an array for each lookup.
- local sequence rendering copies values again even when the binding already owns a protected snapshot.

Fixing that now improves immutability, cache-readiness, and allocation behavior without broadening the parser.

## Work Items

- [ ] Introduce an immutable binding snapshot type for plan-owned bindings.
  - Candidate name: `QueryPlanBindings`.
  - It should own a frozen binding array or equivalent immutable storage.
  - It should expose stable enumeration without allocating wrappers on every access.
  - It should expose O(1) lookup by binding id.
- [ ] Convert `QueryPlanBindingFrame` into parser-time builder state.
  - Keep parser mutation local to `ExpressionQueryPlanParser`.
  - Add a `Freeze()` or equivalent method that returns the immutable snapshot.
  - Validate duplicate binding ids during freeze.
- [ ] Update `DataLinqQueryPlan` to own immutable bindings.
  - Prefer constructing the immutable snapshot at the plan boundary, not inside renderers.
  - Ensure plan snapshots and debug output still redact values.
- [ ] Update binding consumers.
  - `QueryPlanSqlValueRenderer`
  - `QueryPlanSqlPredicateBuilder`
  - `QueryPlanSqlBuilder`
  - `QueryPlanNullSemanticsResolver`
  - `QueryPlanDebugWriter`
  - tests that currently instantiate `QueryPlanBindingFrame`
- [ ] Replace SQL renderer binding lookup.
  - Remove LINQ search plus `Take(2).ToArray()`.
  - Use the immutable snapshot's lookup API.
  - Keep error messages for missing and wrong-kind bindings focused.
- [ ] Reduce avoidable local-sequence copies.
  - Keep a defensive copy when capturing caller-provided local sequence values.
  - Avoid a second copy when rendering if the downstream API can accept a read-only value collection.
  - If a legacy `Operand.Value(object?[])` API still forces an array, localize the remaining copy and document why.
- [ ] Add tests for immutability and lookup behavior.
  - Mutating the original local sequence after capture must not affect the plan.
  - Enumerating plan bindings repeatedly must not expose mutable state.
  - missing binding ids still throw clear translation exceptions.
  - local sequence membership still renders provider parameters correctly.
- [ ] Run focused parser/SQL/binding verification.
- [ ] Run focused allocation benchmarks and record the artifact paths in the phase README when implemented.

## Guardrails

- Do not add query-shape caching in this phase. This phase prepares that work; it does not implement it.
- Do not change binding ids in a way that breaks existing plan snapshot readability without a deliberate snapshot update.
- Do not expose the backing binding array in a mutable form.
- Do not replace clear translation exceptions with generic key lookup failures.
- Do not add new LINQ support while touching the parser.
- Do not optimize away the defensive copy at capture time. Caller-owned arrays and lists must not become plan-owned mutable state.

## Suggested Implementation Shape

The intended end state is:

```text
ExpressionQueryPlanParser
  owns QueryPlanBindingFrame builder
  captures scalar/local values during parse
  freezes builder when constructing DataLinqQueryPlan

DataLinqQueryPlan
  owns QueryPlanBindings immutable snapshot

QueryPlanSqlValueRenderer
  resolves bindings through QueryPlanBindings.GetRequired(id)
  performs no LINQ enumeration for binding lookup
```

The exact type names can change. The boundary cannot.

## Verification Plan

Run focused tests first:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
```

Run focused compliance coverage:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*|/*/*/QueryPlanSqlParityTests/*|/*/*/EmployeesContainsTranslationTests/*|/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
```

Run allocation evidence after the code is stable:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile heavy --history-json artifacts\benchmarks\history\v0.8-phase22-phase2-watch.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile heavy --history-json artifacts\benchmarks\history\v0.8-phase22-query-hotpath.json
```

Always finish with:

```powershell
git diff --check
git status --short
```

## Exit Criteria

- Plan-owned bindings are immutable by type, not by convention.
- Render-time binding lookup is O(1) and does not allocate LINQ helper arrays.
- Captured local sequence values remain protected from caller mutation.
- Focused unit and compliance tests pass.
- Focused benchmark evidence is captured or the implementation notes explain why benchmark execution was not possible on the machine.
- No public LINQ support matrix entry changes.
