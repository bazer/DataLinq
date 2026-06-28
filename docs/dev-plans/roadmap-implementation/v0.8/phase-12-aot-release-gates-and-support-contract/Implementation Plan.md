> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 12 Implementation Plan: AOT Release Gates and Support Contract

**Status:** Release gate wiring implemented; current compatibility evidence blocks the browser AOT support claim.

## Goal

Promote only the support statement backed by current evidence.

The intended 0.8 claim is narrow, but the current branch is not allowed to make it yet:

> DataLinq supports generated SQLite models under Native AOT, trimmed publish, and Blazor WebAssembly AOT for the documented query subset.

That does not include arbitrary LINQ, reflection-discovered models, MySQL/MariaDB browser support, OPFS/file-backed browser storage, or no-AOT browser WebAssembly.

Current host-side browser evidence at `artifacts/dev/compat-size-report/20260628-163740998/` fails while opening generated SQLite in WebAssembly AOT with `MONO_WASM: function signature mismatch`. Until that passes, public release wording must stop short of claiming Blazor WebAssembly AOT support.

## Gate Commands

Compatibility runtime and payload evidence:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --clean-output --release-thresholds --fail-on-banned-payload --format markdown
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
- no-AOT browser WebAssembly remains unsupported unless the no-AOT browser smoke passes in a fresh report.
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

- `artifacts/dev/compat-size-report/20260628-163740998/`: `wasm-aot` publish ok, browser smoke failed at `opening-generated-database`.
- `artifacts/dev/compat-size-report/20260628-164853329/`: clean-output `wasm-aot` publish failed with `ResolveWasmOutputs`.

Do not link only to archived Phase 8 results. They are useful history, but the release support claim needs current automation.

## Exit Criteria

- Current compatibility report is green for the claimed support boundary; if browser AOT remains red, the claim must exclude browser AOT.
- Package report proves Roslyn and Remotion stay out of runtime package dependency groups.
- Public docs and changelog describe the exact support boundary.
- Phase 13 source-slot join follow-up remains behind the AOT/browser gates.
