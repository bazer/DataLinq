> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 11 Implementation Plan: Browser Payload and Deploy-Size Hardening

**Status:** Implemented in compatibility reporting; final numbers come from the release report artifact.

## Goal

Turn compatibility payload measurements into release gates with target-specific metrics.

One global folder-size threshold is not good enough. Native AOT, trimmed self-contained output, no-AOT WASM, and WASM AOT have different meaningful size signals.

## Implementation

Added `size-report --release-thresholds`.

The option applies these 0.8 thresholds:

| Target | Metric | Threshold |
| --- | --- | ---: |
| Native AOT | executable size | 20 MiB |
| Native AOT | symbol-excluded publish folder | 25 MiB |
| Trimmed | symbol-excluded publish folder | 25 MiB |
| Blazor WASM no-AOT | Brotli assets | 6 MiB |
| Blazor WASM AOT | Brotli assets | 12 MiB |

The report keeps symbols separate from runtime payload. That is not cosmetics. Native PDBs can dominate the raw publish folder and should not be counted as deployed constrained-platform runtime size.

The existing generic options remain available for investigations:

- `--max-total-size-mb`
- `--max-symbol-excluded-size-mb`
- `--max-file-count`
- `--fail-on-threshold`

With `--release-thresholds --fail-on-threshold`, threshold warnings become a local release gate.

## Verification

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --release-thresholds --format summary
```

Use `--clean-output` when the result is meant to answer the WebAssembly warning question, not just payload drift.

## Exit Criteria

- Release thresholds are target-specific.
- Native AOT size wording uses executable and symbol-excluded metrics.
- Browser size wording uses compressed Brotli assets.
- Banned Roslyn payload checks remain in the same report.
- Public docs explain payload tradeoffs without implying broad AOT compatibility.

