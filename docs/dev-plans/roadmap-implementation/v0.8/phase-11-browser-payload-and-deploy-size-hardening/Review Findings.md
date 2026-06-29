# 0.8 Phase 11 Review Findings: Browser Payload and Deploy-Size Hardening

**Review date:** 2026-06-28.

**Reviewed scope:** release-threshold code, payload inspector, size-report CLI docs, compatibility reporting docs, and phase 11 docs in the `v0.8` branch through `a978d85a`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved in the review-follow-up pass.

## Findings

### P2: WebAssembly Brotli gates pass when no Brotli assets are present

The Phase 11 release thresholds gate WebAssembly payload size on `brotliAssets.TotalBytes`. For no-AOT WASM this happens in `src/DataLinq.DevTools/CompatibilityReleaseThresholds.cs:41`; for WASM AOT it happens in `src/DataLinq.DevTools/CompatibilityReleaseThresholds.cs:50`.

If the publish output contains no `.br` assets because compression is disabled, broken, moved, or emitted under an unexpected path, the Brotli total is `0`. `0` is below both thresholds, so the report presents an excellent size signal instead of flagging missing compressed deploy evidence.

That undermines the Phase 11 goal. A browser payload gate should fail or warn when the compressed artifact it measures does not exist.

Expected fix: for WebAssembly targets, add a threshold finding when `BrotliAssets.FileCount == 0` or when the expected framework/app assets have no Brotli counterparts. Treat that finding as a threshold warning so `--fail-on-threshold` can make it a hard release gate.

## Resolution Notes

Resolved in the review-follow-up pass:

- `CompatibilityReleaseThresholds` now emits WebAssembly threshold warnings when Brotli asset count is zero, so `--fail-on-threshold` can hard-fail missing compressed deploy evidence.
- `CompatibilitySizeReportTests.ReleaseThresholds_FlagMissingWebAssemblyBrotliAssets` covers the missing-Brotli gate.

Focused verification: `CompatibilitySizeReportTests` passed 9/9.

## Review Notes

- The target-specific metrics are the right metrics: Native AOT executable size, symbol-excluded Native AOT folder size, trimmed symbol-excluded folder size, and Brotli browser payload.
- The report correctly separates symbols from runtime payload and keeps Roslyn banned-payload inspection in the same artifact.
- Threshold warnings are advisory unless `--fail-on-threshold` is used. That behavior is documented in the CLI docs; Phase 12 owns the final gate command issue where the flag is missing.

## Verification

Focused inspection:

```powershell
rg -n "release-wasm|Brotli|brotliAssets|FileCount|TotalBytes" src\DataLinq.DevTools src\DataLinq.Tests.Unit docs\dev-plans\roadmap-implementation\v0.8\phase-11-browser-payload-and-deploy-size-hardening
```

Related delegated verification:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*" --output failures --build
```

Result: compatibility unit slice passed 7/7 in the delegated Phase 10-12 review pass and 9/9 after the review-follow-up coverage was added.
