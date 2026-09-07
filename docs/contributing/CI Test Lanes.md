# CI Test Lanes

DataLinq's CI is organized around time to first useful failure and trustworthy per-provider evidence. It deliberately does not run one giant solution build followed by one giant test command.

## Pull requests and master

`Latest CI` starts these independent lanes together:

| Lane | Coverage | Infrastructure | Blocks |
| --- | --- | --- | --- |
| Smoke | Curated generator, unit, Memory, and SQLite representatives | No Podman | Pull requests and master |
| Local shards | Complete generator, unit, Memory, SQLite-file, and SQLite-memory suites | No Podman | Pull requests and master |
| Latest server shards | Compliance and provider-specific tests on MySQL 9.7 and MariaDB 12.3 | One Podman target per shard | Pull requests and master |
| Dependency advisory audit | Full solution restore, direct/transitive advisories at every severity, and feed/coverage checks | Online NuGet audit source; WebAssembly workload for graph evaluation | Pull requests and master |
| Latest required gate | Requires every smoke/local/server job and the dependency advisory audit to succeed | None | The single branch-protection result |

The smoke result is independent and normally appears first; slower server setup cannot hide an immediate compiler, query, mutation, cache, or SQLite regression. Matrix `fail-fast` is disabled, so one broken target does not suppress the evidence from the other required targets.

Pull-request and branch runs use a workflow/ref concurrency group with cancellation. A newer commit cancels obsolete work for the same pull request or branch. Master uses the same complete latest-provider-family contract; it does not treat a green smoke job as permission to ignore a failed server shard.

Each shard builds `DataLinq.Testing.CLI`, asks the CLI to build its selected test project exactly once, and then executes the resolved host DLL directly. Shards upload raw logs, HTML, TRX, fixture telemetry, and their summary with `if: always()`. Artifact names contain the logical shard, Actions run id, and run attempt, so retries and concurrent runs cannot overwrite one another.

## Dependency advisory audit

The reusable `dependency-audit.yml` workflow runs with `Latest CI`, can be dispatched manually, and runs daily at 05:17 UTC to detect newly disclosed advisories without waiting for a code change. Normal local/offline test profiles may still use `NuGetAudit=false`; those results are not advisory evidence.

Run the dedicated gate with PowerShell 7:

```powershell
./scripts/audit-dependencies.ps1 -SelfTestOnly
./scripts/audit-dependencies.ps1
```

The script uses an explicit audit source, forces dependency reevaluation, disables the HTTP cache, checks transitive packages at every severity, and treats `NU1900` through `NU1905` as errors. The Windows wrapper preserves its explicit config and source-failure policy. `Directory.Solution.targets` requires NuGet's audited-project count to equal the complete solution restore count, and the script requires that coverage marker as well. Static graph restore is disabled for this gate because NuGet does not expose the coverage counters there. The WebAssembly workload is installed in CI so browser projects remain in the restored graph. See [NuGet audit configuration and coverage counters](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).

Self-tests restore isolated fixtures under `artifacts/dependency-audit` without building or executing them. They require failure for direct and transitive versions affected by [GHSA-5crp-9r3c-p9vr](https://github.com/advisories/GHSA-5crp-9r3c-p9vr), successful auditing of a clean fixture, failure when auditing is disabled at solution level, and failure for an unavailable audit feed. The transitive case uses the published dependencies of [Microsoft.AspNet.WebApi.Client 5.2.7](https://www.nuget.org/packages/Microsoft.AspNet.WebApi.Client/5.2.7). These deliberately vulnerable fixture references are generated locally and are not product dependencies.

Logs are uploaded even when the gate fails. A feed or restore failure is inconclusive and blocks the gate; it is not a clean result. A clean audit only means the configured feed reported no known applicable advisories at that time. Advisory-specific `NuGetAuditSuppress` items require a separately reviewed rationale; none are introduced by this gate.

## Nightly full matrix

The nightly workflow fans out into 17 canonical shards:

- generator, unit, and Memory suites;
- compliance on SQLite file, SQLite memory, MySQL 8.4/9.7, and MariaDB 10.11/11.4/11.8/12.3;
- the MySQL-specific suite on both MySQL targets and all four MariaDB targets.

Every provider shard uses `--batch-size 1`. The SQLite-file compliance shard and MySQL 9.7 provider-specific shard are the invariant anchors. MySQL 8.4 and every other provider shard declare `target-specific`, which applies the provider-affinity filter inside the runner and records that role in the invocation, expected row, and result row.

After every shard finishes—even if one failed—the aggregate job downloads all matching artifacts and applies schema `v0.9.testing-shard-aggregate.v2`. Before validation it loads the per-shard case-count baseline published by the previous successful master run. Aggregation fails closed unless all of the following are true:

- exactly one report exists for every canonical suite/target and no unexpected or duplicate shard exists;
- every report uses test-summary schema `v0.9.testing-run-summary.v2`, the requested configuration, the same OS/architecture/.NET runtime, and the exact Actions commit SHA;
- checkout and runner attestations are clean, stable, and commit-matched;
- the shard built once under the CI profile and contains exactly one complete passing result row;
- its affinity role matches the canonical manifest;
- its case count meets both the source-controlled floor and the previous successful per-shard count;
- its uploaded raw log, HTML report, and TRX file are present after download.

A broad multi-target batch is never release evidence. The aggregate is the nightly/release gate; a missing or duplicate target, incompatible schema, wrong commit, wrong configuration, count regression, failed/skipped case, or absent artifact makes it fail. Test-count growth is accepted automatically and becomes the next successful baseline; a later loss in any individual shard still fails even when another shard grows enough to hide it in the total. All count and role mismatches are reported together. Badge and baseline publication happen only after the aggregate succeeds on master. Nightly failure therefore cannot ratchet the baseline downward or rewrite master. An intentional reviewed coverage reduction must change the source floors and increment the source-controlled baseline epoch; ordinary code changes cannot silently reset history. A failed run blocks using that run as release evidence, and a release must use a successful aggregate produced from the exact candidate commit and configuration.

## Critical-path measurements

The pre-sharding baseline uses the most recent five completed runs per lane before this workflow change. Durations are Actions `run_started_at` to `updated_at`, which includes setup, build, tests, reporting, and ordinary orchestration overhead.

| Lane | Before samples | Before median | After median |
| --- | --- | ---: | ---: |
| Pull request | 365, 377, 384, 398, 401 s | 384 s | Record after five successful sharded runs |
| Master/latest | 339, 359, 380, 388, 389 s | 380 s | Record after five successful sharded runs |
| Active nightly/full | 358, 381, 423, 533, 581 s | 423 s | Record after five successful sharded runs |

Do not compare only summed runner minutes: sharding intentionally trades some repeated runner setup for a shorter human-facing critical path. Record the median only after five successful runs of the same lane and preserve failed-run timings separately; deleting slow failures would make the metric dishonest.
