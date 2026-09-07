# Incremental generator emission

Resolution of [review finding F24](Codebase%20Review%202026-09-04.md#f24).

The generator now separates semantic preparation, source emission, and diagnostics. Compilation changes still refresh scalar converter validation, runtime property types, default-value compatibility, constructor capabilities, and nullable contexts. Each successful database then passes through a structural comparer **before** `GeneratorFileFactory` formats its sources. Reusing identical generated strings after formatting would not avoid that work.

The emission signature includes the database and its model declaration shapes, resolved scalar and Guid storage facts, generated converter location anchors, runtime property names, suppressed defaults, constructor support, and effective nullable settings. Declaration identities are retained globally because additions/renames can change name resolution. Missing declaration dependencies conservatively disable reuse. Imports and aliases now participate in declaration snapshots because metadata copies them into generated files.

Diagnostics are published from current semantic preparation independently of cached sources. Emission failures are resolved against the current compilation. Invalid converters remove the affected output, and correcting them restores it. Regression tests compare incremental failure/recovery and import/default-diagnostic edits with fresh generator runs, including current syntax-tree identity and exact diagnostic spans after comments move declarations.

## Measurement

The tracked workload has six databases, eight tables per database, and 529 properties in total. It performs four cycles of an unrelated method edit in another file, mapped-column edits confined to one database, and a converter provider-type change between Int32 and Int64. The initial generated compilation must compile without errors, and the test verifies emitted converter metadata changes. Only `RunGenerators` is timed; syntax replacement, initial compilation validation, and assertions are outside the measured interval.

Measured on Windows x64, .NET 10, Debug generator test harness on 2026-09-05. Baseline product source is master `3915366a`; candidate is this change. Both use the same corrected consumer-compilation fixture and runtime assembly reference. Four observations per scenario, medians below:

| Edit | Baseline elapsed ms | Candidate elapsed ms | Baseline allocated bytes | Candidate allocated bytes | Candidate emission steps |
|---|---:|---:|---:|---:|---|
| Unrelated method | 311.207 | 264.418 | 25,309,140 | 11,926,468 | 6 cached |
| One database | 922.599 | 896.842 | 38,459,832 | 27,624,992 | 1 modified, 5 cached |
| Converter contract | 296.308 | 266.148 | 25,295,648 | 14,451,100 | 1 modified, 5 cached |

The baseline callback emitted every database for every compilation change. The candidate test asserts the six tracked emission reasons on every iteration, so the reuse result is a regression contract rather than a timing assumption.

**Limits:** these are local diagnostic measurements, not BenchmarkDotNet release evidence or measured IDE latency. Timing ranges overlap (one-database baseline 883–1,012 ms; candidate 763–1,099 ms), so this does not establish a reliable latency improvement. The allocation reduction and eliminated emissions are the demonstrated improvements. Parsing declaration metadata and semantic preparation remain significant costs. All enum declaration shapes are still included conservatively; an enum edit can invalidate unrelated database emission until the syntax parser exposes precise enum dependencies. No claim is made that all compilation-related work has disappeared.

Local logs: `artifacts/review-fixes-2026-09-04/F24-baseline-corrected.txt` and `F24-final-measurement.txt`. Earlier measurements without the runtime reference did not exercise converter semantics and are excluded. To record new observations, set `DATALINQ_GENERATOR_MEASUREMENT_PATH` to an output file and run the `IncrementalGeneratorScaleTests/MultiDatabaseEditWorkload` TUnit test through Testing CLI. Force a Debug generator rebuild when switching revisions to avoid stale analyzer output.

The generator suite contains 69 passing cases, including existing nullable, enum, source emission, and converter coverage. The scale test validates actual generated compilation and type changes; separate incremental tests cover default diagnostics and invalid-converter recovery.
