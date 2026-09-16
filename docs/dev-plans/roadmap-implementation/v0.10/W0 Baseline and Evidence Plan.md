> [!WARNING]
> This is planning material for DataLinq 0.10. It is not a completed baseline run or evidence that async APIs exist.

# 0.10 W0 Baseline And Evidence Plan

**Status:** Accepted execution plan. W0-P1 through W0-P6 accepted on 2026-09-16, with .NET 10 required for benchmark baselines and candidate runs now and going forward. Execution/evidence remains pending.

**Last reviewed:** 2026-09-16.

**Source audit:** `58142e7d546a494426d7266f8e256ca5f7cafdc2`.

**Authority:** [Implementation order](Implementation%20Order%20and%20Integration%20Plan.md#w0-baseline-and-io-inventory) and [RE10-0](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md#re10-0-freeze-the-before-state) require a baseline before shared runtime changes. [AAPI-106 through AAPI-111](Async%20Public%20API%20Decisions.md#aapi-106-runtime-validation-types-and-immutable-result-construction) settle validation, package and compatibility policies. This document records the accepted bounded execution details; it does not supersede those decisions.

## Observed Starting Point

- G01–G03 and E01–E06 design policies are accepted through AAPI-111. Actual implementations, emitted signatures and verification are pending.
- `ApiCompatibilityReporter` still declares `v0.9.api-compatibility-report.v2`, defaults to `v0.8.0-packages.json`, omits Memory from baseline library comparisons and checks Memory as new in 0.9. Passing a different baseline-version argument is insufficient.
- Local tags resolve to `0.9.0` at `a687616f6689b46843bbcdb9ce0fb291213322c7`, `0.9.1` at `57aa3dd3cc99e1e214dfae6cae99527e3ae0386e`, and `0.9.2` at `1894d53d25511a3581e8923deda1b74d0f76ee25`. Tag existence alone does not verify published package bytes.
- Development HEAD is not the 0.9.2 source tree: the source diff includes Tools file-writing fixes and test/evidence changes. A current-development benchmark/test baseline must carry its own exact identity.
- Benchmark history already has strict schema-v3 scope/provenance checks. Its six canonical selectors require heavy profiles; smoke/default/filtered runs are diagnostic only.
- The current Testing CLI has quick/full plans and a structured target catalog. Baseline assertions must use captured expected/observed cases and targets, not just a successful process exit or a moving `latest` alias.

These are source-audit findings. No package acquisition, baseline build/test/benchmark run or new tooling implementation has been completed by this document.

## W0-P1: Keep Published Compatibility And Development Baselines Distinct

**Accepted:** preserve AAPI-111's locked published 0.9.0 baseline, add latest-patch coverage, and freeze a separate pre-async development commit.

| Identity | Accepted role | Required record |
| --- | --- | --- |
| Published 0.9.0 | Accepted release-line compatibility baseline, including Memory | Exact nupkg bytes, hashes, repository metadata and tag/commit provenance |
| Latest published 0.9 patch at W0 freeze | Additional upgrade coverage; verify whether tagged 0.9.2 is that release | Independently acquired package lock and generated/behavioral patch-change review; never silently substitute it for 0.9.0 |
| Pre-async development target | Before-state tests, I/O inventory, generated/API snapshots and performance | Clean full commit, package graph, source scope and immutable artifacts |
| Evidence tooling | Code that builds, runs and validates evidence | Clean full tooling commit, embedded runner identities and any instrumented fixture/shim provenance |

Resolve tags to full commits once; do not use moving branch names as evidence identities. A tooling-only change may advance the tooling commit without changing the frozen runtime target, but both identities must remain visible and obey each reporter's existing provenance rules.

Keep published bytes distinct from locally packed development bytes. A local rebuild is useful for development package shape but is not a replacement for the published compatibility input. Review patches between 0.9.0 and the additional baseline so an API/behavior introduced after 0.9.0 is not omitted from upgrade coverage.

## W0-P2: Repair Evidence Tooling In A Separate Slice Before Capture

**Accepted:** implement the narrow 0.10 package/baseline policy and .NET 10 benchmark migration first, verify them and commit before any strict baseline run.

Extend the existing API reporter with explicit release-specific package sets/locks and report identity. Include core, SQLite, MySql, Memory, Tools and CLI assets as appropriate across .NET 8/9/10. Preserve the historical 0.8-to-0.9 path; do not mutate old locks or broaden suppressions to make the new profile pass.

Use active TUnit coverage for meaningful failure cases: wrong/missing package, wrong hash/version/repository identity, missing target asset, Memory API break, changed cross-framework surface, dirty/drifting checkout, stale runner and inherited-divergence classification. Preserve evidence-owned package copies and before/after identity checks.

Keep changes out of runtime execution, async public APIs, hosting/test-helper package construction and the future async LINQ reference. This is tooling preparation, not W1. If another reporter is hard-coded to 0.9, inventory and assign that gap rather than claiming a command already accepts a 0.10 profile.

The API reporter currently requires candidate/checkout identity agreement. Do not feed a candidate built from an old target to a newer checkout while bypassing that check. Use a clean compatible checkout/tooling slice for that report; only use distinct historical targets where the tool explicitly supports them, as the benchmark tool does.

## W0-P3: Capture A Complete Health Baseline With Explicit Coverage

**Accepted:** run doctor, restore and a forced build, then quick feedback followed by the full supported test matrix. Capture catalog/target definitions before execution.

The current provider catalog contains SQLite file/memory plus MySQL 8.4/9.7 and MariaDB 10.11/11.4/11.8/12.3. Freeze resolved targets and actual server/image versions or digests for the run; these names describe the checked-in matrix, not an external support claim. Generators, unit and Memory suites are targetless and must not be multiplied across SQL targets.

Retain commands, timestamps, exits, raw logs, structured summaries, expected/observed/skipped counts, package graph, SDK/runtime/tool identities and relevant environment settings. Do not store credentials or raw secret-bearing connection strings in the tracked manifest.

Representative existing commands from the repository root:

~~~powershell
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- doctor --profile repo
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -- restore
./scripts/dotnet-sandbox.ps1 build src/DataLinq.sln -c Debug -v minimal --no-incremental
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- list --plan full
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --plan quick --output failures
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
./scripts/dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --plan full --batch-size 1 --output failures
~~~

These are plan examples, not executed evidence. The actual run should use a scoped process environment and fresh explicit summary paths. Prepare server readiness through the Testing CLI without silently resetting existing containers. Follow repository sandbox escalation rules; verify known Windows WebAssembly build failures outside the sandbox before classifying them as product failures.

Capture generated-source/API snapshots and packed consumer behavior for existing surfaces now. Record constrained-runtime/package warning debt honestly; new 0.10 package-graph AOT/trim/browser proof cannot exist before that graph exists and remains a later gate.

## W0-P4: Use Existing Strict Benchmark Lanes And Preserve Their Meaning

**Accepted, with the user's .NET 10 amendment:** capture the six canonical lanes with `--profile heavy --release-evidence` and separate history paths, then repeat the same scopes against the candidate on the same runner. Both sides run on .NET 10.

| Existing selector | Current expected rows | Purpose |
| --- | ---: | --- |
| `--phase2-watch` | 6 | Provider initialization, startup primary key and warm key |
| `--phase3-query-hotpath` | 6 | End-to-end query hot paths |
| `--v09-query-backend` | 12 | Parse, capture/bind, preparation and SQL adapter |
| `--v09-memory-read` | 9 | Provider-free Memory construction, lookup and queries |
| `--allocation-regression` | 9 | Cold/warm key/relation, mutation and startup costs |
| `--allocation-stages` | 48 | Row decoding/materialization and mutation capture/preflight |

These are 90 rows across six lanes, not 90 independent feature guarantees. Reconfirm selector/target inventories from the actual clean harness at freeze. Do not interpret smoke runs, filtered/provider-subset runs, reused binaries or historical schema-v1/v2 artifacts as strict evidence.

Migrate the benchmark project, CLI/build/run paths and CI invocation from `net8.0` to `net10.0` before capture. Use .NET 10 for both the new baseline and all subsequent candidate runs. Record the actual SDK/runtime and toolchain; this change does not retarget the public libraries or reduce .NET 8/9/10 consumer coverage.

Preserve historical .NET 8 evidence with its original identity. It cannot serve as a comparable .NET 10 before-state or be relabeled by editing metadata. Historical source targets used for new measurements must actually build/run on .NET 10 through recorded tooling, without silently falling back to .NET 8 or modifying the frozen runtime sources. Recapture both sides when a runtime/toolchain change invalidates comparison.

Freeze scenario definitions, normalized operation counts and corpus/cache state. Retain allocations, latency and workload telemetry together; faster results caused by fewer queries, missing invalidations or changed transaction behavior are not a performance win. Run performance lanes without concurrent full-provider testing or another benchmark on the same machine.

Preserve D10-6/RE10-5: issue #26's final-0.8 parity target remains separate debt, and warning percentages are triage thresholds, not permission for a regression. Keep noisy timing visible and repeat uncertain comparisons; noise cannot waive allocation changes or semantic telemetry differences.

Add targeted diagnostic measurements only for concrete uncovered costs, such as transaction construction/unused disposal, overlap checks or cache invalidation. If a gap needs a new strict lane, define its expected rows and provenance before changing the runtime and capture its synchronous before-state. Do not force a nonexistent 0.9 async operation into a fake baseline.

## W0-P5: Map I/O And Behavior Now, Prove Async Feasibility In W1/W2

**Accepted:** expand the signature inventory into a source-linked execution map rather than another public API redesign.

For every query, relation/navigation, key lookup, prepared/raw reader, mutation, transaction, metadata/probe, provisioning/setup and disposal family, record:

- public entry/receiver and representative source call chain
- exact phase where connection opening, transaction initialization, command execution, row reading and cleanup occur
- current capture, cache-hit/miss, materialization and telemetry behavior
- resource owner, borrowed boundaries and source/transaction lifetime
- proposed native provider call and known blocking/capability boundary
- responsible workstream, existing evidence and missing future evidence

Include preserved synchronous SQLite setup and MariaDB constructor probing. Inventory metadata empty/missing handling and timeout plumbing required by AAPI-108/AAPI-109. Mark unsupported backend families explicitly; do not add Memory transactions/mutations or implicit sync fallback to fill a table.

W0 captures current sync behavior and native-call availability. W1/W2 supply deterministic suspension/fault injection, combined-token handling, overlap rejection, recovery, completion certainty, disposal and invalidation-safe publication evidence. Real-provider interruption tests supplement, not replace, controllable orchestration tests. W3 supplies actual emitted declarations and positive/negative packed consumers.

New async-only features receive an explicitly new-feature evidence entry until implemented. An isolated language probe remains a language probe; no artificial `Task.Run` facade, invented async result or assumed provider capability can make that cell green.

## W0-P6: Define A Reviewable Exit Bundle And Stop At Real Gaps

**Accepted:** close W0 only with an immutable evidence bundle and a concise tracked index, not a collection of successful-looking logs.

Use `artifacts/release/v0.10/<w0-run-id>/` for the baseline manifest and captured logs/summaries, referencing hashed benchmark artifacts under their existing `artifacts/benchmarks` roots. Record file lengths/hashes and the exact baseline/tooling identities. Keep raw generated output and package bytes in evidence storage rather than committing large artifacts.

The tracked index should link each inventory family and required gate to one of:

- baseline captured and verified
- known pre-existing finding, with evidence and explicit disposition
- new-feature-only proof assigned to W1/W2/W3 or release integration
- blocked or missing evidence, with a named owner and precise next action

A failed/incomplete command, missing suite/target, stale artifact, unverified package identity or unexplained before-state failure cannot become a pass through omission. Fix tooling/environment faults and rerun the affected evidence. Any exception to a required gate must be explicit; marking a failure "pre-existing" does not automatically waive it.

W0 is ready to hand off when every current I/O family has an owner, baseline identities and artifacts are trustworthy, the agreed current-behavior matrix is complete, benchmark scopes are frozen and all future-only checks have an assigned stage. Then begin W1 internal contracts and controllable execution tests. Do not publish packages, freeze the final public API or claim release readiness at this boundary.

## References

- [Signature inventory and compatibility matrix](Async%20Signature%20Inventory%20and%20Compatibility%20Matrix.md)
- [Dev CLI API/package reporting](../../../contributing/DataLinq.Dev.CLI.md#api-report)
- [Testing CLI plans and targets](../../../contributing/DataLinq.Testing.CLI.md#run-plans)
- [Benchmark evidence validity](../../../contributing/DataLinq.Benchmark.CLI.md#history-and-comparison-evidence)
- [API compatibility reporter](../../../../src/DataLinq.DevTools/ApiCompatibilityReporter.cs)
- [Provider target matrix](../../../../test-infra/podman/matrix.json)
