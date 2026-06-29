> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 23 Implementation Plan

**Status:** Planned.

## Objective

Debug the current WebAssembly AOT browser failure far enough to make an honest release decision.

The known failure is:

```text
wasm-aot publishes successfully
browser smoke reaches opening-generated-database
browser runtime fails with MONO_WASM: function signature mismatch
```

There is also a clean-output publish issue recorded separately:

```text
clean-output wasm-aot publish can fail with MSB4057 for missing ResolveWasmOutputs
```

Those two failures must not be blurred together. One is SDK/toolchain evidence. The other is the runtime support blocker.

## Work Items

- [ ] Reproduce the current `wasm-aot` failure with the Phase 12 gate command.
  - Prefer `--clean-output`.
  - If clean output fails before browser execution, also run a non-clean or targeted command to preserve browser runtime evidence.
  - Record both artifacts separately.
- [ ] Confirm the browser smoke reports the exact failing stage promptly.
  - Expected current stage: `opening-generated-database`.
  - Ensure `browser-smoke.log` includes DOM status, console output, page errors, and smoke result text.
- [ ] Add temporary or permanent narrowed smoke stages only if the current stage is too coarse.
  - provider registration only
  - raw SQLite connection open without generated database wrapper
  - generated database constructor
  - schema creation
  - first insert
  - first query
- [ ] Investigate SQLitePCLRaw native import warnings.
  - Force a clean publish path that emits or proves absence of `WASM0001`.
  - Identify managed imports for `sqlite3_config` and `sqlite3_db_config`.
  - Decide whether those imports are reachable during provider registration or connection open.
  - Keep any suppression local to a smoke/project boundary and only with proof.
- [ ] Test current no-AOT browser behavior.
  - Use the same browser smoke, not publish output.
  - If it fails, record the exact stage and runtime method.
  - Keep no-AOT explicitly unsupported unless it runs.
- [ ] Try targeted runtime/provider configuration changes only after the failing boundary is understood.
  - Do not swap major provider packages without a small reproduction.
  - Do not hide the failure by skipping generated SQLite startup.
- [ ] Decide the support outcome.
  - If fixed: record passing artifact paths and update support docs through Phase 24.
  - If still blocked: record failure artifacts and narrow public release wording through Phase 24.
- [ ] Update Phase 23 README with the final evidence and outcome when implemented.

## Debugging Notes

Useful existing evidence:

- `artifacts/dev/compat-size-report/20260628-163740998/`
  - `wasm-aot` publish ok
  - browser smoke fails at `opening-generated-database`
  - console/page errors report `MONO_WASM: function signature mismatch`
- `artifacts/dev/compat-size-report/20260628-164853329/`
  - clean-output `wasm-aot` publish fails before browser execution with `ResolveWasmOutputs`

Those paths are historical evidence for orientation. Phase 23 needs fresh artifacts before release.

## Guardrails

- Do not claim browser AOT support if browser smoke does not reach `passed`.
- Do not use historical manual Phase 8 browser success as release evidence when current automation fails.
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

## Exit Criteria

- Fresh `wasm-aot` browser evidence is recorded.
- The `MONO_WASM` failure is fixed or classified with enough detail to support explicit release exclusion.
- Current no-AOT browser behavior is recorded.
- SQLitePCLRaw warning disposition is documented or explicitly left as a blocker.
- Phase 24 can update release docs without guessing.
