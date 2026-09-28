# W3 compiled contract

`compiled-contract.txt` is a generated review projection of the actual core/provider/Memory package assemblies and package-generated consumer assembly, captured on .NET 10. It includes 63 selected types. It is not a handwritten rendering of proposed signatures.

Final input: `artifacts/w3-consumers/db2b71df/final/contract-net10.0.json`, SHA-256 `2b7b0effb0b8ef40902008a60928e893d6bd84919f7a2a5bea65dce94d8fad19`. Packages `0.10.0-w3.db2b71df` and committed consumer sources were built at clean commit `db2b71dff1b12716baf48fa36df38785375a3f8d`. This final export exactly matches the original reviewed projection after line-ending normalization; historical captures remain in the compatibility evidence record.

Reproduce the JSON captures using [the W3 consumer verifier](../../../tests/fixtures/W3%20Consumers.md). Export the review projection with:

```powershell
.\tests\fixtures\Export-W3ContractReview.ps1 `
  -Manifest artifacts/w3-consumers/db2b71df/final/contract-net10.0.json `
  -Output test-infra/api-compatibility/w3/compiled-contract.txt
```

The projection retains method/type visibility, virtual/abstract/static flags, body presence, return and parameter types, parameter names/defaults, generic constraints and attributes, nullable states/attributes, constructors, properties/init modifiers, interfaces/variance and fixed enum values. It includes the explicit private default implementation of inherited `IAsyncDisposable.DisposeAsync`; public-only snapshots alone do not show that slot. Internal generated mutation forwarding is deliberately excluded. The approved protected `Mutable<T>.ExecuteGeneratedMutationAsync` is included with all five required parameters.

## Inventory reconciliation

| Inventory | Captured declarations |
| --- | --- |
| Q01-Q10 | 41 query methods: one row view, two materializers, sixteen element/Any/Count forms, ten Sum, ten Average and two generic extrema |
| Q11-Q12 | Prepared scalar `Task<TResult>` and prepared sequence `IAsyncEnumerable<TElement>`, retaining SQL source constraints |
| R01-R09 | 46 async methods on the interface, built-in relation and mock: one primitive plus 45 defaults/concrete methods; local Func predicates/selectors |
| R10-R12 | Synchronous keyed rename, covariant old reference and invariant async companion; generated required/optional navigation and concrete overrides |
| K01-K05 | Database/transaction canonical lookup, generated scalar/composite/converted typed keys on all three sources, static canonical helper, cache and Memory lookup |
| M01-M09/T01-T05 | Core model/transaction mutations; typed and untyped four-form Task callbacks; managed, standalone and root completion/disposal |
| M10/AAPI-112 | Nineteen generated mutation methods per model; exact typed local callbacks, required protected bridge and inherited constraints |
| L01-L17 | Ten low-level access methods on interface/base, async reader companion, nine fluent methods, SqlQuery selection and raw model base/overrides |
| A01-A04/A08-A11 | Probes, metadata import, journal configuration and provisioning with retained parameter names and optional final tokens |
| D01-D07 | Direct snapshot accessor, immutable context/secondary properties, exact enum values/flags, options init property and provider settings |
| C01-C02 | Existing and required options constructors on concrete/shared/base providers, including protected forwarding forms |
| X01-X12 / W5 exclusions | No extra async construction/composition family, provider capability flags, relation-query conversion, or W5 runtime-validator type |

All selected async execution methods, except parameterless disposal and the explicitly required generator bridge, end in `CancellationToken cancellationToken = default`. Counts distinguish the relation primitive from its 45 other async methods. Inherited built-in methods remain visible through their captured base types; a zero declared-method count is not evidence of an absent inherited method.

The complete package metadata remains in the 36 ApiCompat snapshots. These include pre-existing synchronous APIs and exact assembly references that the focused review projection omits. Use the [compatibility evidence record](../../../docs/dev-plans/roadmap-implementation/v0.10/W3%20Compatibility%20Evidence.md) for approved breaks and behavioral/consumer evidence.

## Runtime reflection difference

The net8/net9/net10 JSON captures differ in one interpreted nullable state: `Select<T>(SqlQuery<T> query)` reports its nested T as NotNull on .NET 8 and Nullable on .NET 9/10. Raw type/constructor metadata matches after accounting for framework assembly identities. Running the same net8 candidate bytes under an explicitly selected .NET 10 host reports Nullable. This isolates the difference to runtime reflection interpretation, not a cross-TFM source declaration change. The diagnostic roll-forward run is not counted as .NET 8 execution evidence. Both raw attributes and all three original manifests remain retained; the projection uses the .NET 10 interpretation.
