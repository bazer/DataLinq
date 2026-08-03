# DataLinq.Memory

`DataLinq.Memory` is an experimental, read-only in-memory backend for generated DataLinq models. It is a preview for DataLinq 0.9, not a general LINQ-to-Objects provider and not a replacement for the SQL providers.

## Package boundary

- Targets .NET 8, .NET 9, and .NET 10.
- Constructs one isolated store with `MemoryDatabase<TDatabase>`.
- Seeds generated mutable rows once per table with `Seed<TModel>(IEnumerable<Mutable<TModel>>)`. A successful seed is snapshotted and published atomically.
- Finds a generated model by one non-null model-side primary-key value with `Find<TModel>(object)`. A miss returns `null`; converter-backed keys use the shared canonical-value boundary.
- Exposes the generated read-only query model through `Query()`.
- Reports invalid seed input with `MemorySeedException`, unsupported lookup shapes and non-null scalar-conversion failures while normalizing or materializing with `MemoryLookupException`, and unsupported query shapes with `QueryBackendCapabilityException`. A null lookup argument uses the standard `ArgumentNullException` contract.
- Depends on `DataLinq`; keep the core and Memory package versions aligned while this API is experimental.

## Current query subset

The preview intentionally supports only its capability-gated subset. The current profile contains exactly 33 capability tokens:

- root entity scans;
- exact non-null direct `Int32` column/scalar equality and inequality (`==` and `!=`) in either operand order;
- exact non-null direct `Guid` and resolved Guid-backed typed-ID column/scalar equality and inequality (`==` and `!=`) in either operand order;
- one ascending or descending ordering over a direct, non-null, converter-free `Int32` single-column primary key, optionally followed by one final `Take`;
- a final direct, non-null, converter-free `Int32` scalar projection; and
- selectorless `Any` and `Count` over admitted entity or scalar sequences.

Unsupported query shapes fail before memory row work. Nearby unsupported shapes include nullable operands or null bindings, strings, widened or boxed numerics, column-to-column comparisons, typed-ID member unwrapping, ordered predicates, compound boolean predicates, membership, `Skip`, `ThenBy`, element terminals, anonymous projections, joins, relation navigation, and grouping. Public lookup is limited to an exact single-column primary key; composite lookup is not supported. `MemoryDatabase<TDatabase>` and the public neutral read-source contract expose no post-seed insert/update/delete, transaction, connection, provider, command, or raw-SQL service, and the preview does not support durability, persistence, arbitrary projections, or general LINQ parity. Generated model types remain eligible for shared core SQL-only APIs: `Get(...)` is static, and transaction-taking mutation uses shared extensions; neither is a Memory operation. The legacy inherited `GetDataSource()` member and parameterless `Delete()` extension reject before provider or backend work. No generated lookup overload has a source parameter typed as `MemoryDatabase<TDatabase>` or `IDataLinqReadSource` alone; the existing overloads require the SQL-capable `IDataSourceAccess` contract.

This checkpoint advances only the bounded M1 comparison family. It does not complete M1; M1 as a whole and M2 remain open.

The memory store keeps canonical provider values but never applies SQL wire/storage codecs. Comparisons against SQL providers are development evidence only and are not part of this package's supported contract.
