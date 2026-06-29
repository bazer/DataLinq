> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 9: WebAssembly Warning and no-AOT Disposition

**Status:** Tooling implemented; Phase 23 current evidence proves the generated SQLite browser smoke, with clean-output and warning caveats still visible.

Execution record: [Implementation Plan](Implementation%20Plan.md).

## Purpose

Phase 9 closes the two browser questions that are currently too fuzzy: SQLitePCLRaw WebAssembly varargs warnings and no-AOT browser behavior.

The correct outcome might be "supported", "unsupported", or "suppressed with proof". The unacceptable outcome is warning silence caused by incremental publish artifacts or a no-AOT publish that nobody actually loads in a browser.

Historical Phase 9 evidence was not supportable: the host-side `wasm-aot` browser report failed with `MONO_WASM: function signature mismatch` while opening generated SQLite, and the clean-output publish path exposed a Blazor SDK `ResolveWasmOutputs` target failure before the browser could run. Phase 23 fixes the browser runtime failure for the generated SQLite smoke. The clean-output SDK failure and `WASM0001` warning disposition remain caveats, not warning noise.

## Scope

In scope:

- force a clean WebAssembly AOT publish in the warning investigation path
- capture `WASM0001` warnings for SQLitePCLRaw imports such as `sqlite3_config` and `sqlite3_db_config`
- map managed imports to reachable or unreachable DataLinq smoke paths
- decide whether to avoid, document, or narrowly suppress the warnings
- keep no-AOT browser smoke evidence current after the Roslyn and Remotion cleanup
- record exact failure stage and runtime method if no-AOT regresses

Out of scope:

- broad provider replacement for SQLite
- pretending no-AOT is supported because publish output is small
- global warning suppression without call-path proof

## Exit Criteria

- SQLitePCLRaw warning disposition is documented with exact symbols and managed call paths
- current no-AOT browser behavior is proven in a browser, not inferred from publish output
- public compatibility docs clearly say no-AOT browser SQLite is only proven at the current generated smoke boundary unless broader evidence is added
- any warning suppression is local to the smoke/project boundary and backed by evidence
