> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 9 Implementation Plan: WebAssembly Warning and no-AOT Disposition

**Status:** Tooling implemented; current evidence keeps browser WebAssembly support blocked.

## Goal

Stop treating WebAssembly warning silence and no-AOT publish success as support evidence.

Phase 9 has two separate jobs:

1. Force fresh WebAssembly publishes when investigating `WASM0001`.
2. Record current no-AOT browser behavior as either supported or explicitly unsupported from a real browser run.

## Implementation

### Workstream A: Clean Publish Mode

Added `size-report --clean-output`.

The option deletes only `bin` and `obj` under the selected project directory after verifying the paths stay inside the repository. Use it when the warning question matters, because incremental WebAssembly publishes can hide `WASM0001` warnings.

Recommended command:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm,wasm-aot --clean-output --release-thresholds --format markdown
```

### Workstream B: Warning Diagnostics In Report Markdown

The compatibility report already stored warning diagnostics in JSON. The Markdown report now also includes a `Warning Diagnostics` section per target with:

- owner classification
- warning code
- count
- diagnostic text

That makes `WASM0001` evidence visible in the artifact most humans read first.

### Workstream C: no-AOT Browser Classification

The no-AOT target now runs the same browser smoke as the AOT target. If it fails in the browser, the smoke status is `unsupported (UnsupportedNoAot)` instead of `n/a`.

That distinction matters:

- `n/a` means no runtime evidence was collected
- `unsupported` means the runtime path was tested and failed or remained outside the support boundary
- `failed` means a supported release gate failed

## SQLitePCLRaw Warning Surface

The installed `SQLitePCLRaw.provider.e_sqlite3` 3.0.3 provider exposes the WebAssembly-varargs symbols through:

- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_config_none`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_config_int`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_config_int_arm64cc`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_config_log`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_config_log_arm64cc`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_db_config_charptr`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_db_config_charptr_arm64cc`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_db_config_int_outint`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_db_config_int_outint_arm64cc`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_db_config_intptr_int_int`
- `SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_db_config_intptr_int_int_arm64cc`

The public SQLitePCLRaw surface that can reach those imports is:

- `SQLitePCL.raw.sqlite3_config(...)`
- `SQLitePCL.raw.sqlite3_config_log(...)`
- `SQLitePCL.raw.sqlite3_db_config(...)`
- matching `SQLitePCL.ISQLite3Provider` members

The generated SQLite smoke path does not intentionally call SQLite global configuration or database configuration APIs. It registers the provider, opens an in-memory connection, enables foreign keys through SQL, creates schema, inserts rows, runs queries, and loads relations. That is a narrow path proof, not a proof that every SQLitePCLRaw config API is browser-safe.

## Current Evidence

- `artifacts/dev/compat-size-report/20260628-164853329/` is the host-side clean-output `wasm-aot` run. It fails during publish with `MSB4057` because the Blazor SDK target graph references `ResolveWasmOutputs`.
- `artifacts/dev/compat-size-report/20260628-163740998/` is the host-side non-clean `wasm-aot` run. It publishes successfully and then fails in the browser at `opening-generated-database` with `MONO_WASM: function signature mismatch`.
- Earlier non-clean publish output emitted the expected `WASM0001` diagnostics for `sqlite3_config` and `sqlite3_db_config`, which are listed above.

The honest interpretation is that the warning is not harmless enough to suppress from the library packages. It lines up with a current browser runtime failure on the SQLite open path. The next fix should either change the browser SQLite provider/configuration path or produce a passing browser report with a very narrow suppression and proof.

## Exit Criteria

- Clean WebAssembly publish mode exists.
- Warning diagnostics are visible in Markdown and JSON report artifacts.
- no-AOT browser runtime is tested and classified from the browser smoke result.
- Public docs keep no-AOT unsupported unless a fresh browser report says it passes.
- No global `WASM0001` suppression is added to DataLinq library packages.
