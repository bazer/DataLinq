# 0.8 Phase 9 Review Findings: WebAssembly Warning and no-AOT Disposition

**Review date:** 2026-06-28.

**Reviewed scope:** WebAssembly warning classification, no-AOT/browser reporting behavior, current compatibility artifacts, and phase 9 docs in the `v0.8` branch through `57da59e2`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** One open warning-diagnostics finding. No stale public browser support claim was found.

## Findings

### P3: WebAssembly warning ownership is too coarse for the Phase 9 diagnostics contract

Phase 9 is supposed to make WebAssembly warning disposition visible enough to decide whether a warning is DataLinq-owned, SDK/toolchain-owned, or third-party dependency-owned.

`CompatibilityWarningClassifier.Classify(...)` currently short-circuits any WebAssembly target to `SdkOrWebAssembly` before checking third-party package names or DataLinq project paths (`src/DataLinq.DevTools/CompatibilityWarningClassifier.cs:57`). That means a DataLinq-owned IL warning or a package warning such as `SQLitePCLRaw` in a WASM publish can be reported as SDK/WebAssembly solely because the target is WebAssembly.

The current unit coverage only proves that a `WASM0001` warning on a WASM target is classified as SDK/WebAssembly (`src/DataLinq.Tests.Unit/CompatibilitySizeReportTests.cs:65`). It does not cover DataLinq-owned or third-party warnings emitted during a WASM publish.

This is not a release blocker by itself because the current docs still require manual symbol/call-path disposition for `SQLitePCLRaw`. But the report's owner summary is less useful than Phase 9 says it is.

Expected fix: classify by warning content and project/package ownership before falling back to target-level WebAssembly ownership. Add tests for DataLinq-owned, third-party, and `WASM0001` diagnostics on WebAssembly targets.

## Review Notes

- The no-AOT browser classification is directionally right: a failing no-AOT browser smoke becomes `unsupported (UnsupportedNoAot)`, not `n/a`.
- The docs correctly say publish success is not no-AOT browser support evidence.
- The current `WASM0001` discussion is appropriately narrow. It does not globally suppress SQLitePCLRaw WebAssembly warnings.

## Verification

Focused inspection:

```powershell
rg -n "WASM0001|UnsupportedNoAot|CompatibilityWarningOwner|Classify" src\DataLinq.DevTools src\DataLinq.Tests.Unit docs\dev-plans\roadmap-implementation\v0.8\phase-9-webassembly-warning-and-no-aot-disposition
```

Recommended focused unit check after fixing the classifier:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*" --output failures --build
```
