> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 23 Implementation Plan

**Status:** Implemented.

## Objective

Debug the WebAssembly AOT browser failure far enough to make an honest release decision.

The known starting failure was:

```text
wasm-aot publishes successfully
browser smoke reaches opening-generated-database
browser runtime fails with MONO_WASM: function signature mismatch
```

The fixed failure boundary was the generic generated metadata startup path. `MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<TDatabase>()` used to wrap static abstract generated metadata hooks in delegates before building runtime metadata. Browser WebAssembly AOT failed that wrapper before the metadata definition could be built. The generic path now calls the generated static hooks directly.

There is still a clean-output publish issue recorded separately:

```text
clean-output wasm-aot publish can fail with MSB4057 for missing ResolveWasmOutputs
```

Those two failures must not be blurred together. The SDK/toolchain clean-output failure remains open. The runtime support blocker is fixed for the narrow generated SQLite smoke boundary.

## Work Items

- [x] Reproduce the current `wasm-aot` failure with the Phase 12 gate command.
  - Prefer `--clean-output`.
  - If clean output fails before browser execution, also run a non-clean or targeted command to preserve browser runtime evidence.
  - Record both artifacts separately.
- [x] Confirm the browser smoke reports the exact failing stage promptly.
  - Original coarse stage: `opening-generated-database`.
  - Final fixed run reaches `verifying-strict-parser-projection`.
  - Ensure `browser-smoke.log` includes DOM status, console output, page errors, and smoke result text.
- [x] Add temporary or permanent narrowed smoke stages only if the current stage is too coarse.
  - generated metadata draft
  - generated metadata definition
  - raw SQLite connection open without generated database wrapper
  - raw SQLite version and PRAGMA probes
  - keep-alive plus second raw connection pattern
  - generated database constructor
  - schema creation
  - first insert
  - first query
- [x] Investigate SQLitePCLRaw native import warnings.
  - Force a clean publish path that emits or proves absence of `WASM0001`.
  - Identify managed imports for `sqlite3_config` and `sqlite3_db_config`.
  - Decide whether those imports are reachable during provider registration or connection open.
  - Keep any suppression local to a smoke/project boundary and only with proof.
- [x] Test current no-AOT browser behavior.
  - Use the same browser smoke, not publish output.
  - It now passes in the same smoke boundary.
  - Keep no-AOT support wording separate from AOT support wording.
- [x] Try targeted runtime/provider configuration changes only after the failing boundary is understood.
  - Do not swap major provider packages without a small reproduction.
  - Do not hide the failure by skipping generated SQLite startup.
- [x] Decide the support outcome.
  - Fixed: record passing artifact paths and update support docs through Phase 24.
  - If still blocked: record failure artifacts and narrow public release wording through Phase 24.
- [x] Update Phase 23 README with the final evidence and outcome when implemented.

## Debugging Notes

Useful existing evidence:

- `artifacts/dev/compat-size-report/20260628-163740998/`
  - `wasm-aot` publish ok
  - browser smoke fails at `opening-generated-database`
  - console/page errors report `MONO_WASM: function signature mismatch`
- `artifacts/dev/compat-size-report/20260628-164853329/`
  - clean-output `wasm-aot` publish fails before browser execution with `ResolveWasmOutputs`

Those paths are historical evidence for orientation. Phase 23 needs fresh artifacts before release.

Fresh implementation evidence:

- `artifacts/dev/compat-size-report/20260629-201032570/`
  - clean-output `wasm-aot` publish failed before browser execution
  - classified as `SdkOrWebAssemblyToolchain`
- `artifacts/dev/compat-size-report/20260629-201125097/`
  - pre-fix non-clean `wasm-aot` publish succeeded
  - browser smoke failed at the old coarse generated database startup stage with `MONO_WASM: function signature mismatch`
- `artifacts/dev/compat-size-report/20260629-210510424/`
  - fixed non-clean `wasm-aot` publish and browser smoke pass
  - `wasm-aot/browser-smoke.log` reaches `passed` at `verifying-strict-parser-projection`
  - report: 203 files, 50.3 MB total, 7.07 MB Brotli assets, 10.26 MB Gzip assets, 0 banned payloads, 13 `WASM0001` diagnostics
- `artifacts/dev/compat-size-report/20260629-205036682/`
  - clean-output `wasm-aot` publish still fails before browser execution with `MSB4057` for missing `ResolveWasmOutputs`
- `artifacts/dev/compat-size-report/20260629-205114951/`
  - non-clean `wasm` publish and browser smoke pass
  - report: 203 files, 22.19 MB total, 3.7 MB Brotli assets, 4.68 MB Gzip assets, 0 banned payloads, 13 `WASM0001` diagnostics
- `artifacts/dev/compat-size-report/20260629-205211590/`
  - clean-output `wasm` publish still fails before browser execution with the same `ResolveWasmOutputs` SDK target issue

Root-cause notes:

- Raw SQLite open, `SELECT sqlite_version()`, `PRAGMA read_uncommitted`, `PRAGMA journal_mode`, and the keep-alive plus second-connection pattern all pass in the browser smoke.
- Generated metadata draft retrieval and definition building now have separate browser smoke stages.
- The fix keeps `WASM0001` visible. The generated SQLite smoke proves those warned SQLitePCLRaw varargs imports are not reached by the supported smoke path, but it does not prove every future browser storage/provider configuration is safe.

## Guardrails

- Do not claim browser AOT support if browser smoke does not reach `passed`.
- Do not use historical manual Phase 8 browser success as release evidence when current automation exists.
- Do not suppress `WASM0001` globally.
- Do not treat no-AOT as supported because its payload is small.
- Do not mix OPFS/file-backed storage work into this phase.
- Do not classify an SDK clean-output failure as a DataLinq query regression.

## Verification Plan

Primary release-style reproduction:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm-aot --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

If clean-output publish is blocked by SDK target failure, preserve browser runtime evidence with:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm-aot --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

No-AOT disposition:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm --clean-output --release-thresholds --format markdown
```

Full final compatibility run after any fix:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

Focused unit coverage after tooling changes:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*" --output failures --build
```

Implementation verification:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.PlatformCompatibility.Smoke\DataLinq.PlatformCompatibility.Smoke.csproj -v:minimal --no-restore
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/MetadataFromTypeFactoryTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm-aot --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm-aot --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm --release-thresholds --format markdown
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm --clean-output --release-thresholds --format markdown
```

## Exit Criteria

- Fresh `wasm-aot` browser evidence is recorded.
- The `MONO_WASM` failure is fixed for generated metadata startup.
- Current no-AOT browser behavior is recorded.
- SQLitePCLRaw warning disposition is documented as visible and unsuppressed, with passing smoke evidence for the supported generated SQLite path.
- Phase 24 can update release docs without guessing.
