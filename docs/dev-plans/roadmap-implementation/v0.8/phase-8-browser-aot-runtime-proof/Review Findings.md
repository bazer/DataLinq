# 0.8 Phase 8 Review Findings: Browser AOT Runtime Proof

**Review date:** 2026-06-28.

**Reviewed scope:** browser smoke tooling, Blazor smoke UI, current `wasm-aot` report artifacts, and phase 8 docs in the `v0.8` branch through `57da59e2`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Open findings in the browser proof tooling. The docs correctly avoid claiming browser AOT support while current evidence is red.

## Findings

### P2: Browser smoke can report a timeout instead of the real runtime failure

The browser runner collects console messages and page errors, but it only fails fast when the DOM snapshot status is `"failed"` (`src/DataLinq.DevTools/BrowserSmokeRunner.cs:140`). If the DOM remains `"running"`, the runner waits until the three-minute timeout (`src/DataLinq.DevTools/BrowserSmokeRunner.cs:160`) even when page errors have already been captured.

The current host artifact shows the bad behavior. `artifacts/dev/compat-size-report/20260628-163740998/wasm-aot/browser-smoke.log` records `RuntimeError: function signature mismatch` and `MONO_WASM: function signature mismatch`, but the report status remains `running` until timeout. That turns a crisp SQLite/WebAssembly runtime failure into a slower and less accurate timeout.

There is a second semantic-failure variant in the UI itself: `src/DataLinq.BlazorWasm/Pages/Home.razor:67` reports `"running"` whenever a `PlatformSmokeResult` exists but `result.Passed` is false. The executable smoke exits non-zero for that state (`src/DataLinq.AotSmoke/Program.cs:6`), but the browser UI would keep polling until timeout.

Expected fix: fail the browser smoke as soon as Playwright page errors or captured console runtime errors appear, and report `result.Passed == false` as `"failed"` in the Blazor smoke UI with the smoke result text available in the log.

### P2: The static browser server advertises `.gz` support but serves `.gzip`

The Phase 8 implementation plan says the runner supports `.br` and `.gz` precompressed assets. Brotli works, but gzip fallback is broken.

`SelectContentEncoding(...)` checks for `filePath + ".gz"` and returns `"gzip"` (`src/DataLinq.DevTools/BrowserSmokeRunner.cs:530`). `ServeAsync(...)` then appends `".{contentEncoding}"` (`src/DataLinq.DevTools/BrowserSmokeRunner.cs:456`), so it tries to serve `asset.gzip`, not `asset.gz`.

Most modern browser runs prefer Brotli, so this can stay hidden. It still makes the server contract false and weakens fallback evidence for clients or environments that request gzip but not Brotli.

Expected fix: return both the content-encoding header value and the file extension from content negotiation, or map `"gzip"` to `".gz"` when choosing the on-disk file.

## Review Notes

- The major Phase 8 direction is correct: browser runtime support must be proven through an HTTP-served browser run, not inferred from `dotnet publish`.
- Current docs are appropriately pessimistic. They treat `artifacts/dev/compat-size-report/20260628-163740998/` as negative evidence and do not promote browser AOT support.
- The browser smoke log is already rich enough to debug the current failure. The problem is status classification, not evidence capture.

## Verification

Focused verification and artifact review:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Dev.CLI\DataLinq.Dev.CLI.csproj -v:minimal
rg -n "StatusText|Passed|timed out|pageErrors|SelectContentEncoding|\\.gz|gzip" src\DataLinq.BlazorWasm src\DataLinq.DevTools
Get-Content artifacts\dev\compat-size-report\20260628-163740998\wasm-aot\browser-smoke.log
```

The build passed in delegated review. A delegated unit-suite rerun was blocked by a locked `src\DataLinq.CLI\obj\Debug\net10.0\DataLinq.CLI.dll`; rerun the unit filter after closing the locking `dotnet.exe`:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*" --output failures --build
```
