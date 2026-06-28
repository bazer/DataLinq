> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 9: WebAssembly Warning and no-AOT Disposition

**Status:** Tooling implemented; current evidence keeps browser WebAssembly support blocked.

Execution record: [Implementation Plan](Implementation%20Plan.md).

## Purpose

Phase 9 closes the two browser questions that are currently too fuzzy: SQLitePCLRaw WebAssembly varargs warnings and no-AOT browser behavior.

The correct outcome might be "supported", "unsupported", or "suppressed with proof". The unacceptable outcome is warning silence caused by incremental publish artifacts or a no-AOT publish that nobody actually loads in a browser.

Current evidence is not supportable: the host-side `wasm-aot` browser report fails with `MONO_WASM: function signature mismatch` while opening generated SQLite, and the clean-output publish path exposes a Blazor SDK `ResolveWasmOutputs` target failure before the browser can run. Treat both as release blockers, not as warning noise.

## Scope

In scope:

- force a clean WebAssembly AOT publish in the warning investigation path
- capture `WASM0001` warnings for SQLitePCLRaw imports such as `sqlite3_config` and `sqlite3_db_config`
- map managed imports to reachable or unreachable DataLinq smoke paths
- decide whether to avoid, document, or narrowly suppress the warnings
- re-run no-AOT browser smoke after the Roslyn and Remotion cleanup
- record exact failure stage and runtime method if no-AOT still fails

Out of scope:

- broad provider replacement for SQLite
- pretending no-AOT is supported because publish output is small
- global warning suppression without call-path proof

## Exit Criteria

- SQLitePCLRaw warning disposition is documented with exact symbols and managed call paths
- current no-AOT browser behavior is proven in a browser, not inferred from publish output
- public compatibility docs clearly say whether no-AOT browser SQLite is supported or unsupported
- any warning suppression is local to the smoke/project boundary and backed by evidence
