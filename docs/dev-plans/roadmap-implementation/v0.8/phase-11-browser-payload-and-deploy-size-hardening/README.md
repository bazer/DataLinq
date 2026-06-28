> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 11: Browser Payload and Deploy-Size Hardening

**Status:** Planned 0.8 release work.

## Purpose

Phase 11 turns current good payload numbers into release discipline. The 2026-06-28 report has WebAssembly AOT at 6.99 MB Brotli and no-AOT publish at 3.28 MB Brotli for the smoke app. That is good, but a release needs gates, not lucky measurements.

## Scope

In scope:

- define release thresholds for WASM AOT Brotli, no-AOT Brotli, Native AOT executable, symbol-excluded Native AOT folder, and trimmed folder size
- fail or warn on banned runtime payload such as Roslyn/compiler assets
- separate Native AOT symbols from deployed runtime size
- document largest payload contributors and whether they are fixed costs, DataLinq costs, SQLite costs, or app costs
- make clean publish size reporting reproducible
- update deployment guidance so users understand AOT size tradeoffs

Out of scope:

- chasing arbitrary sub-megabyte browser payloads
- removing SQLite to make the smoke app look smaller
- counting `.pdb` files as deployed runtime size

## Exit Criteria

- payload thresholds exist and are wired into local release evidence
- package/runtime reports prove Roslyn and Remotion do not re-enter constrained outputs
- Native AOT release-size wording uses symbol-excluded numbers
- browser AOT payload is within the release target or has a documented exception
- public docs explain AOT payload tradeoffs without marketing varnish

