> [!WARNING]
> This is planning material for DataLinq 0.10. It is not a completed baseline run or evidence that async APIs exist.

# 0.10 W0 Baseline And Evidence Plan

**Status:** Accepted execution plan. W0-P1 through W0-P6 accepted on 2026-09-16, with .NET 10 required for benchmark baselines and candidate runs now and going forward. Tooling and baseline capture are complete; the [sealed evidence index](W0%20Baseline%20Evidence.md) and [I/O map](W0%20IO%20Execution%20Map.md) record the executed checkpoint. W0 completion and W1 handoff remain blocked by W0-F1, the unresolved SQLite finding.

**Last reviewed:** 2026-09-17.

**Baseline amendment:** On 2026-09-16 the user selected published **0.9.2** as the 0.10 compatibility baseline, replacing the earlier primary-0.9.0/additional-patch policy. Existing provider registration changes stay; consumers are aware of them. New performance baselines still use a separately frozen development commit and .NET 10.

**Source audit:** `58142e7d546a494426d7266f8e256ca5f7cafdc2`.

**Authority:** [Implementation order](Implementation%20Order%20and%20Integration%20Plan.md#w0-baseline-and-io-inventory) and [RE10-0](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md#re10-0-freeze-the-before-state) require a baseline before shared runtime changes. [AAPI-106 through AAPI-111](Async%20Public%20API%20Decisions.md#aapi-106-runtime-validation-types-and-immutable-result-construction) settle validation, package and compatibility policies. This document records the accepted bounded execution details; it does not supersede those decisions.

## Observed Starting Point

This section records the source audit above, before the tooling slice below.

- G01–G03 and E01–E06 design policies are accepted through AAPI-111. Actual implementations, emitted signatures and verification are pending.
- `ApiCompatibilityReporter` still declares `v0.9.api-compatibility-report.v2`, defaults to `v0.8.0-packages.json`, omits Memory from baseline library comparisons and checks Memory as new in 0.9. Passing a different baseline-version argument is insufficient.
- Local tags resolve to `0.9.0` at `a687616f6689b46843bbcdb9ce0fb291213322c7`, `0.9.1` at `57aa3dd3cc99e1e214dfae6cae99527e3ae0386e`, and `0.9.2` at `1894d53d25511a3581e8923deda1b74d0f76ee25`. Tag existence alone does not verify published package bytes.
- Development HEAD is not the 0.9.2 source tree: the source diff includes Tools file-writing fixes and test/evidence changes. A current-development benchmark/test baseline must carry its own exact identity.
- Benchmark history already has strict schema-v3 scope/provenance checks. Its six canonical selectors require heavy profiles; smoke/default/filtered runs are diagnostic only.
- The current Testing CLI has quick/full plans and a structured target catalog. Baseline assertions must use captured expected/observed cases and targets, not just a successful process exit or a moving `latest` alias.

These were source-audit findings, not executed baseline evidence.

## Tooling Preparation Checkpoint: 2026-09-16

The first W0-P2 slice implements release-specific API reporting and the .NET 10 benchmark migration without changing runtime execution or public library target frameworks:

- Published 0.9.0 and 0.9.2 bytes were independently acquired, hashed and checked against nuspec/tag repository identities. 0.9.2 was the latest stable 0.9 version in the checked NuGet index. Both six-package locks and the exact inherited `loadLock` dispositions are tracked under `test-infra/api-compatibility/`; the 0.8 lock is unchanged. See the [acquisition record](../../../../test-infra/api-compatibility/README.md).
- At this initial checkpoint, `api-report` defaulted to 0.9.0; the subsequent accepted baseline amendment changes the default to 0.9.2. It uses the 0.10 report/lock policy for stable locked 0.9 baselines and compares Memory as an existing package. Explicit 0.8.0 retains the historical policy. Candidate/checkout, clean runner, immutable-input, tag and canonical-lock checks remain enforced.
- The benchmark CLI/harness, build paths and CI invocation now target .NET 10. History writing and revalidation require both CLI and row runtimes to identify .NET 10. Historical .NET 8 artifacts retain their identity and cannot qualify as new .NET 10 evidence.
- Recorded historical build hooks retarget the harness and fix BenchmarkDotNet's inherited framework matrix without changing the frozen target sources. Existing historical calibration definitions are preserved; injection is only used when the target lacks them.
- The full unit suite passed **1,833/1,833**, including new Memory-break routing, release/lock validation and cross-runtime benchmark evidence tests. Log root: `artifacts/test-results/20260916T171744352Z-ca8df3f8913e4869992e9fffc659c4c5/`. An earlier exploratory filter was invalid; an initial fixture dependency fault was fixed, and a transient allocation assertion passed on the full rerun. Neither failed run is baseline evidence.
- Current-development and clean-source 0.9.2 historical Memory smoke runs each completed all nine cases with telemetry and artifact-complete histories on .NET 10.0.12 (row runtime .NET 10.0). Histories: `artifacts/benchmarks/history/w0-net10-current-smoke.json` and `w0-net10-historical-smoke-fixed.json`. The first historical attempt exposed the generated-project framework inheritance fault; its failed history remains separate. The successful smoke runs are diagnostic, with `ValidForEvidence: false`, not timing/allocation baselines.
- A real 0.9.2-to-itself API diagnostic captured 36 surfaces and ten comparison groups with zero compatibility/framework breaks and the two inherited review items. Its four hard failures correctly concern the uncommitted lock, dirty-built runner/checkout and historical candidate/checkout mismatch; no provenance check was bypassed. Report: `artifacts/dev/api-report/w0-policy-0.9.2-self-diagnostic/report.json`.
- DocFX built the contributor documentation successfully with zero warnings/errors; `git diff --check` passed.

Other reporters retain their existing contracts: package inspection and package-consumer smoke still identify their 0.9 schemas, and constrained-runtime reporting still uses its 0.9 catalog. W0 may capture the current six-package graph with those exact contracts; release integration must extend and verify them before claiming evidence for the new 0.10 package graph. No hosting/test-helper package or new async consumer is covered by this slice.

### Pre-existing PluginHook Compatibility Finding

A real ApiCompat 10.0.400 diagnostic comparison of the acquired 0.9.0 and 0.9.2 packages found **nine CP0002 compatibility breaks**: these three public fields were replaced by read-only properties on each of net8/net9/net10:

- `DataLinq.Metadata.PluginHook.DatabaseProviders`
- `DataLinq.Metadata.PluginHook.SqlFromMetadataFactories`
- `DataLinq.Metadata.PluginHook.MetadataFromSqlFactories`

The tag-to-tag source diff corroborates the field-to-property and `Dictionary`-to-`IReadOnlyDictionary` changes made with atomic provider registration. Identical names do not preserve compiled field references or source code that mutates/reassigns those dictionaries. These are baseline-to-candidate breaks, not inherited cross-framework `loadLock` differences.

Diagnostic report: `artifacts/dev/api-report/w0-policy-0.9.0-to-0.9.2-diagnostic/report.json`, with six baseline packages, six candidate packages, 36 API surfaces and ten comparison groups. The run correctly failed: nine compatibility findings plus five provenance failures from the uncommitted/dirty/changing tooling checkout and the historical candidate/checkout mismatch. It is not clean release evidence. Memory, provider, Tools and CLI comparisons reported no baseline breaks.

**Disposition accepted on 2026-09-16:** the user explicitly replaces the 0.9.0 compatibility baseline with 0.9.2, keeps the atomic registration changes and confirms consumers know about them. AAPI-111 is amended accordingly. This historical 0.9.0-to-0.9.2 finding no longer blocks W0 or requires a compatibility exception/suppression. Retain the exact diagnostic record and both acquired locks; do not relabel the failed report as a pass. Old-binary/source consumer checks for 0.10 now start from 0.9.2, and new unexplained breaks against that baseline remain failures.

That tooling checkpoint was followed by the [clean baseline capture](W0%20Baseline%20Evidence.md): health/package/generated-source evidence, the I/O map and all six heavy benchmark lanes are retained under exact identities. W0 remains open because W0-F1 requires a focused provider-lifetime follow-up; W1 runtime changes have not started.

## W0-P1: Keep Published Compatibility And Development Baselines Distinct

**Accepted, amended on 2026-09-16:** use AAPI-111's locked published 0.9.2 compatibility baseline and freeze a separate pre-async development commit. The earlier primary-0.9.0/additional-patch requirement is superseded.

| Identity | Accepted role | Required record |
| --- | --- | --- |
| Published 0.9.2 | Accepted fixed compatibility baseline, including Memory and the existing registration changes | Exact nupkg bytes, hashes, repository metadata and tag/commit provenance |
| Published 0.9.0 | Retained historical diagnostic input; no longer a required 0.10 compatibility gate | Existing acquired lock and truthful 0.9.0-to-0.9.2 diagnostic record |
| Pre-async development target | Before-state tests, I/O inventory, generated/API snapshots and performance | Clean full commit, package graph, source scope and immutable artifacts |
| Evidence tooling | Code that builds, runs and validates evidence | Clean full tooling commit, embedded runner identities and any instrumented fixture/shim provenance |

Resolve tags to full commits once; do not use moving branch names as evidence identities. A tooling-only change may advance the tooling commit without changing the frozen runtime target, but both identities must remain visible and obey each reporter's existing provenance rules.

Keep published bytes distinct from locally packed development bytes. A local rebuild is useful for development package shape but is not a replacement for the published compatibility input. Freeze the exact 0.9.2 bytes already acquired; a later patch must not silently move the baseline.

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
