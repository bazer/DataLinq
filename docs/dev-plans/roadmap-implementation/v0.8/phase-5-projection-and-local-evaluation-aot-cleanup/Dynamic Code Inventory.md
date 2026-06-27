> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 5 Dynamic Code Inventory

**Status:** Live inventory after parser-local and projection fallback isolation slices.

## Purpose

This inventory tracks dynamic-code and reflection-invocation seams that matter for the 0.8 parser-removal path.

The useful distinction is not "reflection exists" versus "reflection does not exist." The useful distinction is whether supported parser/projection execution can run without arbitrary method invocation, expression compilation, or hidden dynamic-code fallbacks.

## Current Parser-Local Evaluation State

Owned files:

- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionLocalValueEvaluator.cs`

Current state:

- the expression parser no longer contains a broad local `MethodInfo.Invoke(...)` fallback
- local scalar and local sequence capture now route through `ExpressionLocalValueEvaluator`
- local constants, closure fields/properties, captured queryable roots, new arrays, `Array.Empty<T>()`, `Enumerable.Empty<T>()`, and local `Enumerable.Select(...)` membership projection remain supported
- arbitrary local method calls now fail during parsing instead of being invoked
- the parser-owned root query source path derives mapped `IQueryable<T>` roots from expression type metadata instead of invoking `Database.Query()`
- parser conversion now carries `ExpressionQueryPlanParserOptions`, including an `AotStrict` mode that rejects compatibility member reflection during local value evaluation

Remaining parser-local reflection:

- `ExpressionLocalValueEvaluator` still uses `FieldInfo.GetValue(...)` and `PropertyInfo.GetValue(...)` for captured closure/member reads in the default compatibility mode
- strict parser verification can now reject that fallback; generated/accessor-backed capture is still required before captured closure values can be claimed AOT-clean

Verification:

- `ExpressionQueryPlanParserTests.ExpressionParser_LocalMethodEvaluationFailsWithoutInvokingMethod` asserts unsupported local scalar and sequence method calls are rejected and not invoked
- `ExpressionQueryPlanParserTests.ExpressionParser_AotStrictLocalEvaluationRejectsCapturedMemberReflection` asserts strict parser local evaluation rejects captured-member reflection
- `ProjectionExpressionEvaluatorTests.CleanedParserAndProjectionSources_DoNotHideDynamicInvocationFallbacks` guards the cleaned parser/projection files against hidden method invocation, expression compilation, dynamic invocation, runtime array activation, and delegate creation
- focused parser parity still passes across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`

## Projection Execution State

Owned files:

- `src/DataLinq/Linq/ProjectionExpressionEvaluator.cs`
- `src/DataLinq/Linq/QueryExecutor.cs`

Current state:

- production row-local projection still calls `ProjectionExpressionEvaluator.Evaluate(...)` from `QueryExecutor`
- projection evaluation no longer uses an arbitrary `MethodInfo.Invoke(...)` fallback
- mapped DataLinq model value-property projection reads now use `IModelInstance.GetRowData()` and metadata columns instead of invoking generated property getters through reflection
- supported nullable `.HasValue`/`.Value` and `string.Length` projection members are interpreted explicitly
- supported string projection methods are interpreted explicitly
- unsupported projection methods now fail without invoking user code
- projection evaluation now carries `ProjectionEvaluationOptions`, including an `AotStrict` mode that rejects compatibility object construction and compatibility member reflection
- `PlatformSmokeRunner` now includes a strict parser/projection stage that parses a generated-model query with `ExpressionQueryPlanParserOptions.AotStrict`, renders SQL through `QueryPlanSqlBuilder`, and evaluates a supported scalar row projection with `ProjectionEvaluationOptions.AotStrict`
- anonymous/new-object projection still uses `ConstructorInfo.Invoke(...)` in the default compatibility mode
- projection evaluation still uses `FieldInfo.GetValue(...)` and `PropertyInfo.GetValue(...)` for non-model compatibility member reads in the default compatibility mode
- this is the main remaining Phase 5 implementation target

Required action:

- define the supported row-local projection interpreter boundary
- keep relation projection and nested database projection rejected
- route supported projection execution away from constructor invocation and broad non-model member reflection where practical
- expand constrained-platform verification beyond the current strict scalar projection smoke once generated-projector support exists for constructed result types

## Legacy Remotion-Backed Local Evaluation State

Owned files:

- `src/DataLinq/Linq/Evaluator.cs`
- `src/DataLinq/Linq/LocalSequenceExtractor.cs`
- `src/DataLinq/Linq/Planning/RemotionQueryPlanAdapter.cs`

Current state:

- Remotion-backed translation still uses `ProjectionExpressionEvaluator` for partial evaluation and local sequence projection
- these paths remain temporary oracle and compatibility scaffolding until Phase 6/7 remove the Remotion parser path

Required action:

- avoid expanding these paths while Phase 5 hardens the DataLinq parser path
- use them as parity or compatibility references only where they still add value

## Test-Only Reflection

Owned file:

- `src/DataLinq.Tests.Compliance/Translation/CurrentQueryTranslationInspection.cs`

Current state:

- test inspection helpers use `Activator.CreateInstance(...)` and reflected method invocation to inspect current translation internals
- this is test-only and not part of production execution

Required action:

- keep test-only reflection out of constrained-platform product gates
- remove or rewrite this scaffolding during the later Remotion cleanup phases if it starts rooting obsolete parser internals
