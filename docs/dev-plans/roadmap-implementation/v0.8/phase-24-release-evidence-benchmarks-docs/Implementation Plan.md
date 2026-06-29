> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 24 Implementation Plan

**Status:** Planned.

## Objective

Close 0.8 with evidence instead of vibes.

Phase 24 is the final pass after:

1. Phase 22 cleans up the LINQ parser plan binding seam.
2. Phase 23 fixes the generated SQLite browser AOT runtime blocker and records the remaining clean-output WebAssembly SDK plus SQLitePCLRaw warning caveats.

This phase records final compatibility, package, benchmark, and documentation evidence. It should not add new behavior.

## Work Items

- [ ] Run final compatibility report.
  - Use clean output.
  - Use release thresholds.
  - Fail on threshold findings.
  - Fail on banned payload findings.
  - Record report directory and summary.
- [ ] Pack release packages locally without publishing.
  - Use the repo's `publish-nuget.ps1 -PackOnly` workflow.
  - Keep package output under `artifacts\nuget-release\...`.
- [ ] Run final package report.
  - Confirm Roslyn and Remotion stay out of runtime dependency groups.
  - Confirm analyzer assets are packaged as analyzers.
  - Record report directory and summary.
- [ ] Run final focused AOT smoke.
- [ ] Run final benchmark refresh.
  - `phase3-query-hotpath` heavy profile for parser/query hot paths.
  - `phase2-watch` heavy profile for provider startup, startup PK, and warm PK watchpoints.
  - Treat timing as evidence, not marketing copy.
- [ ] Run final test sweep appropriate for release.
  - At minimum, focused unit/compliance slices touched by Phases 22 and 23.
  - Prefer full Testing CLI `--alias all` if local services and time allow.
- [ ] Update public docs.
  - Platform compatibility wording must match Phase 23: generated SQLite browser AOT passes, no-AOT passes the same narrow smoke, clean-output WebAssembly publish remains a separate SDK/toolchain caveat, and `WASM0001` remains visible.
  - LINQ support wording must match the implemented query subset.
  - Internals docs should describe the final parser/binding shape.
  - Benchmark wording should distinguish allocation evidence from latency claims.
- [ ] Update dev-plan closeout notes.
  - Phase 22 README: implementation evidence and benchmark artifacts.
  - Phase 23 README: browser AOT decision and artifact paths.
  - Phase 24 README: final release evidence paths.
  - v0.8 README: final status and support contract.
- [ ] Build docs where feasible.
- [ ] Prepare release-note/changelog wording.
  - Do not publish packages.
  - Do not automate release upload.

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

## Exit Criteria

- Final reports and benchmark artifact paths are recorded in the phase docs or release notes.
- Public docs describe the exact final support boundary.
- Browser AOT is proven by Phase 23/24 artifacts for the generated SQLite smoke boundary or explicitly narrowed if final evidence regresses.
- Package report confirms release package dependency hygiene.
- Docs build is green or the blocker is documented.
- No NuGet publishing is performed by automation.
