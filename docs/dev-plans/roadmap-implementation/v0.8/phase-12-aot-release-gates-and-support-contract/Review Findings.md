# 0.8 Phase 12 Review Findings: AOT Release Gates and Support Contract

**Review date:** 2026-06-28.

**Reviewed scope:** package-report enforcement, size-report hard-failure behavior, public compatibility wording, and phase 12 docs in the `v0.8` branch through `a978d85a`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Open release-gate findings. Current browser AOT evidence remains red, so no browser support claim should be promoted.

## Findings

### P1: `package-report` does not enforce the Remotion runtime-dependency contract

The Phase 12 implementation plan says package evidence should prove Roslyn and Remotion stay out of runtime package dependency groups. `docs/Platform Compatibility.md` makes the corresponding public claim: runtime package dependency groups do not include `Microsoft.CodeAnalysis.*` or `Remotion.Linq`.

The package inspector enforces Roslyn, not Remotion:

- `src/DataLinq.DevTools/PackageInspector.cs:218` checks runtime dependencies only with `IsRoslynPackageId(...)`.
- `src/DataLinq.DevTools/PackageInspector.cs:229` checks runtime assets only for `Microsoft.CodeAnalysis*`.
- `src/DataLinq.DevTools/PackageInspectionModels.cs:12` has no Remotion-related finding kind.

A release package could reintroduce a `Remotion.Linq` runtime dependency and still pass `package-report`, even though that is one of the core 0.8 parser-removal and AOT support contracts.

Expected fix: add runtime dependency and runtime asset checks for `Remotion.Linq`/`Remotion.*` where appropriate, add hard-failure finding kinds, and add unit coverage showing package-report fails when a runtime package references Remotion.

### P1: The documented Phase 12 size gate does not fail threshold regressions

The gate command in `Implementation Plan.md:25` uses `--release-thresholds` but omits `--fail-on-threshold`.

That means the command can exit successfully with oversized payloads:

- `SizeReportCommand.cs:207` explicitly prints threshold findings as advisory unless `--fail-on-threshold` is used.
- `CompatibilitySizeReporter.cs:408` includes threshold warnings in hard failures only when `options.FailOnThresholdWarnings` is true.

For exploratory runs, advisory thresholds are fine. For Phase 12 release gates, a green exit code with exceeded release thresholds is not a gate.

Expected fix: update the Phase 12 gate command to include `--fail-on-threshold`, and consider documenting a separate advisory command for investigation.

## Review Notes

- Publish and smoke failures are already hard failures. The gap is threshold enforcement and Remotion package enforcement.
- Current public compatibility wording is appropriately narrow about browser support: it says Native AOT and trimmed smoke evidence are green, while WebAssembly AOT browser evidence is red.
- The release still needs a fresh final evidence artifact after these gate holes are fixed.

## Verification

Focused inspection:

```powershell
rg -n "Remotion|Microsoft\.CodeAnalysis|RuntimeRoslyn|PackageInspectionFindingKind|release-thresholds|fail-on-threshold|HasHardFailures" src\DataLinq.DevTools src\DataLinq.Dev.CLI docs\Platform* docs\dev-plans\roadmap-implementation\v0.8\phase-12-aot-release-gates-and-support-contract
```

Delegated verification:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.AotSmoke
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*" --output failures --build
```

Result:

- `DataLinq.AotSmoke` passed.
- `CompatibilitySizeReportTests`: 7/7 passed in the delegated Phase 10-12 review pass.

The passing unit tests do not cover the Remotion package-report gap or the missing `--fail-on-threshold` gate command.
