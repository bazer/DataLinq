# Codebase review remediation — 2026-09-04

Implementation and verification completed on 2026-09-05 against the [original 29-finding review](Codebase%20Review%202026-09-04.md). There is one open pull request per finding, numbered **#114–#142**. No PR has been merged and no package has been published by this work.

The original priorities are preserved: four P1, twenty-one P2, and four P3 findings. Each PR contains its implementation, regression coverage or investigation evidence, and relevant compatibility notes. F29 is resolved as a verified, documented support boundary with stronger browser tests; it does **not** remove SQLitePCLRaw's unsupported native signatures or suppress their warnings.

## Finding-to-PR register

| Finding | Priority | Category | Implemented resolution | PR |
| --- | --- | --- | --- | --- |
| F01 | P1 | Correctness / security | Restrict primary-key fast lookup to equivalent query shapes; honor joins and pagination. | [#114](https://github.com/bazer/DataLinq/pull/114) |
| F02 | P1 | Security | Quote and escape identifier components consistently in queries and schema generation. | [#115](https://github.com/bazer/DataLinq/pull/115) |
| F03 | P1 | Cache consistency | Validate in-flight row publication against invalidation generations under the table gate. | [#132](https://github.com/bazer/DataLinq/pull/132) |
| F04 | P1 | Data loss / tooling | Separate successful file replacement from backup cleanup so a cleanup failure cannot trigger destructive rollback. | [#116](https://github.com/bazer/DataLinq/pull/116) |
| F05 | P2 | Resource ownership | Dispose the owned connection when MySQL reader setup or logging fails; preserve caller command ownership. | [#117](https://github.com/bazer/DataLinq/pull/117) |
| F06 | P2 | Provider correctness | Reject out-of-range TimeOnly values; preserve signed/multi-day TimeSpan defaults and microseconds. Align seeded fixture values with TIME(0). | [#118](https://github.com/bazer/DataLinq/pull/118) |
| F07 | P2 | Provider correctness | Generate BIT(1) as bool and wider BIT columns as ulong. | [#119](https://github.com/bazer/DataLinq/pull/119) |
| F08 | P2 | Provider correctness | Parse escaped enum members without splitting, renumbering, or corrupting emitted names/defaults. | [#120](https://github.com/bazer/DataLinq/pull/120) |
| F09 | P2 | Query correctness / performance | Preserve SQL result order during cached entity materialization. | [#121](https://github.com/bazer/DataLinq/pull/121) |
| F10 | P2 | Concurrency | Synchronize cache-history mutation, trimming, and snapshots; invoke callbacks outside the lock. | [#122](https://github.com/bazer/DataLinq/pull/122) |
| F11 | P2 | Cache policy / retention | Remove obsolete index-expiration nodes so old entries cannot evict replacements. | [#123](https://github.com/bazer/DataLinq/pull/123) |
| F12 | P2 | Performance | Maintain ordered eviction nodes instead of rescanning every cached row for each victim. | [#124](https://github.com/bazer/DataLinq/pull/124) |
| F13 | P2 | Security / observability | Redact SQL parameter values by default; provide bounded, escaped opt-in logging. | [#125](https://github.com/bazer/DataLinq/pull/125) |
| F14 | P2 | Security / correctness | Parameterize exact metadata existence queries and avoid wildcard matching/full listings. | [#126](https://github.com/bazer/DataLinq/pull/126) |
| F15 | P2 | Query correctness | Render empty/null membership consistently, including IN/NOT IN and nullable values. | [#127](https://github.com/bazer/DataLinq/pull/127) |
| F16 | P2 | Concurrency | Publish relation rows and keyed lookups as coherent snapshots with invalidation checks. | [#133](https://github.com/bazer/DataLinq/pull/133) |
| F17 | P2 | Cache consistency | Publish exactly one notification manager during concurrent first subscription. | [#128](https://github.com/bazer/DataLinq/pull/128) |
| F18 | P2 | Tooling reliability | Drain process stdout/stderr concurrently and handle cancellation, timeout, and cleanup failures. | [#129](https://github.com/bazer/DataLinq/pull/129) |
| F19 | P2 | Tooling / data protection | Honor OverwriteExistingModels=false in planning and final filesystem operations. | [#130](https://github.com/bazer/DataLinq/pull/130) |
| F20 | P2 | Resource ownership | Give internally created commands deterministic lifetimes across reader, scalar, non-query, and mutation paths. | [#134](https://github.com/bazer/DataLinq/pull/134) |
| F21 | P2 | Development security | Bind newly created test database ports to loopback by default in CLI and socket transports. | [#135](https://github.com/bazer/DataLinq/pull/135) |
| F22 | P3 | Public API | Implement command builders and relation mocks; deprecate nine never-working mutation entry points with compile-time errors. | [#136](https://github.com/bazer/DataLinq/pull/136) |
| F23 | P3 | Dependency security | Add a fail-closed, direct/transitive NuGet advisory audit to required CI and a daily workflow. | [#139](https://github.com/bazer/DataLinq/pull/139) |
| F24 | P3 | Build performance | Cache source emission per database using structural inputs while refreshing diagnostics from the current compilation. | [#141](https://github.com/bazer/DataLinq/pull/141) |
| F25 | P3 | Memory backend performance | Retain only a bounded ordered prefix for small pages, preserving stable ties and cancellation. | [#140](https://github.com/bazer/DataLinq/pull/140) |
| F26 | P2 | Schema tooling correctness | Report missing views as manual CREATE VIEW work instead of emitting CREATE TABLE. | [#137](https://github.com/bazer/DataLinq/pull/137) |
| F27 | P2 | Schema tooling correctness | Include explicit foreign-key/check/index review actions in missing-table scripts. | [#138](https://github.com/bazer/DataLinq/pull/138) |
| F28 | P2 | Startup concurrency | Atomically publish immutable provider registrations and add a fresh-process concurrency regression. | [#131](https://github.com/bazer/DataLinq/pull/131) |
| F29 | P2 | Platform compatibility | Audit exact SQLite dependency call paths and verify generated CRUD/rollback in both browser variants; document unsupported configuration/extension paths. | [#142](https://github.com/bazer/DataLinq/pull/142) |

## Merge order and compatibility

Start with the P1 fixes F01–F04. The following PRs are intentionally stacked; merge the parent first and retarget the child to master before merging it:

| Parent | Child |
| --- | --- |
| F03 / #132 | F16 / #133 |
| F04 / #116 | F19 / #130 |
| F05 / #117 | F13 / #125 and F20 / #134 |
| F26 / #137 | F27 / #138 |

Merge F17 / #128 with F16 / #133: F17 closes the concurrent notification-manager creation path used by relation subscriptions. F13 and F20 preserve the same F05 reader cleanup contract.

All 29 branches were combined locally in finding order, with follow-ups merged afterward, on `codex/review-fixes-verified`. The final combined source revision is `bf6b1b47c3f8492f60fbf7475a16bda493ec6a3f`. The branches combine without merge conflicts, and the final diff passes `git diff --check`. This is local integration evidence, not a remote merge or a substitute for CI after the eventual merge strategy changes commit history.

Two changes need explicit API migration review:

- **F28:** mutable provider dictionary fields become read-only snapshot properties. Custom providers must use `PluginHook.RegisterProvider` and recompile; binary compatibility with direct field access is not preserved.
- **F22:** nine mutation entry points that always threw are now compile-time obsolete errors. Use transaction mutations or caller-owned command builders. Executing raw commands still requires the caller to handle cache invalidation; this does not introduce tracked set-based mutations.

Other operational boundaries:

- **F06:** a TIME(0) column can round a fractional time just before midnight into a 24-hour duration on MySQL. Choose a suitable column precision or an explicit application rounding/truncation policy. The reader no longer silently wraps it.
- **F13:** SQL values are redacted by default. Enabling value logging deliberately exposes bounded values and requires application-specific judgment.
- **F21:** new containers bind to loopback. Existing containers retain their bindings until recreated; the documented whole-matrix recreate operation is destructive to test database contents and was not applied to the user's existing matrix. Disposable validation containers were removed.
- **F26/F27:** schema scripts explicitly report unsupported/manual work. They do not infer missing view definitions or silently claim complete constraint recreation.
- **F29:** native extension loading and arbitrary SQLitePCLRaw configuration remain outside the verified WebAssembly path. WASM0001 remains visible.

## Verification

### Pull-request CI

The current heads of all 29 PRs have successful latest CI runs: **320 successful checks** (11 per PR, plus the F23 dependency audit). PR #125 also retains an older cancelled run and its failed aggregate gate after its base changed; the later replacement run passed all 11 checks. That older run is not represented as successful.

The F23 live audit covered **28 of 28 restored solution projects**, with no known vulnerable dependencies reported by the advisory feed at the time of execution. Its self-tests also verified rejection of direct and transitive vulnerable packages, disabled audit coverage, and an unavailable audit feed. This is not a guarantee that dependencies have no undisclosed vulnerabilities.

### Combined suite

The final combined run passed **6,355 of 6,355 case executions**, with zero failures and process exit code 0. All five test projects built successfully. Ten TRX result files independently confirm the totals. This run used four concurrent TUnit tests to reduce local connection pressure; concurrency regressions still exercise their own worker tasks/processes.

| Suite | Targets | Passed / executed |
| --- | --- | --- |
| Generators | Local | 69 / 69 |
| Unit | Local | 1,804 / 1,804 |
| Memory | In-process provider | 149 / 149 |
| Compliance | SQLite file and memory | 899 / 899 |
| Compliance | MySQL 8.4 and 9.7 | 911 / 911 |
| Compliance | MariaDB 10.11 and 11.4 | 911 / 911 |
| Compliance | MariaDB 11.8 and 12.3 | 911 / 911 |
| MySQL provider-specific | MySQL 8.4 and 9.7 | 231 / 231 |
| MySQL provider-specific | MariaDB 10.11 and 11.4 | 235 / 235 |
| MySQL provider-specific | MariaDB 11.8 and 12.3 | 235 / 235 |

Reproduction command from the final integration branch:

```powershell
$env:DATALINQ_TEST_DB_HOST='127.0.0.1'
.\scripts\dotnet-sandbox.ps1 run --project src/DataLinq.Testing.CLI -- run --suite all --alias all --maximum-parallel-tests 4 --output failures
```

Log: `artifacts/review-fixes-2026-09-04/integration-verified-tests.log`. Raw host reports: `artifacts/test-results/20260905T155448851Z-0e2208e7ad054dd59ac87f5055b98ccd/`. Parsed totals: `artifacts/review-fixes-2026-09-04/integration-test-summary.json`.

Earlier combined attempts remain recorded:

1. `integration-all-tests.log`: 6,348 of 6,349 cases passed; one MySQL 8.4 connection failure. Windows recorded TCP/IP event 4227 at the same time, consistent with ephemeral-port reuse pressure. This run was red.
2. `integration-final-tests.log`: 6,345 of 6,349 cases passed; two connection failures and two employee-fixture TimeOnly decoding failures. The inner exception showed a stored `24:00:00` value. The fixture had generated fractional values for TIME(0); F06 now generates whole-second values and documents the precision boundary. All 18 focused TimeDurationTests case executions passed afterward across six servers. This second combined run was also red.

Connection failures occurred during a high-volume local server suite that opens many unpooled connections. The machine's ephemeral range was 49,152–65,535, and event 4227 established port pressure for the first attempt. The second attempt's connection errors are consistent with that condition, but no matching new event was observed, so the exact cause of those two failures is not claimed proven. No OS network settings were changed or healthy database containers restarted.

Earlier isolated checks also encountered a strict allocation-test fluctuation and a SQLitePCL finalizer crash, followed by successful reruns. F20 now verifies deterministic command disposal, including **24,576 instrumented commands disposed with zero live commands at completion**. That result does not establish that every possible native finalizer failure has been eliminated.

### Browser and documentation

Both combined SQLite browser variants (`sqlite-wasm-no-aot` and `sqlite-wasm-aot`) published and passed the generated CRUD/rollback/query smoke in headless Edge. The command exited 0 with `Outcome=Passed`, `IsCompleteForInvocation=true`, `ArtifactsComplete=true`, and `RunnerStateValidForEvidence=true`. Both reported zero page exceptions and the `verified-generated-crud-and-rollback` marker.

This ran from clean combined revision `cb80a0168003fb8eb4087be15f2b674ca893df09`. The only subsequent changes before the final matrix removed three trailing blank lines in test/documentation files; `git diff --ignore-blank-lines --exit-code cb80a016 HEAD` passed. Each browser variant still records 13 WASM0001 entries covering the native configuration signatures, and one console resource 404. The successful runtime smoke does not claim those diagnostics were removed.

The report is a two-target project-reference invocation, with `ReviewRequired=true` and `ValidForEvidence=false`; it is **not** the canonical eight-target package release gate. The complete report and per-target logs are under `artifacts/review-fixes-2026-09-04/integration-wasm/`.

The earlier isolated F29 clean-revision report also passed both AOT and non-AOT browser variants, including generated insert/read/update/rollback/delete verification. Its exact source/IL audit and limitations are in [SQLite WebAssembly Verification](https://github.com/bazer/DataLinq/blob/dceb0947ba109f6b53fb52c4f291d0ad0e08bf68/docs/contributing/SQLite%20WebAssembly%20Verification.md).

DocFX completed successfully outside the sandbox with **zero errors and two warnings** about duplicate generator analyzer-release Markdown inputs. The first sandbox attempt could not read the user's NuGet configuration and is not the API-generation evidence. A broken script link discovered during that pass was corrected in F23. The final generated HTML was checked for the published SQLite verification page, its links from Platform Compatibility, the audit documentation anchor, and absence of the invalid script link.

### Performance evidence

These are controlled local diagnostics, not production latency or canonical package-release evidence:

| Finding | Observed result | Limit |
| --- | --- | --- |
| F12 | Half-cache eviction at 5k/10k/20k rows changed from roughly 39/168/724 ms to 0.13/0.25/0.49 ms. | MediumRun with tiered compilation disabled; short-iteration warnings require review. One additional live node is retained per row. |
| F24 | Six-database incremental tests reuse unchanged emissions and refresh edited converter/diagnostic state. Unrelated-edit median allocations fell from about 25.3 MB to 11.9 MB. | Timing ranges overlap; no reliable IDE latency improvement is claimed. Some dependencies deliberately invalidate conservatively. |
| F25 | At 100k rows, Take(5) allocation fell from about 2,355 KiB to 11.35 KiB; Skip(100).Take(5) from about 2,356 KiB to 18.47 KiB. | ShortRun diagnostics; every row is still scanned, and larger prefixes retain the full-sort path. |

Detailed notes are included in the relevant PRs: [row eviction](https://github.com/bazer/DataLinq/pull/124), [generator emission](https://github.com/bazer/DataLinq/pull/141), and [Memory paging](https://github.com/bazer/DataLinq/pull/140).

## Evidence and handoff

The original reviewed product commit was `57aa3dd3cc99e1e214dfae6cae99527e3ae0386e`; the review document was committed as `d172a43b850a1d989534f7a4d87a80af9141562b`. The PR branches started from master `3915366a206626dffae0be670e5efa3e74003bd0`.

Local evidence is retained under `artifacts/review-fixes-2026-09-04/`: per-finding PR descriptions and logs, benchmark comparisons, source/IL audit material, full test logs, browser reports, DocFX logs, `final-pr-snapshot.json`, and `final-ci-latest.json`. These ignored files are local investigation evidence, not website downloads. The PR descriptions carry the reviewable verification summaries.

The unrelated pre-existing PR #111 was not changed. The main working copy is returned to master with this tracker left uncommitted for the user's review. The local integration branches remain available for inspection. No remote merge, package publication, or production deployment was performed.
