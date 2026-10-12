# W4 compiled contract and package evidence

Candidate `0.10.0-w4.002bf74b` was packed from clean commit `002bf74bacbcd98c5566c7a63c788bc0641989b0` with the repository's `publish-nuget.ps1 -PackOnly`. All six public packages and their symbols passed `package-report` with valid clean runner/candidate provenance. No package was published. Core adds only DI and configuration abstractions 10.0.11 alongside its existing dependencies; provider packages gain no hosting/ASP.NET runtime dependency. The Memory-only graph retains no SQL dependency. Async LINQ remains 10.0.12 on net8/9 and absent on net10.

Pinned ApiCompat 10.0.400 captured 36 surfaces. Its raw result remains **failed with thirteen compatibility diagnostics**: their fingerprints exactly match the [accepted W3 findings](../../../docs/dev-plans/roadmap-implementation/v0.10/W3%20Compatibility%20Evidence.md#apicompat-findings). There are 981 additive/compatible findings, two locked inherited divergences and zero current-framework mismatches. This is no new W4 API break, not a blanket compatibility pass. Raw logs, metadata and diagnostics remain under the report directory.

## Verified artifacts

Paths are relative to the repository. SHA-256 identifies exact local report bytes, including machine-specific paths/timestamps.

| Artifact under `artifacts/w4-consumers/002bf74b/` | Result | SHA-256 |
| --- | --- | --- |
| `packages-clean/report.json` | Six package/symbol pairs; passed, valid provenance | `8d98924a40ac349cc7b62a4e99b08419b5a2071dc03a179cebb828c518fe0909` |
| `api-clean/report.json` | Thirteen unchanged W3 diagnostics; valid provenance | `8a0b3d03e33adf2fdf9240643799ed0b96e7f3d438c1d9cd55dbe60a50c71edc` |
| `w3-final/report.json` | Nine packed async/EF/Memory/unchanged-old-binary executions | `faccbaaad325da9ee1cd206e7019f8cb6d75e7398ed4fc28c416517896715f6f` |
| `w4-final/report.json` | Six packed session/registration/service/host executions | `477d0157bc9394eb692acffca62cd77b98451d5b294f945e7c94f92b90191878` |
| `w4-final/contract-net10.0.json` | W4 selected contract capture | `5031d3fc320a2024872f57b81e6b7bcbf2bd3a3b9f353ae60ee0db6127b3f5a0` |
| `w3-final/contract-net10.0.json` | Broader async/provider capture | `5871a46977da506158334c005e53f4d5acaab8c27bef356f6b8b26aa328aed28` |

All five candidate projects build with zero warnings/errors; all fifteen executions use actual .NET **8.0.31, 9.0.20 and 10.0.12**. Generic Host is exercised on all three; ASP.NET Core HTTP execution is **10.0.12 only**. The separate `ef-ambiguity.log` contains only expected CS0121 CountAsync errors on all three TFMs. Runtime source bindings include unchanged W3 sync/async mutation expressions and old 0.9.2 binary probes.

The candidate/runtime/generator bytes are from `002bf74b`. Contract capture and verifier fixture changes in this checkpoint were compiled against those packages, with their source hashes retained by the verifiers. Earlier failed reports are not passing evidence: the first report runner was stale, the sandbox initially blocked NuGet/profile access, and the initial capture fixtures contained two incorrect transaction namespace strings. Corrected fixture builds precede the final reports above; product bytes did not change.

## Signature reconciliation

`compiled-contract.txt` projects 29 actual core/SQLite/generated-consumer types; `mysql-contract.txt` projects four MySQL/MariaDB registration types. They retain constraints, nullable metadata, optional values, visibility, interfaces, constructors, result properties and enum values. Full library metadata remains in ApiCompat snapshots.

| Surface | Frozen boundary and execution evidence |
| --- | --- |
| Participant/owner | Session is independent of `IDataSourceAccess`, provider access and completion/disposal. The existing `Transaction<T>` is the owner. Full mutations preserve return types; owner inherits participant rights. |
| Factory | Logical `Create`; action/value synchronous callbacks and four Task callback forms; optional transaction mode/final cancellation tokens. Existing runtime admission, draining and recovery are reused. |
| Prepared execution | Four participant shapes: scalar/row and sequence, sync/async. Capture, supplied-transaction identity, deferred lifetime and active-reader behavior remain verified by controlled and all-provider tests. |
| Generated mutations | Nineteen original async forms plus twenty additive concrete-transaction/participant forms. Shared consumers bind static/named/method-group/token calls under C# 12/13/14 and nullable on/off. |
| Results | Optional string/typed reasons, nullable/no-value success, guarded invalid defaults, explicit result-aware opt-in. First transaction reason and mapped result reason remain independent. |
| Registration | Per-model/key identity, explicit default, lazy settings capture, borrowed/owned handoff and all alias/lifetime shutdown proofs. MySQL/MariaDB/SQLite expose explicit provider selection and typed settings. |
| Services | Marker, explicit constructor factory, generated typed construction bridge, owning/participating overloads and DLG005 diagnostics. Authored disposal is preserved within the documented alias restrictions. |
| Memory/fakes | Memory remains a distinct read-only provider, without SQL transaction/factory/prepared ownership. Its existing supported query/lookup, cancellation and unsupported aggregate probes pass. Separate graph doubles remain graph doubles; fake owners/DI replacements belong to W6B. |

The net9/net10 W4 projections match. Net8 differs only in the reflection-interpreted nullability of the generated `Success<T>` constructor's `Value` parameter. Executing the **same net8 bytes** under an explicitly selected .NET 10 runtime yields the net10 projection exactly (`contract-net8-under10.json`). This isolates runtime reflection interpretation; that diagnostic run is not counted as .NET 8 evidence.

## Reproduce

Follow [W4 Consumers](../../../tests/fixtures/W4%20Consumers.md), then export the .NET 10 captures:

```powershell
.\tests\fixtures\Export-W3ContractReview.ps1 -Label W4 `
    -Manifest artifacts/w4-consumers/002bf74b/w4-final/contract-net10.0.json `
    -Output test-infra/api-compatibility/w4/compiled-contract.txt
.\tests\fixtures\Export-W3ContractReview.ps1 -Label W4 `
    -Manifest artifacts/w4-consumers/002bf74b/w3-final/contract-net10.0.json `
    -TypePrefixes DataLinq.MySql.Extensions.DependencyInjection.,DataLinq.MariaDB.Extensions.DependencyInjection. `
    -Output test-infra/api-compatibility/w4/mysql-contract.txt
```
