> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 12: AOT Release Gates and Support Contract

**Status:** Planned 0.8 release work.

## Purpose

Phase 12 is the release gate phase. It promotes the narrow AOT/browser support statement only if the evidence is boring.

The release claim should be specific: generated SQLite models, documented query subset, Native AOT, trimmed publish, and Blazor WebAssembly AOT. Anything outside that boundary is either unsupported, experimental, or future work.

## Scope

In scope:

- run the final compatibility report and browser smoke evidence
- run package-report against fresh release packages
- verify generated SQLite Native AOT, trimmed publish, WASM AOT browser runtime, and payload thresholds
- update `docs/Platform Compatibility.md`, release notes, and support matrix links
- document no-AOT browser disposition
- document provider breadth: SQLite browser support only unless separately proven
- capture final benchmark history for query-hot-path and relevant macro smoke lanes

Out of scope:

- adding new query features during release gating
- broad "DataLinq is AOT-compatible" claims
- source-slot join expansion
- OPFS/file-backed browser storage unless it already has separate proof

## Exit Criteria

- final release evidence is linked from the 0.8 roadmap
- public docs say exactly what works and exactly what does not
- no release claim depends on an archived historical proof when current automation exists
- AOT/browser gates are green or the release explicitly narrows the claim
- source-slot join follow-up remains queued after the AOT release work

