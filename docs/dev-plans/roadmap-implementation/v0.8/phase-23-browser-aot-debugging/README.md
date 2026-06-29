> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 23: Browser AOT Debugging

**Status:** Planned.

Execution plan: [Implementation Plan](Implementation%20Plan.md).

## Purpose

Phase 23 owns the remaining browser AOT question for 0.8.

Current tooling proves something useful and uncomfortable: the WebAssembly AOT target can publish, the browser smoke can run, and the smoke currently fails while opening generated SQLite with `MONO_WASM: function signature mismatch`.

This phase is not about making a nicer support sentence. It is about finding the failure boundary and deciding whether 0.8 can honestly include browser AOT support.

## Scope

In scope:

- reproduce the current WebAssembly AOT browser failure from a clean command
- separate Blazor SDK clean-output failures from DataLinq/SQLite runtime failures
- identify whether the `MONO_WASM: function signature mismatch` comes from SQLitePCLRaw varargs imports, provider registration, connection opening, generated metadata startup, or another runtime boundary
- capture exact browser logs, failing stage, and warning classification
- map SQLitePCLRaw `WASM0001` warnings to managed call paths where possible
- test current no-AOT browser behavior and record whether it remains unsupported
- try narrow configuration/provider changes only when they directly address the identified failure
- update phase evidence and support-contract wording based on the result

Out of scope:

- OPFS or file-backed browser storage
- MySQL/MariaDB browser support
- broad WebAssembly performance tuning
- replacing the SQLite provider as a speculative rewrite
- global warning suppression without call-path proof
- claiming browser support from publish success alone

## Decision Outcomes

This phase has three acceptable outcomes:

1. **Browser AOT fixed and proven**
   - `wasm-aot` clean publish passes.
   - browser smoke reaches `passed`.
   - SQLitePCLRaw warning disposition is documented.
   - 0.8 can claim the narrow generated SQLite browser AOT boundary.

2. **Browser AOT remains blocked but understood**
   - failure stage and likely native/managed boundary are documented.
   - release docs explicitly exclude browser AOT support.
   - no warning suppression is added.

3. **Browser AOT is excluded by support decision**
   - current runtime stack is judged too unstable or provider-dependent for 0.8.
   - release docs state Native AOT and trimmed support only.
   - future browser work gets a separate post-0.8 plan.

The unacceptable outcome is a vague "WASM is flaky" note with a support claim still implied elsewhere.

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

## Exit Criteria

Phase 23 is done when:

- the browser AOT failure is either fixed or explicitly classified as a release blocker/exclusion
- current no-AOT browser behavior is recorded from browser smoke, not inferred from publish output
- SQLitePCLRaw warning disposition is documented with exact symbols and call-path reasoning where available
- support docs and Phase 24 release evidence know whether browser AOT is included or excluded
- no public browser support claim depends on historical manual proof or publish-only evidence
