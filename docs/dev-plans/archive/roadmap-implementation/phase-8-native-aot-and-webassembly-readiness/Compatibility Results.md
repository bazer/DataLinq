> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a general support claim for DataLinq unless product documentation later promotes a narrower statement.
# Phase 8 Compatibility Results

**Status:** Implemented and verified for the generated SQLite smoke boundary.

**Recorded:** 2026-05-04.

## Proven Boundary

The Phase 8 proof uses generated SQLite models in `src/DataLinq.PlatformCompatibility.Smoke` and exercises:

- generated database metadata hooks
- generated table model declarations
- generated mutable/immutable instance factories
- schema creation from generated metadata
- SQLite insert and query execution
- generated relation loading
- translated filtering and ordering
- computed projection materialization without hot-path `Expression.Compile()`
- browser-safe cache startup with no cleanup worker in WebAssembly

This is the right support boundary to claim. It proves the path DataLinq should prefer for constrained platforms. It does not prove arbitrary reflection-discovered models, arbitrary client projection expressions, MySQL/MariaDB browser support, or full query-provider compatibility under Native AOT.

## Native AOT

Command:

```powershell
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.AotSmoke\DataLinq.AotSmoke.csproj -f net10.0 -r win-x64 -c Release -v:minimal --self-contained true -p:PublishAot=true
```

Result:

- publish passed
- executable smoke run passed
- DataLinq-owned AOT warnings were eliminated for the generated SQLite boundary
- remaining warnings came from `Remotion.Linq`
  - `IL3053`: assembly produced AOT analysis warnings
  - `IL2104`: assembly produced trim analysis warnings

Smoke output:

```text
DataLinq platform smoke passed
owners=2, tasks=3, open=2
first-task="Compile generated hooks", related-owner="Ada"
projection="COMPILE GENERATED HOOKS"/3
schema-ms=0.352
seed-ms=1.196
first-query-ms=1.406
repeated-query-ms=2.336
```

Native AOT publish size:

| Item | Size |
| --- | ---: |
| publish folder | 76.76 MiB |
| `DataLinq.AotSmoke.exe` | 18.61 MiB |
| `DataLinq.AotSmoke.pdb` | 56.20 MiB |
| `e_sqlite3.dll` | 1.71 MiB |

Interpretation:

Native AOT is real for the generated SQLite smoke path. The honest caveat is that `Remotion.Linq` still emits compatibility warnings, so DataLinq should not set broad package-level AOT compatibility until the Remotion boundary is either replaced, isolated, or precisely documented.

## Trimmed Runtime

Command:

```powershell
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.TrimSmoke\DataLinq.TrimSmoke.csproj -f net10.0 -r win-x64 -c Release -v:minimal --self-contained true -p:PublishTrimmed=true
```

Result:

- publish passed
- executable smoke run passed
- remaining trim warning came from `Remotion.Linq`
  - `IL2104`: assembly produced trim analysis warnings

Smoke output:

```text
DataLinq platform smoke passed
owners=2, tasks=3, open=2
first-task="Compile generated hooks", related-owner="Ada"
projection="COMPILE GENERATED HOOKS"/3
schema-ms=20.206
seed-ms=48.673
first-query-ms=55.321
repeated-query-ms=16.583
```

Trim publish size:

| Item | Size |
| --- | ---: |
| publish folder | 32.10 MiB |
| file count | 95 |
| largest runtime file | `coreclr.dll`, 4.40 MiB |
| `System.Private.CoreLib.dll` | 2.69 MiB |
| `Microsoft.CodeAnalysis.CSharp.dll` | 1.37 MiB |

Interpretation:

Trimming works for the generated SQLite smoke path, but the output still includes Roslyn assemblies because the runtime package currently references `Microsoft.CodeAnalysis.CSharp`. That is a payload smell, not a harmless detail. The generator/runtime package split should become follow-up work before anyone treats this as a polished constrained-platform story.

## WebAssembly

No-AOT publish command:

```powershell
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj -f net10.0 -c Release -v:minimal -p:RunAOTCompilation=false -o artifacts\tmp\phase8\wasm-noaot-final
```

No-AOT result:

- publish passed
- browser execution failed in the Mono interpreter
- first failure was `Microsoft.Data.Sqlite.SqliteConnectionStringBuilder`
- after replacing that with a literal connection string, the next failure was `SQLiteProvider.RegisterProvider`
- failure class: `MONO interpreter: NIY encountered`

Conclusion:

No-AOT browser WebAssembly is not a supported DataLinq SQLite runtime boundary from this phase. The build can publish, but the actual DataLinq/SQLite path fails in the interpreter. Pretending otherwise would be packaging theater.

WASM AOT publish command:

```powershell
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj -f net10.0 -c Release -v:minimal -p:RunAOTCompilation=true -o artifacts\tmp\phase8\wasm-aot-latest
```

WASM AOT result:

- publish passed
- 89 assemblies were AOT-compiled
- headless Chrome browser smoke passed against the published output
- browser log stages completed:
  - `creating-connection-string`
  - `registering-sqlite-provider`
  - `opening-generated-database`
  - `creating-schema-from-generated-metadata`
  - `enabling-foreign-keys`
  - `seeding-generated-models`
  - `querying-generated-relation`
  - `querying-generated-projection`
- remaining warnings were `WASM0001` native varargs warnings from SQLitePCLRaw `e_sqlite3`
  - `sqlite3_config`
  - `sqlite3_db_config`

Browser smoke DOM result:

```text
DataLinq SQLite on WebAssembly
passed
Owners 2
Tasks 3
Open 2
First query 26,3 ms
Repeated query 14,4 ms
Compile generated hooks
Ada owns the first generated model row.
```

WASM payload sizes:

| Publish | Total | Compressed assets | Brotli assets | Notable largest files |
| --- | ---: | ---: | ---: | --- |
| no AOT | 49.35 MiB | 18.20 MiB | 7.91 MiB | Roslyn wasm payloads, `dotnet.native.wasm` |
| AOT | 135.33 MiB | 46.48 MiB | 18.80 MiB | `dotnet.native.wasm` 61.27 MiB, Roslyn wasm payloads |

Interpretation:

WASM AOT is proven for the generated SQLite smoke boundary. The costs are not subtle: AOT roughly triples the uncompressed publish output in this sample, and the runtime Roslyn dependency is visibly bad for browser payload. The SQLitePCLRaw varargs warnings also need a sharper answer before this becomes a clean public support claim.

## Dynamic-Code Inventory Result

`Expression.Compile()` was removed from the DataLinq LINQ and instance hot paths checked by:

```powershell
rg -n "\.Compile\(|DynamicInvoke|Expression\.Lambda" src\DataLinq\Linq src\DataLinq\Instances
```

Remaining reflection-sensitive runtime sites are classified as generated-metadata bootstrap or compatibility paths:

| Site | Classification | Phase 8 stance |
| --- | --- | --- |
| `MetadataFromTypeFactory` generated hook parsing | startup, generated path | supported boundary |
| `MetadataFromTypeFactory` attribute/property reads | startup, generated-type metadata | annotated and rooted by generated table declarations |
| `MetadataTypeConverter.GetType(...)` for `DateOnly`/`TimeOnly` | generator/shared compatibility | acceptable for now, should disappear with a cleaner type map |
| `QueryExecutor.GetTypeCode(...)` | primitive runtime classification | acceptable |
| `Remotion.Linq` internals | dependency boundary | proven to run, still warning-producing |

## Follow-Up Work

These are not optional if DataLinq wants a strong public AOT/WASM claim later:

1. Split Roslyn dependencies out of the runtime package so browser and trimmed apps do not carry compiler assemblies.
2. Replace, isolate, or deeply audit the Remotion.Linq dependency so Native AOT and trim publishes stop producing third-party warnings.
3. Investigate SQLitePCLRaw `WASM0001` varargs warnings and either avoid the affected exports or document why the linked symbols are unreachable for DataLinq's path.
4. Add an automated WASM AOT browser smoke job if CI has the WebAssembly workload and browser runtime available.
5. Keep no-AOT WebAssembly unsupported for the SQLite/DataLinq path until the Mono interpreter failures are gone.
6. Decide whether generated metadata should become mandatory for all public runtime entry points in a future breaking release.
