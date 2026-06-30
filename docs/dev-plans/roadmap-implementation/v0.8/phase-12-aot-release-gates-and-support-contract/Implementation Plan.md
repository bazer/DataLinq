> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 12 Implementation Plan: AOT Release Gates and Support Contract

**Status:** Release gate wiring implemented; Phase 24 current compatibility evidence unblocks the narrow generated SQLite constrained-platform claim, with SQLitePCLRaw warning caveats.

## Goal

Promote only the support statement backed by current evidence.

The intended 0.8 claim is narrow:

> DataLinq supports generated SQLite models under Native AOT, trimmed publish, and Blazor WebAssembly AOT for the documented query subset.

That does not include arbitrary LINQ, reflection-discovered models, MySQL/MariaDB browser support, OPFS/file-backed browser storage, or no-AOT browser WebAssembly beyond the current generated SQLite smoke boundary.

Historical host-side browser evidence at `artifacts/dev/compat-size-report/20260628-163740998/` failed while opening generated SQLite in WebAssembly AOT with `MONO_WASM: function signature mismatch`. Phase 23 superseded that with `artifacts/dev/compat-size-report/20260629-210510424/`, which publishes `wasm-aot` and passes browser smoke at `verifying-strict-parser-projection`. Phase 24 supersedes the clean-output SDK caveat with `artifacts/dev/compat-size-report/20260630-131026977/report.md`, which passes Native AOT, trimmed, `wasm`, and `wasm-aot` from clean output with release thresholds and banned-payload failure enabled.

## Gate Commands

Compatibility runtime and payload evidence:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

Package evidence after packing release packages:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- package-report --package-dir artifacts\nuget-release\<timestamp> --format markdown
```

Focused query smoke:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.AotSmoke
```

Benchmark evidence:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile heavy --history-json artifacts\benchmarks\history\v0.8-aot-query-hotpath.json
```

## Support Contract Rules

- Browser support requires browser runtime evidence, not just `dotnet publish`.
- no-AOT browser WebAssembly remains scoped to the current generated SQLite smoke unless broader no-AOT evidence is added deliberately.
- SQLitePCLRaw `WASM0001` is not suppressed globally. If it is suppressed locally later, the suppression must point at the smoke proof and symbol disposition.
- Native AOT size uses executable and symbol-excluded folder metrics.
- WASM size uses compressed Brotli asset totals.
- Public docs must say "generated SQLite" and "documented query subset".

## Release Evidence Links

The final closeout should record:

- compatibility report directory
- package report directory
- browser smoke log path for `wasm-aot`
- no-AOT smoke status
- warning diagnostic summary
- threshold findings
- benchmark history path

Current evidence to carry forward:

- `artifacts/dev/compat-size-report/20260630-131026977/report.md`: final clean-output `phase8c` report; Native AOT, trimmed, `wasm`, and `wasm-aot` publish and smoke passed.
- `artifacts/dev/compat-size-report/20260629-210510424/`: `wasm-aot` publish ok, browser smoke passed at `verifying-strict-parser-projection`.
- `artifacts/dev/compat-size-report/20260629-205114951/`: `wasm` publish ok, browser smoke passed at `verifying-strict-parser-projection`.
- `artifacts/dev/compat-size-report/20260629-205036682/`: clean-output `wasm-aot` publish failed with `ResolveWasmOutputs`.
- `artifacts/dev/compat-size-report/20260629-205211590/`: clean-output `wasm` publish failed with `ResolveWasmOutputs`.

Do not link only to archived Phase 8 results. They are useful history, but the release support claim needs current automation.

## Exit Criteria

- Current compatibility report is green for the claimed support boundary; if browser AOT regresses red, the claim must exclude or narrow browser AOT.
- Package report proves Roslyn and Remotion stay out of runtime package dependency groups.
- Public docs and changelog describe the exact support boundary.
- Phase 13 query-composition hardening and the Phase 14/15 join follow-ups remain behind the AOT/browser gates.
