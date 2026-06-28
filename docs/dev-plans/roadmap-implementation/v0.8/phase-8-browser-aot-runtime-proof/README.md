> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 8: Browser AOT Runtime Proof

**Status:** Planned 0.8 release work.

## Purpose

Phase 8 makes browser AOT runtime proof boring. The current compatibility report proves WebAssembly publish output and payload size, but `size-report` still marks browser smoke as `n/a`. That is not enough for a release whose story includes browser AOT.

The goal is a repeatable local command that publishes the Blazor WebAssembly AOT smoke app, serves it over HTTP, opens it in a real browser, waits for the DataLinq smoke result, and records evidence.

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
- generated SQLite schema creation, insert, query, relation, projection, and documented parser route execute in browser
- the report can be cited from `docs/Platform Compatibility.md`
- no browser support claim depends only on publish success

