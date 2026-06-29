> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 8: Browser AOT Runtime Proof

**Status:** Implemented in tooling; current host-side WebAssembly AOT browser evidence fails at generated SQLite startup.

Execution record: [Implementation Plan](Implementation%20Plan.md).

## Purpose

Phase 8 makes browser AOT runtime proof boring. The compatibility report already proves WebAssembly publish output and payload size; this phase adds the missing browser execution proof so the release story does not depend on publish success alone.

The goal is a repeatable local command that publishes the Blazor WebAssembly AOT smoke app, serves it over HTTP, opens it in a real browser, waits for the DataLinq smoke result, and records evidence.

## Current Evidence

`artifacts/dev/compat-size-report/20260628-163740998/` proves the tooling path: the `wasm-aot` publish succeeds, the app is served over HTTP, Edge opens through Playwright, and `browser-smoke.log` records DOM text, console output, page errors, and stage markers.

That evidence is negative for support. The smoke reaches `opening-generated-database`, then fails with `MONO_WASM: function signature mismatch`. Do not use the historical Phase 8 manual browser proof as a release support claim until the current Playwright report passes.

## Scope

In scope:

- add a repo-owned browser smoke command or test lane
- serve the published output over HTTP, not `file://`
- run the generated SQLite path in a browser under WebAssembly AOT
- record DOM result, console errors, smoke timings, and publish artifact paths
- make failures distinguish publish, browser startup, SQLite initialization, schema creation, seeding, query, projection, and relation stages
- keep the current `size-report` payload summary linked to the browser run

Out of scope:

- OPFS/file-backed browser storage
- no-AOT browser support
- arbitrary browser query shapes
- MySQL/MariaDB browser support

## Exit Criteria

- one command refreshes WASM AOT publish plus browser smoke evidence
- browser smoke fails with actionable stage diagnostics
- generated SQLite schema creation, insert, query, relation, projection, and documented parser route execute in browser; current evidence does not satisfy this because it fails while opening SQLite
- the report can be cited from `docs/Platform Compatibility.md`
- no browser support claim depends only on publish success
