> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 24 Implementation Plan

**Status:** Implemented.

## Objective

Close 0.8 with evidence instead of vibes.

Phase 24 is the final pass after:

1. Phase 22 cleans up the LINQ parser plan binding seam.
2. Phase 23 fixes the generated SQLite browser AOT runtime blocker and records the remaining clean-output WebAssembly SDK plus SQLitePCLRaw warning caveats.
3. Phase 24 reruns the full clean-output gate and supersedes the clean-output caveat with current passing evidence.

This phase records final compatibility, package, benchmark, and documentation evidence. It should not add new behavior.

## Work Items

- [x] Run final compatibility report.
  - Use clean output.
  - Use release thresholds.
  - Fail on threshold findings.
  - Fail on banned payload findings.
  - Record report directory and summary.
- [x] Pack release packages locally without publishing.
  - Use the repo's `publish-nuget.ps1 -PackOnly` workflow.
  - Keep package output under `artifacts\nuget-release\...`.
- [x] Run final package report.
  - Confirm Roslyn and Remotion stay out of runtime dependency groups.
  - Confirm analyzer assets are packaged as analyzers.
  - Record report directory and summary.
- [x] Run final focused AOT smoke.
- [x] Run final benchmark refresh.
  - `phase3-query-hotpath` heavy profile for parser/query hot paths.
  - `phase2-watch` heavy profile for provider startup, startup PK, and warm PK watchpoints.
  - Treat timing as evidence, not marketing copy.
- [x] Run final test sweep appropriate for release.
  - At minimum, focused unit/compliance slices touched by Phases 22 and 23.
  - Prefer full Testing CLI `--alias all` if local services and time allow.
- [x] Update public docs.
  - Platform compatibility wording must match final evidence: generated SQLite browser AOT passes, no-AOT passes the same narrow smoke, clean-output final report passes on the release machine, and `WASM0001` remains visible.
  - LINQ support wording must match the implemented query subset.
  - Internals docs should describe the final parser/binding shape.
  - Benchmark wording should distinguish allocation evidence from latency claims.
- [x] Update dev-plan closeout notes.
  - Phase 22 README: implementation evidence and benchmark artifacts.
  - Phase 23 README: browser AOT decision and artifact paths.
  - Phase 24 README: final release evidence paths.
  - v0.8 README: final status and support contract.
- [x] Build docs where feasible.
- [x] Prepare release-note/changelog wording.
  - Do not publish packages.
  - Do not automate release upload.

## Implementation Notes

The first Phase 24 clean-output report exposed a real release-gate problem in the smoke harness:

- report directory: `artifacts/dev/compat-size-report/20260630-130240109/`
- Native AOT and trimmed publish failed on `IL2026`
- root cause: `PlatformSmokeRunner.VerifyDocumentedSubsetCoverage(...)` used an anonymous join projection, which made the C# expression-tree compiler emit the trim-unsafe `Expression.New(ConstructorInfo, IEnumerable<Expression>, MemberInfo[])` overload

The fix keeps the same documented join coverage but changes the smoke projection to an explicit `PlatformSmokeJoinedTaskOwner` record. That avoids the trimmer-hostile anonymous-type member metadata path. This is release harness hardening, not a product query-behavior expansion.

Final compatibility evidence then passed with clean output and release thresholds:

- report: `artifacts/dev/compat-size-report/20260630-131026977/report.md`
- Native AOT: publish ok, smoke ok, 7.47 MB executable, 9.29 MB symbol-excluded folder, 0 warnings, 0 banned payloads
- Trimmed: publish ok, smoke ok, 22.79 MB symbol-excluded folder, 0 warnings, 0 banned payloads
- WebAssembly no-AOT: publish ok, browser smoke ok, 3.7 MB Brotli assets, 0 banned payloads, 13 `WASM0001` diagnostics
- WebAssembly AOT: publish ok, browser smoke ok, 7.06 MB Brotli assets, 0 banned payloads, 13 `WASM0001` diagnostics

Package evidence:

- package directory: `artifacts/nuget-release/v0.8-final`
- produced runtime packages: `DataLinq`, `DataLinq.SQLite`, `DataLinq.MySql`
- produced tool packages: `DataLinq.CLI`, `DataLinq.Tools`
- `package-report` confirms Roslyn stays out of `DataLinq`, `DataLinq.SQLite`, and `DataLinq.MySql` runtime dependency groups; `Microsoft.CodeAnalysis.CSharp` appears only in `DataLinq.Tools`
- `DataLinq` carries generator assets under `analyzers`, not runtime `lib` assets

Benchmark evidence:

- query hotpath history: `artifacts/benchmarks/history/v0.8-final-query-hotpath.json`
- query hotpath summary: `artifacts/benchmarks/results/20260630-131912069-17f558bd98aa4c24bae2537536bff983-summary.json`
- phase2 watch history: `artifacts/benchmarks/history/v0.8-final-phase2-watch.json`
- phase2 watch summary: `artifacts/benchmarks/results/20260630-132114783-483249215eea4cb2a5124c13c326718e-summary.json`

Benchmark interpretation is intentionally conservative. The query-hotpath run still has noisy rows, so it is not latency marketing evidence. The phase2 watch run is useful allocation evidence; warm primary-key fetch remains 1.77 KB allocated in both SQLite memory and file modes.

Final test evidence:

- full Testing CLI run: generators 39/39, unit 739/739, compliance 1732/1732, mysql 268/268
- standalone AOT smoke: passed
- docs build: passed

## Documentation Rules

Use narrow wording:

- "generated SQLite Native AOT" only if final AOT report passes
- "trimmed publish" only if final trimmed report passes
- "Blazor WebAssembly AOT" only inside the generated SQLite smoke boundary unless final browser smoke proves a broader path
- "documented query subset" instead of "LINQ support"
- "allocation reduction evidence" instead of "faster" unless benchmark history justifies latency wording

Avoid broad wording:

- "AOT-compatible"
- "full LINQ provider"
- "browser support" without AOT/no-AOT distinction
- "GroupBy support" without grouped aggregate projection scope
- "join support" without inner/left/multi-join boundaries

## Guardrails

- Do not publish NuGet packages.
- Do not broaden support docs beyond final artifact evidence.
- Do not hide red browser AOT evidence behind a green publish.
- Do not hide clean-output SDK failures behind a green non-clean browser smoke.
- Do not add new benchmark scenarios during the release pass unless they are clearly marked experimental and kept out of support claims.
- Do not treat noisy benchmark means as proof of latency improvement.
- Do not rewrite large docs unrelated to the 0.8 release boundary.

## Verification Plan

Compatibility:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

Packaging:

```powershell
.\publish-nuget.ps1 -PackOnly -PackageOutputPath artifacts\nuget-release\v0.8-final
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- package-report --package-dir artifacts\nuget-release\v0.8-final --format markdown
```

Smoke:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.AotSmoke
```

Benchmarks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile heavy --history-json artifacts\benchmarks\history\v0.8-final-query-hotpath.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile heavy --history-json artifacts\benchmarks\history\v0.8-final-phase2-watch.json
```

Tests:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --alias all --batch-size 6 --output failures
```

Docs:

```powershell
docfx build docfx.json
```

If any command cannot run locally because of toolchain, sandbox, service, or time constraints, record that explicitly in the closeout instead of silently omitting it.

Executed commands:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
# First run exposed IL2026 in the smoke harness; rerun after the smoke projection fix passed.

.\publish-nuget.ps1 -PackOnly -PackageOutputPath artifacts\nuget-release\v0.8-final
# Produced DataLinq, DataLinq.SQLite, DataLinq.MySql, DataLinq.CLI, DataLinq.Tools packages and symbols. PackOnly skipped push.

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- package-report --package-dir artifacts\nuget-release\v0.8-final --format markdown
# Passed package hygiene inspection.

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.AotSmoke
# DataLinq platform smoke passed.

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile heavy --history-json artifacts\benchmarks\history\v0.8-final-query-hotpath.json
# Completed with MultimodalDistribution warning.

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile heavy --history-json artifacts\benchmarks\history\v0.8-final-phase2-watch.json
# Completed.

$env:DATALINQ_TEST_DB_HOST='127.0.0.1'; .\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --alias all --batch-size 6 --output failures
# OK: 2778/2778 passed across generators, unit, compliance, and mysql suites.

docfx build docfx.json
# Passed.
```

## Exit Criteria

- Final reports and benchmark artifact paths are recorded in the phase docs or release notes.
- Public docs describe the exact final support boundary.
- Browser AOT is proven by Phase 23/24 artifacts for the generated SQLite smoke boundary or explicitly narrowed if final evidence regresses.
- Package report confirms release package dependency hygiene.
- Docs build is green or the blocker is documented.
- No NuGet publishing is performed by automation.
