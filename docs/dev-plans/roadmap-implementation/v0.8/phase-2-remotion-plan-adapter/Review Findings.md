> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 2 Review Findings

**Review date:** 2026-06-27.

**Reviewed scope:** Phase 2 commits from `1209601f` through `f11f212a`, excluding later Phase 3 planning material.

**Follow-up review date:** 2026-06-27.

**Follow-up reviewed scope:** `dc928bef` (`Fix 0.8 phase 2 review findings`).

**Current status:** Resolved. No open Phase 2 review findings remain from this pass.

## Resolved Findings

### P2: Unsupported projections can become client-expression plan nodes

**Status:** Resolved in `dc928bef`.

**File:** `src/DataLinq/Linq/Planning/RemotionQueryPlanAdapter.cs`

**Area:** `CreateProjection(...)`

The adapter falls back to `ComputedRowLocal` or `JoinedRowLocal` when it cannot classify a selector as entity, scalar member, or structured projection. That is too permissive for Phase 2.

Shapes that production translation deliberately rejects, such as relation-property projection (`Departments.Select(d => d.Managers)`), relation projection inside join result selectors, and nested database subquery projection, can become generic client-expression plan nodes instead of failing during adapter conversion.

Why this matters:

- Phase 2 is supposed to preserve Phase 1 unsupported-shape boundaries.
- Phase 3 SQL generation could later see these generic projection nodes and either generate wrong behavior or silently route unsupported query shapes into client projection.
- The plan snapshots would make the unsupported shape look intentional.

Required fix:

- Detect relation-property projections and nested database subquery projections before the generic projection fallback.
- Throw `QueryTranslationException` with DataLinq-owned wording.
- Add focused `QueryPlanAdapterUnsupportedShapeTests` coverage for relation projection, nested database subquery projection, and relation-property projection in explicit join selectors.

Resolution review:

- `CreateProjection(...)` now calls projection-shape validation before the generic computed/client projection fallback.
- Relation-property projections now throw a focused `QueryTranslationException`.
- Nested database subquery projections now throw a focused `QueryTranslationException`.
- Unsupported-shape coverage was added for direct relation projection, nested database subquery projection, and relation-property projection inside an explicit join selector.

### P2: Captured nullable null values get the wrong inequality null semantics

**Status:** Resolved in `dc928bef`.

**File:** `src/DataLinq/Linq/Planning/RemotionQueryPlanAdapter.cs`

**Area:** `ConvertComparison(...)`, `TryConvertValue(...)`, `IsNullableColumnAndNonNullValue(...)`

Literal `null` is represented as `QueryPlanConstantValue(null, ...)`, but captured null values are represented as `QueryPlanCapturedValue`. The current nullable inequality check treats every captured value as non-null:

```csharp
valueCandidate is not QueryPlanConstantValue { Value: null }
```

That means these two shapes can produce different semantics even though both compare against null:

```csharp
x.last_login != null

TimeOnly? login = null;
x.last_login != login
```

The second shape is currently marked with `CSharpNullableNotEqualIncludesNull`, which is correct for non-null captured values but wrong for captured null values. Comparing a nullable column to captured null should behave like `IS NOT NULL`, not like `column <> value OR column IS NULL`.

Why this matters:

- Phase 1 explicitly treats nullable inequality semantics as parser-migration contract.
- Phase 3 SQL generation will likely use `QueryPlanNullSemantics` to choose SQL null handling.
- A plan snapshot can claim the wrong null behavior while all actual production execution still passes through the old Remotion-to-SQL path.

Required fix:

- Preserve captured-value nullness in the plan shape when it affects semantics, or consult the binding frame while deriving nullable inequality semantics.
- Add snapshot coverage for captured-null and captured-non-null nullable inequality.
- Keep actual captured values redacted; only nullness/semantic class needs to be visible.

Resolution review:

- Nullable inequality null semantics are now derived through `GetComparisonNullSemantics(...)`, which checks scalar binding values for captured nulls.
- Captured null nullable inequality no longer records `CSharpNullableNotEqualIncludesNull`.
- Captured non-null nullable inequality still records `CSharpNullableNotEqualIncludesNull`.
- Unit and snapshot coverage now distinguish literal-null, captured-null, and captured-non-null nullable inequality shapes while keeping captured values redacted.

## Verification Context

Focused checks run during the original review:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI --no-build -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI --no-build -- run --suite compliance --filter "/*/*/QueryPlanAdapterUnsupportedShapeTests/*" --output failures
```

Results:

- `QueryPlanNodeTests`: 4/4 passed.
- `QueryPlanSnapshotTests`: 8/8 passed per batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `QueryPlanAdapterUnsupportedShapeTests`: 3/3 passed per batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.

One compliance build attempt was blocked by an existing `docfx` process holding `src/DataLinq.Generators/bin/Release/netstandard2.0/DataLinq.Generators.dll`; rerunning the compliance filters with `--no-build` succeeded.

Focused checks run during the follow-up review:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanAdapterUnsupportedShapeTests/*" --output failures --build
```

Results:

- `QueryPlanNodeTests`: 5/5 passed.
- `QueryPlanSnapshotTests`: 9/9 passed per batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `QueryPlanAdapterUnsupportedShapeTests`: 6/6 passed per batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
