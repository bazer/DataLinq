> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 10 Implementation Plan: AOT Query Coverage and Fallback Fencing

**Status:** Implemented for the selected 0.8 constrained smoke subset.

## Goal

Make constrained-platform proof cover the selected documented query subset, not only a single representative query.

The browser, Native AOT, and trimmed paths should share the same smoke runner so evidence cannot drift between platforms.

## Implementation

Updated `src/DataLinq.PlatformCompatibility.Smoke/PlatformSmokeRunner.cs` and `PlatformSmokeModel.cs`.

The shared smoke now covers:

- generated SQLite schema creation from generated metadata
- generated mutable inserts
- generated relation loading
- row-local projection
- expression parser route evidence
- strict parser/projection checks with `AotStrict`
- direct numeric aggregates: `Sum`, `Min`, `Max`, `Average`
- `Any(predicate)`
- ordered `Skip(...).Take(...)` paging over materialized sequence results
- one-to-many relation predicate using a simple related-row comparison
- explicit inner `Join(...)` with direct member keys
- local collection membership through `Contains(...)`
- nullable value predicate with guarded `.Value`
- unsupported predicate diagnostics through `QueryTranslationException`

The nullable predicate coverage added an `estimate_hours` nullable SQLite column to the smoke task model.

## Important Boundaries Found During Implementation

Two tempting query shapes were deliberately not promoted:

- `OrderBy(...).Skip(...).Take(...).Single()` is not used as proof, because result-operator composition after paging is not the supported boundary being claimed here.
- relation predicate `task => !task.Completed` is not used as proof, because the current relation predicate implementation supports simple comparisons. The smoke uses `task.Completed == false`.

That is the right outcome. Phase 10 should prove the documented subset, not smuggle new support claims into the release gate.

## Verification

Fast smoke:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.AotSmoke
```

Expected output includes:

```text
DataLinq platform smoke passed
coverage=sum:6, avg:2, page:"Publish AOT smoke", relation-owners:1, join:"Ada:Compile generated hooks", local:2, nullable:1
unsupported-diagnostic="The LINQ query cannot be translated: Method 'IsKnownTaskTitle' ...
```

Release evidence:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets aot,trim,wasm-aot --release-thresholds --format summary
```

## Exit Criteria

- Shared smoke coverage includes the selected documented subset.
- Unsupported diagnostics are part of the constrained smoke result.
- The smoke keeps using strict AOT parser/projection checks.
- The public LINQ docs remain unchanged except for evidence notes; this phase does not expand the support matrix.

