# CI Test Lanes

DataLinq's CI is organized around time to first useful failure and trustworthy per-provider evidence. It deliberately does not run one giant solution build followed by one giant test command.

## Pull requests and master

`Latest CI` starts these independent lanes together:

| Lane | Coverage | Infrastructure | Blocks |
| --- | --- | --- | --- |
| Smoke | Curated generator, unit, Memory, and SQLite representatives | No Podman | Pull requests and master |
| Local shards | Complete generator, unit, Memory, SQLite-file, and SQLite-memory suites | No Podman | Pull requests and master |
| Latest server shards | Compliance and provider-specific tests on MySQL 9.7 and MariaDB 12.3 | One Podman target per shard | Pull requests and master |
| Latest required gate | Requires every smoke/local/server matrix job to succeed | None | The single branch-protection result |

The smoke result is independent and normally appears first; slower server setup cannot hide an immediate compiler, query, mutation, cache, or SQLite regression. Matrix `fail-fast` is disabled, so one broken target does not suppress the evidence from the other required targets.

Pull-request and branch runs use a workflow/ref concurrency group with cancellation. A newer commit cancels obsolete work for the same pull request or branch. Master uses the same complete latest-provider-family contract; it does not treat a green smoke job as permission to ignore a failed server shard.

Each shard builds `DataLinq.Testing.CLI`, asks the CLI to build its selected test project exactly once, and then executes the resolved host DLL directly. Shards upload raw logs, HTML, TRX, fixture telemetry, and their summary with `if: always()`. Artifact names contain the logical shard, Actions run id, and run attempt, so retries and concurrent runs cannot overwrite one another.

## Nightly full matrix

The nightly workflow fans out into 17 canonical shards:

- generator, unit, and Memory suites;
- compliance on SQLite file, SQLite memory, MySQL 8.4/9.7, and MariaDB 10.11/11.4/11.8/12.3;
- the MySQL-specific suite on both MySQL targets and all four MariaDB targets.

Every provider shard uses `--batch-size 1`. The SQLite-file compliance shard and MySQL 9.7 provider-specific shard are the invariant anchors. MySQL 8.4 and every other provider shard declare `target-specific`, which applies the provider-affinity filter inside the runner and records that role in the invocation, expected row, and result row.

After every shard finishes—even if one failed—the aggregate job downloads all matching artifacts and applies schema `v0.9.testing-shard-aggregate.v1`. Aggregation fails closed unless all of the following are true:

- exactly one report exists for every canonical suite/target and no unexpected or duplicate shard exists;
- every report uses test-summary schema `v0.9.testing-run-summary.v2`, the requested configuration, the same OS/architecture/.NET runtime, and the exact Actions commit SHA;
- checkout and runner attestations are clean, stable, and commit-matched;
- the shard built once under the CI profile and contains exactly one complete passing result row;
- its affinity role and exact case count match the canonical manifest;
- its uploaded raw log, HTML report, and TRX file are present after download.

A broad multi-target batch is never release evidence. The aggregate is the nightly/release gate; a missing or duplicate target, incompatible schema, wrong commit, wrong configuration, count drift, failed/skipped case, or absent artifact makes it fail. Badge publication happens only after that aggregate succeeds. Nightly failure does not rewrite master, but it blocks using that run as release evidence. A release must use a successful aggregate produced from the exact candidate commit and configuration.

## Critical-path measurements

The pre-sharding baseline uses the most recent five completed runs per lane before this workflow change. Durations are Actions `run_started_at` to `updated_at`, which includes setup, build, tests, reporting, and ordinary orchestration overhead.

| Lane | Before samples | Before median | After median |
| --- | --- | ---: | ---: |
| Pull request | 365, 377, 384, 398, 401 s | 384 s | Record after five successful sharded runs |
| Master/latest | 339, 359, 380, 388, 389 s | 380 s | Record after five successful sharded runs |
| Active nightly/full | 358, 381, 423, 533, 581 s | 423 s | Record after five successful sharded runs |

Do not compare only summed runner minutes: sharding intentionally trades some repeated runner setup for a shorter human-facing critical path. Record the median only after five successful runs of the same lane and preserve failed-run timings separately; deleting slow failures would make the metric dishonest.
