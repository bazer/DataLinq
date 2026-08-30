> [!WARNING]
> This is an implementation plan for a future release. It is not documentation of shipped DataLinq behavior.

# 0.10 Release Evidence And Closeout Implementation Plan

**Status:** Accepted.

**Target release:** 0.10.

**Last reviewed:** 2026-08-30.

**Depends on:** The required workstreams and gates in the [0.10 implementation roadmap](README.md) and [implementation order](Implementation%20Order%20and%20Integration%20Plan.md).

## Objective

Produce reproducible evidence that one exact 0.10 candidate satisfies the adopted async, hosting, validation, testing, generator, package, compatibility, and performance contracts without silently importing excluded work.

This plan is deliberately shorter than the 0.9 closeout record. It reuses the release tooling built for 0.9 rather than restating its implementation history.

Publication is not part of this plan. The final action is a maintainer go/no-go handoff for a pack-only candidate.

## Evidence Rules

1. Every required command must have a tracked exit code and complete final summary.
2. Evidence from different commits or candidate versions cannot be assembled into one release claim.
3. Diagnostic or partial runs do not become release evidence merely because their individual tests passed.
4. Existing warnings require explicit disposition; new warnings require an owner.
5. Provider/environment failures remain visible and are not relabeled as product success.
6. Public documentation changes from planned to shipped wording only after the frozen candidate passes.
7. Test counts are discovered from the current catalog and ratcheted during implementation; this planning document does not freeze stale numeric totals.
8. A completed required workstream does not authorize a stretch feature.

## Artifact Layout

Use one candidate root:

```text
artifacts/release/v0.10/<candidate>/
├── manifest.md
├── tests/
├── packages/
├── api/
├── compatibility/
├── benchmarks/
├── docs/
└── logs/
```

The manifest records:

- exact repository commit and clean/dirty state
- SDK/runtime/tool versions
- exact candidate version
- package hashes and repository metadata
- command lines, exit codes, timestamps, and artifact paths
- expected and observed suite/target/package inventories
- compatibility, benchmark, warning, and manual-review dispositions
- explicit GO or NO-GO

## RE10-0: Freeze The Before-State

Before changing shared execution paths:

- run repository doctor, restore, and forced build
- record the current test catalog and supported provider targets
- run quick and complete provider matrices
- pack and inspect a current-development 0.9 baseline if package shape evidence is needed
- capture public API and generated-source baselines
- run the benchmark lanes affected by query, relation, mutation, transaction, and provider initialization
- record current logging, metrics, cache, invalidation, and terminal-state telemetry
- inventory known #26 allocation exceptions separately from 0.10 changes

Representative commands:

```powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- doctor --profile repo
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- restore
./scripts/dotnet-sandbox.ps1 build src/DataLinq.sln -c Debug -v minimal --no-incremental
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- list
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --plan quick --output failures
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Benchmark.CLI -- list
```

The final command catalog and artifact roots are frozen in the W0 baseline record before implementation.

Acceptance:

- baseline commit and environment are explicit
- no known failing baseline is mistaken for a 0.10 regression
- all future release gates have a before-state or a documented new-feature-only contract

## RE10-1: Workstream Contract Gates

### Async And Cancellation

Required focused evidence:

- the accepted [public API decisions](Async%20Public%20API%20Decisions.md), with unresolved signature/failure questions settled before W3
- no database I/O during transaction construction or unused disposal; first-use initialization follows the selected sync/async operation and its token
- token-free and token-supplied consumer calls, sequential sync/async mixing, and both synchronous and asynchronous disposal
- generated `<PropertyName>Async` relation methods bypass synchronous getters and preserve cache/nullability/source behavior

- sync/async result parity for supported entity, scalar, projection, paging, aggregate, and terminal query families
- explicit relation-load parity for cache hit, cache miss, missing row, and provider failure
- mutation success/failure and transaction commit/rollback parity
- cancellation before dispatch, during command execution, during reader/materialization, and during multi-step orchestration
- timeout versus caller cancellation versus provider error classification
- uncertain commit and cleanup/disposal behavior
- no `Task.Run`/sync-over-async fallback on supported native provider paths
- logging, metrics, cache, invalidation, and terminal-state parity

### DI, Hosting, And Unit Of Work

Required focused evidence:

- service registration and options validation
- singleton/scoped/transient ownership and exact-once disposal
- ASP.NET Core and Generic Host consumer fixtures
- concurrent scope isolation
- explicit unit-of-work begin/commit/rollback/failure/cancellation/disposal
- nested application-service participation without hidden ambient ownership
- logging through the host pipeline

### Startup Validation

Required focused evidence:

- fail-fast, warning-only, and disabled policies
- deterministic multiple-target order and aggregation
- connectivity, missing-secret, metadata-read, drift, unsupported-difference, timeout, and cancellation outcomes
- no database access when disabled
- no migration, repair, or secret leakage

### Testing Support

Required focused evidence:

- immutable scalar values, nullability, defaults, primary keys, equality, `GetValues()`, and `Mutate()`
- empty/single/multiple/duplicate relation behavior
- one-to-many, many-to-one, nullable foreign-key, and composite-key graphs
- relation direction and key mismatch diagnostics
- Memory fixture seeding/reset and capability rejection
- fake unit-of-work recording, commit/rollback/disposal, and failure injection
- DI replacement behavior for Memory, fake unit of work, and SQLite-in-memory provider tests

### Source Type Aliases

Required focused evidence:

- `using Text = System.String` and neighboring reference aliases
- value-type, nullable, namespace, generic/custom, enum, and scalar-converter aliases
- generated implementation and metadata type identity
- null materialization under explicit nullable attributes/context
- same-driver incremental recomputation after alias-target-only changes
- focused unsupported diagnostic for syntax-only paths

Acceptance: each focused lane passes before its implementation is considered complete, and failures remain attributable to one workstream.

## RE10-2: Full Test Matrix

The final matrix includes the current suites and all current supported targets discovered by `DataLinq.Testing.CLI`. At minimum it must preserve:

- generators
- unit
- Memory
- SQLite file and in-memory compliance
- MySQL provider/compliance targets
- MariaDB provider/compliance targets
- any new host/testing project suites registered during 0.10

For server-backed sandboxed runs on native Windows, use `DATALINQ_TEST_DB_HOST=127.0.0.1` with the repository wrapper and the actual running target catalog.

Representative final shape:

```powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- up --alias all
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --alias all --batch-size 1 --output failures --summary-json artifacts/release/v0.10/<candidate>/tests/all.json
Remove-Item Env:DATALINQ_TEST_DB_HOST
```

Acceptance:

- every expected suite/target row is present
- zero failed or skipped required tests
- summary and referenced artifacts are complete and valid
- no targeted run leaves the canonical infrastructure state narrowed or stale

## RE10-3: Public API, Generated Code, And Consumer Compatibility

Required evidence:

- ApiCompat against published 0.9 packages
- review of every public API addition and break candidate
- generated-source comparison for representative models
- compilation against net8, net9, and net10 where the package supports them
- external consumer smoke for synchronous compatibility and new async/DI/testing usage
- source alias consumer fixture independent of generator unit tests
- package dependency graph proves host/testing dependencies do not leak into unrelated runtime packages

Acceptance:

- no undispositioned API break
- current synchronous consumer continues to build and run
- new hosted async consumer restores, builds, validates, and executes from packed packages
- generated output is deterministic for identical inputs

## RE10-4: Packaging And Constrained Runtimes

Pack without publishing:

```powershell
./publish-nuget.ps1 -PackOnly -Version 0.10.0-rc.N -PackageOutputPath artifacts/nuget-release/v0.10-rc.N
```

Required evidence:

- default package inventory updated for every new public package
- matching symbol packages, repository metadata, versions, dependencies, frameworks, and banned payloads
- package-report strict release evidence
- package-backed consumer smoke
- Native AOT, trimmed, WebAssembly, and browser matrices rerun wherever the changed package graph or shared runtime path can affect an existing claim
- new host-integration and testing packages checked for unnecessary ASP.NET Core, Roslyn, provider, or native dependencies

Representative package inspection:

```powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- package-report --package-dir artifacts/nuget-release/v0.10-rc.N --version 0.10.0-rc.N --output artifacts/release/v0.10/v0.10-rc.N/packages/inspection --format markdown
```

Acceptance:

- exact expected packages and symbols are present once
- all package/report hashes and repository commits agree
- compatibility runs are package-backed and candidate-stable where required for release evidence
- no package is published

## RE10-5: Performance And Telemetry

Required comparison:

- same runner, runtime family, provider, profile, operation counts, harness schema, and target selection
- baseline captured before shared runtime work
- query, relation, mutation, transaction, provider-init, startup, and any new async-specific stages affected by 0.10
- allocation, latency, and normalized telemetry reviewed together
- benchmark artifacts complete and valid; low-duration or multimodal warnings remain visible

Policy:

- [issue #26](https://github.com/bazer/DataLinq/issues/26) remains open until its own acceptance criteria are met or explicitly revised
- 0.10 does not claim final-0.8 allocation parity merely because the release is otherwise acceptable
- 0.10 cannot introduce an unexplained regression and call it pre-existing debt
- performance fixes require attribution and their own correctness coverage; speculative plan caches, pooling, or provider-state sharing are outside scope

Acceptance:

- every material change has an explanation tied to code and telemetry
- no statistically meaningful undispositioned latency regression
- no changed query/mutation/cache/transaction telemetry shape without an approved semantic reason
- accepted trade-offs are recorded in release notes and the final manifest

## RE10-6: Documentation

Planning documents remain explicitly non-normative during implementation.

Before final release review, update the shipped documentation for only the verified subset:

- installation and package guidance
- async query/relation/mutation/transaction usage
- DI and Generic Host registration
- unit-of-work ownership and disposal
- startup validation policies
- testing-layer guidance and examples
- generator/source alias behavior where user-visible
- API/support matrices
- public roadmap and 0.10 release notes

Run:

```powershell
docfx build docfx.json
```

Also validate links under `docs/dev-plans`, inspect generated `_site` navigation for changed public pages, and preserve any known baseline warnings separately from new warnings.

Acceptance:

- DocFX exits zero
- no broken internal links
- generated site pages render the new public navigation/content correctly
- no plan or example implies hidden lazy loading, automatic migrations, Memory/provider parity, or excluded 0.10 work

## RE10-7: Frozen Candidate And Go/No-Go

Final sequence:

1. freeze one clean commit and candidate version
2. stop feature implementation
3. restore/build from the frozen candidate
4. run the full test matrix
5. pack without publishing
6. run package inspection and consumer smoke
7. run ApiCompat and generated-code checks
8. run applicable constrained-runtime/package-backed compatibility evidence
9. run final benchmark comparison
10. build and inspect documentation
11. write `manifest.md` with every artifact, warning, exception, and scope check
12. record explicit GO or NO-GO

Automatic NO-GO conditions:

- dirty or drifting candidate
- nonzero required build/test/tool command without an accepted environment rerun
- incomplete expected matrix rows or missing artifacts
- undispositioned API break, package finding, product compatibility failure, or material benchmark regression
- documentation claims beyond evidence
- any explicitly excluded work included without an approved roadmap revision

The plan stops at maintainer review. Tagging, publishing packages, creating a GitHub release, and announcing the release require separate maintainer action.

## Definition Of Done

- all required 0.10 workstream gates pass
- complete final provider matrix passes
- package, API, generated-code, consumer, and applicable constrained-runtime evidence is valid
- performance and telemetry changes are dispositioned honestly
- documentation matches the frozen candidate
- final manifest identifies one exact candidate and contains an explicit GO
- no release package is published by this plan

## Links

- [DataLinq 0.10 Implementation Roadmap](README.md)
- [0.10 Implementation Order and Integration Plan](Implementation%20Order%20and%20Integration%20Plan.md)
- [Development Roadmap](../../Roadmap.md)
- [DataLinq Testing CLI](../../../contributing/DataLinq.Testing.CLI.md)
- [DataLinq Dev CLI](../../../contributing/DataLinq.Dev.CLI.md)
- [AI Assistant Guidance](../../../contributing/AI%20Assistant%20Guidance.md)
