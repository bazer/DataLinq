> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 5 Implementation Plan: Projection and Local Evaluation AOT Cleanup

**Status:** In progress.

**Created:** 2026-06-27.

## Purpose

Phase 5 makes the new parser path credible for constrained platforms by cleaning up the parts of query translation and post-materialization projection that still depend on dynamic invocation, expression compilation, or broad reflection fallbacks.

Phase 4 proved that DataLinq can parse supported expression trees into `DataLinqQueryPlan`. That is necessary but not sufficient. A parser that emits a plan and then quietly relies on dynamic projection execution is still not a defensible AOT story.

## Inputs

Phase 5 consumes:

- [Phase 4 Implementation Plan](../phase-4-supported-subset-expression-parser/Implementation%20Plan.md)
- [Phase 4 README](../phase-4-supported-subset-expression-parser/README.md)
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs`
- `src/DataLinq/Linq/ProjectionExpressionEvaluator.cs`
- `src/DataLinq/Linq/QueryExecutor.cs`
- generated metadata/accessor paths used by current materialization
- constrained-platform smoke projects and package/compatibility tooling

## Scope

In scope:

- inventory dynamic code and reflection invocation in parser-local evaluation, projection classification, and row-local projection execution
- keep supported scalar/local-value evaluation narrow and explicit
- replace supported row-member reads with metadata/accessor paths where practical
- add focused diagnostics for projection expressions that cannot be interpreted safely
- separate supported AOT-clean paths from compatibility fallbacks that remain temporarily Remotion-backed or reflection-backed
- add tests that prove supported projection/local evaluation paths do not use `Expression.Compile()`

Out of scope:

- changing the public query provider default
- removing Remotion package references
- arbitrary client method execution inside provider predicates
- broad SQL projection expansion
- generated projectors for every possible expression shape
- expanding relation projection or nested database projection support
- browser WebAssembly no-AOT support

## Workstreams

### A. Dynamic-Code Inventory

Goal: make the remaining dynamic execution debt concrete.

Tasks:

1. Search the query/parser/projection path for `Expression.Compile`, `DynamicInvoke`, reflection `Invoke`, constructor invocation, property getters, field getters, and delegate creation.
2. Classify each use as production execution, parser-local constant capture, test-only, or compatibility fallback.
3. Add a Phase 5 audit document with file/line ownership and required action.
4. Add narrow guardrail tests where a banned API would be dangerous to reintroduce silently.

Exit criteria:

- remaining dynamic-code uses are documented with owner and phase disposition
- supported parser/projection paths have tests or guardrails for the highest-risk APIs

### B. Supported Local Evaluation Boundary

Goal: keep captured scalar and local sequence evaluation explicit instead of letting arbitrary local expression execution leak into the supported parser.

Tasks:

1. Introduce a small local-expression evaluator for supported closure/member/constant shapes.
2. Support constants, captured closure fields/properties, nullable `.Value` where already supported, and local enumerable values needed for membership.
3. Reject method calls, constructors, indexers, and arbitrary expression execution unless the query translator already treats them as supported DataLinq functions.
4. Route the expression parser through the evaluator instead of ad hoc reflection calls.
5. Add tests for supported captured scalars, captured nulls, local sequences, and rejected arbitrary local method calls.

Exit criteria:

- parser-local value capture avoids arbitrary `Expression.Compile()`
- unsupported local evaluation shapes fail with focused DataLinq diagnostics

### C. Row-Local Projection Interpreter

Goal: make the supported post-materialization projection surface honest.

Tasks:

1. Identify the projection shapes currently represented by `QueryPlanProjection` and executed by `ProjectionExpressionEvaluator`.
2. Build or isolate an interpreter for supported row-local member reads, anonymous/new-object projection, simple scalar conversions, and supported computed scalar expressions.
3. Prefer existing generated metadata/accessor paths for model member reads.
4. Keep unsupported client expressions out of the constrained-platform support boundary.
5. Add projection tests that run through the same materialized-row path as production execution.

Exit criteria:

- supported row-local projections do not require `Expression.Compile()`
- unsupported projection expressions produce focused diagnostics instead of late dynamic invocation failures

### D. Compatibility Fallback Isolation

Goal: keep temporary non-AOT behavior visible instead of pretending it is part of the supported path.

Tasks:

1. Name and isolate compatibility-only projection/local evaluation fallbacks.
2. Ensure the new parser path can opt out of those fallbacks for constrained-platform verification.
3. Add tests or smoke gates that fail if constrained-platform paths route through dynamic projection fallback.
4. Document any fallback that must survive until Phase 6 or Phase 7.

Exit criteria:

- fallback code is mechanically distinguishable from supported generated/AOT paths
- constrained-platform verification can assert that fallback was not used

### E. Verification and Closeout

Goal: prove the cleanup without claiming more support than exists.

Tasks:

1. Run focused parser/projection/local-evaluation tests.
2. Run unit quick and compliance quick.
3. Run constrained-platform smoke or package compatibility checks that are available locally.
4. Update Phase 5 README with closeout evidence and handoff to Phase 6.

Exit criteria:

- focused tests prove supported local evaluation and row-local projection behavior
- broad quick suites pass
- remaining dynamic-code debt is either removed or explicitly isolated with owner and rationale

## Recommended Implementation Order

1. Add this implementation plan and start the dynamic-code inventory.
2. Add focused tests around the parser-local value capture boundary.
3. Implement the narrow local-expression evaluator and route the expression parser through it.
4. Inventory `ProjectionExpressionEvaluator` and add failing tests for the supported projection boundary.
5. Implement or isolate the row-local projection interpreter.
6. Add constrained-platform guardrails for the supported path.
7. Run broad verification and close Phase 5.

## Verification

Focused verification:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build
```

Broad verification:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Constrained-platform verification should use the existing AOT/trim smoke projects or package tooling available when the implementation slice reaches that boundary. Blazor WebAssembly build failures inside the sandbox must be verified outside the sandbox before being classified as product failures.

## Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| The parser accepts arbitrary local execution through a convenient evaluator | High | Keep the evaluator allow-list small and fail closed with focused diagnostics. |
| Projection cleanup quietly changes public query results | High | Test through production materialization and keep Remotion-backed behavior as oracle where useful. |
| AOT cleanup turns into broad projection feature work | Medium | Implement only documented projection shapes and reject the rest. |
| Reflection is removed from one path but reintroduced through generated metadata fallback | Medium | Add guardrails for `Expression.Compile()` and classify remaining reflection by owner. |
| Constrained-platform smoke failures are sandbox artifacts | Medium | Follow repo guidance and verify suspected sandbox-only failures outside the sandbox before treating them as product bugs. |
| Compatibility fallback becomes the default new-parser path | High | Make fallback usage explicit and assert it is absent from constrained-platform verification. |

## Exit Criteria

Phase 5 is complete when:

- supported generated/AOT projection execution avoids `Expression.Compile()`
- parser-local value capture avoids arbitrary dynamic expression execution
- reflection invocation in supported projection/local paths is removed or explicitly isolated
- unsupported projection and local-evaluation expressions fail with focused DataLinq diagnostics
- constrained-platform smoke or compatibility gates can use the new parser path without dynamic-code fallback
- focused unit and compliance tests pass
- unit quick and compliance quick pass

## Phase 6 Handoff

Phase 6 should receive:

- a parser path whose supported local evaluation and projection behavior is explicit
- a list of any compatibility-only fallbacks still present
- tests that can run dual parser parity without relying on dynamic projection execution
- constrained-platform evidence that the new parser path does not reintroduce the dynamic-code debt Phase 5 removed or isolated
