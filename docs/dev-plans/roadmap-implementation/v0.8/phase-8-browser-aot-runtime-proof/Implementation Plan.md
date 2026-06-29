> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 8 Implementation Plan: Browser AOT Runtime Proof

**Status:** Implemented in tooling; Phase 23 current host-side WebAssembly AOT browser evidence passes after generated metadata startup fix.

## Goal

Make WebAssembly AOT browser proof a repo command, not a memory of a manual test.

The compatibility report must publish the Blazor WebAssembly AOT smoke app, serve the published `wwwroot` over HTTP, open it in a real headless browser, wait for the DataLinq smoke status, and write evidence that can be cited from release docs.

## Implementation

### Workstream A: Browser-Readable Smoke Contract

Updated `src/DataLinq.BlazorWasm/Pages/Home.razor` so the smoke app exposes stable DOM markers:

- `data-datalinq-smoke-status`
- `data-datalinq-smoke-stage`
- `#datalinq-smoke-status`
- `#datalinq-smoke-stage`
- `#datalinq-smoke-error`
- `#datalinq-smoke-result`

The existing `wwwroot/index.html` boot-status and console-log capture remain useful for failures before Blazor renders.

### Workstream B: Repo-Owned Browser Runner

Added `BrowserSmokeRunner` in `src/DataLinq.DevTools`.

The runner:

- locates Edge, Chrome, or Chromium, with `DATALINQ_BROWSER_PATH` as an override
- starts the browser headless through `Microsoft.Playwright`
- serves the publish output from a local HTTP listener
- supports `.br` and `.gz` precompressed assets with the correct `Content-Encoding`
- opens the smoke page with Playwright
- polls the DOM smoke status until `passed`, `failed`, or timeout
- writes `browser-smoke.log` under the target report folder
- records DOM text, observed stages, window console log, Playwright console events, page errors, URL, publish directory, browser path, and duration

The Playwright dependency is deliberately scoped to the repo-local `DataLinq.DevTools` project. It must not leak into runtime packages. The runner uses the locally installed browser executable instead of requiring a browser download for ordinary developer runs.

### Workstream C: Wire Browser Smoke Into `size-report`

Updated `CompatibilitySizeReporter` so WebAssembly targets no longer report browser smoke as `n/a`:

- `wasm-aot` browser failure is a hard smoke failure
- `wasm` browser failure is reported as `unsupported (UnsupportedNoAot)` so the report can record the current no-AOT disposition without failing the whole release gate only because an unsupported target failed
- `--skip-smoke` still skips browser and executable smokes

## Verification

Fast verification:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Dev.CLI\DataLinq.Dev.CLI.csproj -v:minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*" --output failures --build
```

Release evidence command:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm-aot --release-thresholds --clean-output --format summary
```

On native Windows, WebAssembly publish can fail inside the Codex sandbox with the known `MarshalingPInvokeScanner` task-host issue. If that happens, rerun the same command outside the sandbox before classifying it as a product regression.

Historical Phase 8 evidence:

- `artifacts/dev/compat-size-report/20260628-163740998/` publishes `wasm-aot`, serves the published app, opens Edge through Playwright, and captures the browser smoke failure.
- The smoke reaches `opening-generated-database`.
- Edge reports `MONO_WASM: function signature mismatch`.
- The browser smoke log records the DOM text, window console log, Playwright console events, page error, URL, browser path, and publish directory.

This satisfied the automation goal but not the browser AOT support goal at the time. Phase 23 then fixed the generated metadata startup failure. Current evidence at `artifacts/dev/compat-size-report/20260629-210510424/` publishes `wasm-aot` and passes browser smoke at `verifying-strict-parser-projection`.

## Exit Criteria

- `size-report --targets wasm-aot` runs browser smoke by default.
- Browser smoke evidence is written next to the target report.
- The smoke page exposes stable status and stage markers.
- Browser failures distinguish publish failure, browser/toolchain failure, boot failure, smoke failure, and timeout well enough to debug the next run.
