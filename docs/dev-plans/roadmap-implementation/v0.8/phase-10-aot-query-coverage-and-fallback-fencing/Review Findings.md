# 0.8 Phase 10 Review Findings: AOT Query Coverage and Fallback Fencing

**Review date:** 2026-06-28.

**Reviewed scope:** constrained platform smoke runner, generated smoke model, AOT executable smoke, phase 10 docs, and compatibility report wiring in the `v0.8` branch through `57da59e2`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** No blocking findings. No open Phase 10 review findings remain from this pass.

## Findings

No actionable findings.

The constrained smoke covers the selected 0.8 query subset without expanding the public support matrix by accident. It exercises generated metadata startup, generated inserts, relation loading, row-local projection, DataLinq expression parser routing, strict parser/projection checks, scalar aggregates, paging, relation predicates, explicit join baseline, local membership, nullable predicates, and unsupported diagnostics.

## Review Notes

- `PlatformSmokeRunner` uses `ExpressionQueryPlanProvider.ForExecution(...)` and strict parser/projection checks where the constrained-platform boundary needs them.
- The explicit join coverage in this phase proves the narrow join baseline in the constrained smoke. It deliberately does not prove Phase 14 provider-side joined composition; that belongs to Phase 14 and has separate compliance coverage.
- The smoke avoids tempting unsupported shapes such as post-paging `Single()` and relation boolean shorthand. That restraint is correct; AOT proof should not smuggle feature expansion into the release gate.
- Native AOT and trimmed browser claims still depend on the broader Phase 11/12 gates; this phase only proves the selected query subset inside the smoke.

## Verification

Focused verification run in delegated review:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.AotSmoke
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*" --output failures --build
```

Result:

- `DataLinq.AotSmoke` passed and printed the expected platform smoke summary.
- `CompatibilitySizeReportTests`: 7/7 passed in the delegated Phase 10-12 review pass.

The full WebAssembly publish/browser gate was not rerun in the sandbox because the repo documents native-Windows sandbox WebAssembly builds as unreliable. Current host-side WebAssembly evidence remains the Phase 8/9 artifact set.
