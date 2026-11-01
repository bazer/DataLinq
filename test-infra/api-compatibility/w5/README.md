# W5 compiled contract and candidate evidence

W5 runtime/startup validation is implemented for review in [PR #254](https://github.com/bazer/DataLinq/pull/254). Candidate **`0.10.0-w5.9479fe97`** was packed from clean commit **`9479fe9760a796fa1cbe18334160759cf869b182`** using `publish-nuget.ps1 -PackOnly`. No package was published. The strict package/API/platform runners were rebuilt non-incrementally at that same clean revision. Verifier/evidence commits through `2defe71f` preserved those candidate bytes. The subsequent SQLite review corrections change runtime code; the original packed/platform/full-matrix evidence below remains evidence for that earlier candidate.

## SQLite review corrections, 2026-11-01

Both findings in the [review of `2defe71f`](https://github.com/bazer/DataLinq/pull/254#issuecomment-6560957298) are corrected:

- Sync and async runtime acquisition share a catalog query excluding the reserved `sqlite_` prefix. `ANALYZE` statistics and sequence tables cannot enter the application graph. The literal underscore preserves ordinary tables such as `sqliteXstatistics`.
- SQLite runtime comparison folds ASCII A–Z only, consistently across table/column/index/constraint identities, Include resolution, ambiguity checks, omission evidence and reference canonicalization. Runtime column lookup uses that comparer instead of the legacy Unicode lookup. Non-ASCII case variants remain distinct; server rules and legacy import/CLI behavior retain their existing contracts.

Fourteen new public validation/startup cases first failed against the reviewed implementation: receipt `20261101T170325212Z-479cecf749c24081ac3a0c2b1fddc1fc`. After the fixes, Release quick passes **5,459/5,459**, zero failures/skips: generators 199, unit 4,456, Memory 223, SQLite-file compliance 581. Receipt: `20261101T170659359Z-3d89a283fe904e8e974dc4d29fed43f8`. TRX counters were checked. This includes **22 new regression cases**: file/memory sync/async validation after ANALYZE, both startup modes, non-ASCII table/column rename drift, Include selection, ASCII collisions, distinct non-ASCII names and native foreign-key/index canonicalization.

Reproduce the quick run from the repo root with a Debug Testing CLI runner and Release tests:

```powershell
.\scripts\dotnet-sandbox.ps1 run --no-build --configuration Debug --project src/DataLinq.Testing.CLI "--" run --plan quick --configuration Release --output failures
```

Non-incremental Release builds of `DataLinq.SQLite` and its core/generator dependencies pass on net8.0/net9.0/net10.0 (generator: netstandard2.0), with zero warnings/errors. `docfx build docfx.json` also passes with zero warnings/errors; the generated runtime guide contains the ASCII identifier and internal-object guidance.

These are working-tree regression receipts, not a replacement packed candidate or certified full release evidence. No package, deployment or platform report has been relabeled as covering the corrections.

## Functional and consumer evidence

| Gate | Result |
| --- | --- |
| Release full plan | **9,532/9,532**, zero failures/skips; generators 199, unit 4,434, Memory 223, compliance 3,676 and server-specific 1,000. Both SQLite targets and all six MySQL/MariaDB versions. Run `20261101T155641760Z-b3495c0cf21a461a905f6a4cfd5506de`. |
| Release quick plan | **5,437/5,437**, zero failures/skips. Run `20261101T161535475Z-571b3a8913da472aa399c7cc62637d8b`. These overlap the full plan. |
| Package inspection | All six public packages and symbols pass, clean matching provenance, `ValidForEvidence=true`. |
| Isolated package smoke | Exact-package restore, all three TFM builds/generated checks and net10 execution pass. |
| W3 consumers | Nine packed async/EF/Memory/unchanged-0.9.2-binary executions pass in explicit W5 mode. Original source bindings are unchanged. |
| W4 consumers | Six packed session/registration/service/host executions pass. |
| W5 consumers | Three packed and six isolated published executions pass on actual **.NET 8.0.33, 9.0.20 and 10.0.14**. Each runs 48 host mode/order/concurrency/scope combinations, callback provenance and direct checks. Net10 additionally exercises four real HTTP listener barriers. |
| Native insensitive servers | The same packed W5 consumer passes mode 1 on MySQL 9.7.2 and MariaDB 12.3.2, including Unicode identities, effective schema, exact Include and literal drift. Mode-2 attempts return mode 0 on the Linux filesystem and are explicitly unavailable, not native passes. |
| Documentation | DocFX build: zero warnings/errors; generated runtime guide, cross-links and Usage navigation inspected. |
| Candidate CI | [Latest CI #713](https://github.com/bazer/DataLinq/actions/runs/41322894894) passes on `9479fe97`. Later record/verifier commits retain their own CI identity. |

All six candidate fixtures build without warnings/errors on net8/net9/net10. The EF negative build contains only the expected `CountAsync` **CS0121** diagnostic on each TFM. The W5 published copies execute outside the checkout with no model source or Tools files, first without tooling configuration and then with malformed `datalinq.json` and `datalinq.user.json`. Both direct code-supplied connections and application-owned host configuration exercise pass/drift and full/Include scope. Hashes bind deployed DataLinq assemblies to the exact candidate.

The initial W5 verifier stopped at its outside-checkout guard because invoking the sandbox build wrapper changed the parent's TEMP. The corrected verifier executes already-built DLLs with the native runtime, captures the deployment root before execution, and records its own and linked-source hashes. This fixture correction was verified against unchanged candidate bytes. Sandbox failures in junction/socket tests, NuGet audit access, compiler-task startup and MSBuild task-host creation remain diagnostic history; their successful retries do not rewrite the failed runs.

## Public contracts and compatibility

`compiled-contract.txt` projects **13 actual types**, including Database members, validator/options/result, result/read/startup exceptions, startup modes/options/target/outcome, central selection and fluent extensions. It preserves generic constraints, visibility, nullability, optional values and enum values. Projections from the actual net8/net9/net10 executions are **identical** (SHA-256 `859f8bba529e67773e7ba262cd8547ae0e97da2efa4687a7edc5f13ed3da6634`). Full metadata remains in the ApiCompat snapshots.

| Inventory | Frozen implementation and proof |
| --- | --- |
| A05 | `Database<T>.ValidateSchemaAsync(Action<DataLinqSchemaValidationOptions>? configure = null, CancellationToken cancellationToken = default)` returns `Task<DataLinqSchemaValidationResult>`. Callback executes once synchronously and captures inputs before suspension. |
| A06 | `EnsureSchemaValidAsync` has the same optional callback/final token and returns `Task`; completed policy failure throws with the exact result. Operational failure and cancellation remain distinct. |
| A07 | Static `DataLinqSchemaValidator.ValidateAsync(IDatabaseProvider provider, DataLinqSchemaValidationOptions? options = null, CancellationToken cancellationToken = default)` returns the same structured result. Synchronous companions are also exported and executed. |
| B16 | Core package placement, existing effective provider identity, independent owned metadata sessions, W4 borrowed/transferred ownership, canonical key/default selection, captured defaults/overrides, global disablement, sequential settled outcomes and the host StartingAsync barrier are covered by focused/full suites and external consumers. |

Pinned ApiCompat **10.0.400** captures **36 surfaces**. Its raw result remains **failed with 13 compatibility diagnostics** whose exact fingerprints equal the [accepted W3 findings](../../../docs/dev-plans/roadmap-implementation/v0.10/W3%20Compatibility%20Evidence.md#apicompat-findings) and the W4 record. There are **1,065 additive/compatible findings**, **two inherited divergences**, and **zero current-framework mismatches**. Runner/candidate provenance is valid. This proves no new W5 API break against that accepted boundary; it is not a blanket compatibility pass or approval of arbitrary old implementations.

Core adds only **Hosting.Abstractions 10.0.11** to the W4 dependency set. Actual dependency groups contain no Tools, full Hosting or ASP.NET package; async LINQ remains 10.0.12 on net8/net9 and absent on net10. Generator payload stays under `analyzers/dotnet/cs`, with no runtime Roslyn payload. The Memory-only consumer and all four Memory platform restore graphs contain no SQL-provider package; its separate root exposes no SQL validation API.

## Platform findings retained for release review

All **eight** package-backed SQLite/Memory Native AOT, trimmed, browser no-AOT and browser AOT targets publish, execute their smoke contracts and complete payload inspection. Browser contracts reach their final stages with no page exceptions. All package-resolution checks pass; there are no size-threshold violations or new DataLinq trim/AOT warnings. These are existing capability smoke tests against the new dependency graph, not performance acceptance or a claim that every validation/hosting API executes in a browser.

The strict platform report nevertheless remains **`Outcome=Failed`, `ValidForEvidence=false`** because its byte-token scanner reports **14 Memory payload findings**. The findings are two provider-name tokens in trimmed `DataLinq.dll` and those same two tokens in each raw/Brotli/gzip WebCIL core asset for both browser modes.

The [metadata probe](metadata-probe/Program.cs) audits the exact seven flagged files plus W4/candidate core assemblies. In all nine inputs, each provider name appears once, solely within the exact serialized `InternalsVisibleToAttribute` value; there are no UTF-16 occurrences. It decodes actual PE custom attributes and assembly references for W4, W5 and the trimmed core: neither SQL-provider assembly is referenced. The WebCIL/compressed checks identify the same exact attribute blobs rather than pretending to parse their assembly metadata. Independently, every Memory target's complete restore graph has zero SQL-provider packages. Source inspection shows both friend declarations predate W5. This supports treating these specific findings as byte-scanner false positives for the SQL-dependency boundary, **without suppressing them or changing the scanner**. Final release acceptance still needs a metadata-aware tooling disposition; this report is not certified release evidence.

Both SQLite browser lanes retain **13 emitted WASM0001 diagnostic lines** concerning SQLitePCLRaw's `sqlite3_config`/`sqlite3_db_config` varargs imports. Those are real third-party warnings. The passing smoke does not establish that every such function is unreachable; the existing SQLite provider/release obligation remains. Mode-2 native testing likewise still needs a supported case-insensitive filesystem. W8/W9 performance and final release acceptance, reviewer acceptance, merge and publication remain separate.

## Exact receipts

Paths below are relative to `artifacts/w5-consumers/9479fe97/`. Hashes identify local report bytes, including machine paths/timestamps.

| Artifact | SHA-256 |
| --- | --- |
| `packages/report.json` | `f3214d43935c8b0f301ae68b17a2fcfb365beec9d20a31e27d71e42ffeae7259` |
| `api/report.json` | `6218957937f075ac4b41630fb0954cf0adfbda5ef0a23026688f3f6066822ea4` |
| `w3-verified/report.json` | `5c62b8017110a6cf218087bd8dd4358241426da4e03fb9674471cd5ef35d5e07` |
| `w4-verified/report.json` | `625dabd6bcfb86a8b39aa7a5f2c480e25e968e06975e9aa197c001f815ad7d5a` |
| `w5-verified-retry/report.json` | `c6b66caac81278cb16731b2404680de71b5f8ca137a1f2428782d1bec46815ac` |
| `package-smoke/report.json` | `bbbeb347d36a0d27c395e1c6a8400ebc961cf39e2ac2b850a2aa6b4691504541` |
| `server-cases/report.json` | `28feb61737086ea818f1168e02a97e24a7d516bbbf62bab98fc649e50c6bafc2` |
| `platforms-retry/report.json` (raw failed gate) | `12c9a0df56cbbb66bc35bbc0a6403cef527a1cd231c48a9f1bed0e22f59dc121` |
| `friend-metadata-audit.json` | `e24366b3f062ce64576d513ba256d9f367f566ca78b70d4a594317ec45ac9f3a` |
| `memory-graph-audit.json` | `7800779119f92af11a80183202e7f74ecc0f75f5d98ed080919e808a23c1ea62` |
| `full-summary.json` (10 TRX files) | `aa106c70197a06bd3339a31fe21faf188316757812e474412e40409634e4b0c3` |
| `quick-summary.json` (4 TRX files) | `caf9144b40921b20ce49bc1c80dec5db5f12d029b6ede6d8a7c0a33470c0d9cc` |

Follow [W5 Consumers](../../../tests/fixtures/W5%20Consumers.md) to reproduce package/contract/deployment evidence. To audit the retained platform token findings without changing their raw status:

```powershell
$root = 'artifacts/w5-consumers/9479fe97'
$platform = Get-Content "$root/platforms-retry/report.json" -Raw | ConvertFrom-Json
$files = @(
    'artifacts/w3-consumers/78d1b3c4/packages-candidate/datalinq/0.10.0-w4.002bf74b/lib/net10.0/DataLinq.dll',
    'artifacts/w3-consumers/78d1b3c4/packages-candidate/datalinq/0.10.0-w5.9479fe97/lib/net10.0/DataLinq.dll')
foreach ($target in $platform.Targets) {
    foreach ($path in @($target.BannedPayloads | ForEach-Object RelativePath | Sort-Object -Unique)) {
        $files += Join-Path $target.PublishDirectory $path
    }
}
.\scripts\dotnet-sandbox.ps1 restore test-infra/api-compatibility/w5/metadata-probe/MetadataProbe.csproj
.\scripts\dotnet-sandbox.ps1 run --project test-infra/api-compatibility/w5/metadata-probe/MetadataProbe.csproj -- "$root/friend-metadata-reproduced.json" @files
```
