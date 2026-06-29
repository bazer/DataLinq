> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 23: Browser AOT Debugging

**Status:** Implemented.

Execution plan: [Implementation Plan](Implementation%20Plan.md).

## Purpose

Phase 23 owns the remaining browser AOT question for 0.8.

The phase fixed the current browser runtime blocker. The WebAssembly AOT target now publishes in the non-clean release-gate path, runs in the Playwright browser smoke, and reaches `passed` for the generated SQLite smoke boundary.

The old failure was not SQLitePCLRaw varargs, provider registration, raw connection open, or the DataLinq database constructor. It was the generic generated metadata startup path wrapping static abstract generated metadata hooks in delegates. Mono WebAssembly AOT failed that delegate/static-interface-member combination with `MONO_WASM: function signature mismatch` before metadata definition construction. The generic path now calls `TDatabase.GetDataLinqGeneratedMetadata()` and `TDatabase.SetDataLinqGeneratedMetadata(...)` directly.

The result is useful, not magic. Clean-output WebAssembly publish still fails before browser execution with the Blazor SDK `ResolveWasmOutputs` target issue, and fresh WebAssembly publishes still emit `WASM0001` diagnostics for SQLitePCLRaw varargs exports. Those warnings are kept visible; no global suppression was added.

## Scope

In scope:

- reproduced the current WebAssembly AOT browser failure from fresh commands
- separated Blazor SDK clean-output failures from DataLinq/SQLite runtime failures
- identified the `MONO_WASM: function signature mismatch` as generated metadata startup, not raw SQLite open or provider registration
- captured exact browser logs, failing stage, and warning classification
- kept SQLitePCLRaw `WASM0001` warnings visible and unsuppressed
- tested current no-AOT browser behavior and recorded the fresh passing smoke result
- fixed the generated metadata startup path without swapping SQLite providers or hiding generated database startup
- updated phase evidence and support-contract wording based on the result

Out of scope:

- OPFS or file-backed browser storage
- MySQL/MariaDB browser support
- broad WebAssembly performance tuning
- replacing the SQLite provider as a speculative rewrite
- global warning suppression without call-path proof
- claiming browser support from publish success alone

## Outcome

Browser AOT is fixed and proven for the narrow generated SQLite in-memory smoke boundary:

- non-clean `wasm-aot` report `artifacts/dev/compat-size-report/20260629-210510424/` publishes and browser-smokes successfully
- `wasm-aot/browser-smoke.log` reaches `passed` at `verifying-strict-parser-projection`
- the browser console stages now prove generated metadata draft/definition construction, raw SQLite open/version/PRAGMAs, the keep-alive plus second-connection pattern, generated database construction, schema creation, inserts, relations, projections, and parser route evidence
- non-clean `wasm` report `artifacts/dev/compat-size-report/20260629-205114951/` also passes the same browser smoke boundary
- clean-output `wasm-aot` report `artifacts/dev/compat-size-report/20260629-205036682/` still fails before browser execution with `MSB4057` for missing `ResolveWasmOutputs`
- clean-output `wasm` report `artifacts/dev/compat-size-report/20260629-205211590/` fails the same SDK target path before browser execution
- fresh WebAssembly publishes emit 13 `WASM0001` diagnostics, including `sqlite3_config` and `sqlite3_db_config`; the smoke proves the supported generated SQLite path does not hit those failing imports, but Phase 23 does not justify global warning suppression

## Verification

Primary command:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm-aot --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

Secondary commands:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets wasm --clean-output --release-thresholds --format markdown
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets phase8c --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown
```

Evidence to capture:

- compatibility report directory
- `wasm-aot/browser-smoke.log`
- no-AOT browser smoke status and log
- warning summary for `WASM0001`
- exact failing stage and exception text
- any minimal reproduction or narrowed smoke variant used during debugging

Captured evidence:

- AOT runtime pass: `artifacts/dev/compat-size-report/20260629-210510424/report.md`
- AOT browser smoke: `artifacts/dev/compat-size-report/20260629-210510424/wasm-aot/browser-smoke.log`
- AOT clean-output SDK failure: `artifacts/dev/compat-size-report/20260629-205036682/report.md`
- no-AOT runtime pass: `artifacts/dev/compat-size-report/20260629-205114951/report.md`
- no-AOT browser smoke: `artifacts/dev/compat-size-report/20260629-205114951/wasm/browser-smoke.log`
- no-AOT clean-output SDK failure: `artifacts/dev/compat-size-report/20260629-205211590/report.md`

## Exit Criteria

Phase 23 is done when:

- the browser AOT failure is fixed for the generated SQLite smoke boundary
- current no-AOT browser behavior is recorded from browser smoke, not inferred from publish output
- SQLitePCLRaw warning disposition is documented as visible/unsuppressed, with smoke evidence for the supported path and remaining call-graph work for broader claims
- support docs and Phase 24 release evidence know browser AOT is included for the narrow generated SQLite boundary
- no public browser support claim depends on historical manual proof or publish-only evidence
