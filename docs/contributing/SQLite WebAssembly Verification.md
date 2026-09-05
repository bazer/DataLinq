# SQLite WebAssembly native configuration boundary

Resolution of [review finding F29](https://github.com/bazer/DataLinq/blob/d172a43b/docs/dev-plans/Codebase%20Review%202026-09-04.md#f29). The native varargs warnings remain visible. The generated in-memory SQLite browser path avoids the warned entry points; native extension loading and direct SQLitePCLRaw configuration calls are outside this verified boundary.

## Exact dependency and call-path audit

The tested browser assets resolve Microsoft.Data.Sqlite/Core 10.0.11, SQLitePCLRaw core/config/provider/bundle 3.0.5, and the native SQLite package 3.53.4. Builds use .NET SDK 10.0.400 and WebAssembly workload assets 10.0.11.

- `SqliteConnection` initializes SQLitePCLRaw batteries. Batteries installs the e_sqlite3 provider, and `raw.SetProvider` checks its library version. This path does not invoke `sqlite3_config`. Source: [SqliteConnection initialization](https://github.com/dotnet/dotnet/blob/e2f47b0110ed922f21a1522da67279133ce28f32/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs#L55), [batteries](https://github.com/ericsink/SQLitePCL.raw/blob/ed046114d5a30534e13294d94d78eb73de896ad4/src/common/batteries_v2.cs), and [provider installation](https://github.com/ericsink/SQLitePCL.raw/blob/ed046114d5a30534e13294d94d78eb73de896ad4/src/SQLitePCLRaw.core/raw.cs#L51).
- `SqliteConnection.Open` calls `sqlite3_db_config` only when extensions were queued through `LoadExtension`. Calling `LoadExtension` on an open connection can also invoke it. Fresh DataLinq-owned connections do not queue extensions, and no DataLinq runtime source calls `LoadExtension`, `EnableExtensions`, or either warned configuration API. See the exact [Open branch](https://github.com/dotnet/dotnet/blob/e2f47b0110ed922f21a1522da67279133ce28f32/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs#L300) and [LoadExtension branch](https://github.com/dotnet/dotnet/blob/e2f47b0110ed922f21a1522da67279133ce28f32/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs#L610).
- The provider exposes several managed signatures for SQLite's two native varargs entry points. Their presence is why the WebAssembly SDK warns even though the smoke does not take these branches. No binding has been made safe by hiding the warning. See the pinned [native imports](https://github.com/ericsink/SQLitePCL.raw/blob/ed046114d5a30534e13294d94d78eb73de896ad4/src/SQLitePCLRaw.provider.e_sqlite3/Generated/provider_e_sqlite3_funcptrs_notwin.cs#L1849).

The commits above come from the installed packages' nuspec repository metadata. A separate PE metadata/IL inspection of the actual Microsoft.Data.Sqlite.dll found exactly two configuration call sites: `SqliteConnection.Open` and `SqliteConnection.LoadExtension`, both calling `sqlite3_db_config`; no `sqlite3_config` reference exists there. The batteries and DataLinq.SQLite assemblies have no configuration call references. This corroborates the source audit; it does not prove arbitrary user code, reflection, native extension loading, or direct raw configuration APIs safe.

Local audit materials are retained under `artifacts/review-fixes-2026-09-04/`: `F29-upstream/`, `F29-audit-il.ps1`, and `F29-il-results.json`. These are local verification artifacts, not website downloads.

## Executed behavior

The shared SQLite platform smoke now checks a temporary generated row through committed insert, committed update, transaction-local update visibility followed by rollback, and committed delete. It verifies every resulting value and leaves the original query fixtures intact. A failure throws before the page can report success. Browser telemetry includes `verified-generated-crud-and-rollback`.

The same page also checks generated metadata construction, raw SQLite opening and PRAGMAs, two connections sharing the in-memory database, generated schema creation, relation/projection queries, aggregates, ordering/paging, membership, null handling, and deterministic unsupported-query diagnostics. Desktop execution passed before browser publishing.

On 2026-09-05, both `sqlite-wasm-no-aot` and `sqlite-wasm-aot` published and passed in headless Microsoft Edge through Playwright from clean source revision `625d2396a4c221c789e2557897aff466b8555063`. Both logs contain the CRUD/rollback success marker and no page exceptions. Each retains 13 `WASM0001` diagnostic entries describing the two configuration functions and their managed signatures. The browser console also records one resource 404 per run; it did not prevent the runtime/CRUD smoke and is not claimed to be a clean network-resource audit.

The final command returned exit code 0: `Outcome=Passed`, `IsCompleteForInvocation=true`, `ArtifactsComplete=true`, and `RunnerStateValidForEvidence=true`. The report still has `ReviewRequired=true` and `ValidForEvidence=false`: this is a two-target project-reference verification, not the eight-target package-backed canonical release gate. Report: `artifacts/review-fixes-2026-09-04/F29-wasm-clean/report.json`; browser logs are in each target subdirectory. No package release is inferred from this result.

Initial attempts are retained separately: the sandbox could not reach the package source; reusing a populated report directory was correctly rejected; both browser runs then passed but the overall command failed because its runner was built from a dirty working tree. Those reports are not presented as a green command or release evidence. A later clean-revision report is the verification record above.

## Reproduction and support limit

From a clean checkout, build `DataLinq.Dev.CLI` with `--no-incremental` so its runner revision matches the checkout, then run:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src/DataLinq.Dev.CLI -c Debug --no-build -- size-report --target v0.9 --targets 'sqlite-wasm-no-aot,sqlite-wasm-aot' --output artifacts/<fresh-report-directory> --stop-on-publish-failure
```

The output directory must be fresh. The comma-separated selector must be quoted in PowerShell. Browser smoke results and logs matter; successful publishing alone is insufficient. On this host, package-source access required running outside the sandbox.

This proves the generated in-memory SQLite CRUD/query smoke with these dependencies and browser. It does not add native extension loading, arbitrary SQLitePCLRaw configuration, OPFS/file-backed storage, all LINQ shapes, other SQL providers in a browser, or a small browser payload. Dependency upgrades require rerunning this audit and the browser checks. `WASM0001` remains unsuppressed because the unsupported entry points still exist.

