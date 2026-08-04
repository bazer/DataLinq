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

The preview intentionally supports only its capability-gated subset. The current profile contains exactly 53 capability tokens:

- root entity scans;
- exact direct non-nullable converter-free model/provider `Int32` column/scalar equality, inequality, and relational comparison (`==`, `!=`, `<`, `<=`, `>`, and `>=`) in either operand order;
- positive and negated membership of an exact direct non-nullable converter-free model/provider `Int32` column in an invocation-local, null-free `Int32` sequence, including empty sequences (`Contains` and equivalent equality-shaped local `Any` expressions);
- exact non-null direct `Guid` and resolved Guid-backed typed-ID column/scalar equality and inequality (`==` and `!=`) in either operand order;
- nested Boolean composition with `&&`, `||`, and `!` over only those admitted comparison and membership leaves, with left-to-right row-time short-circuiting;
- one ascending or descending ordering over a direct, non-null, converter-free `Int32` single-column primary key, optionally followed by either one final `Take` or one final `Skip`, but not both;
- a final direct, non-null, converter-free `Int32` scalar projection; and
- selectorless `Any` and `Count` over admitted entity or scalar sequences;
- `Single` and `SingleOrDefault` over an admitted unpaged root entity or direct-`Int32` scalar sequence, optionally using the admitted predicates and exact primary-key ordering.

The shared expression parser normalizes a captured `null` local collection reference to an empty local sequence. Memory therefore evaluates positive membership as false and negated membership as true in that case, matching the existing neutral-plan/SQL behavior rather than LINQ-to-Objects' null-source exception.

An admitted `Skip` buffers every matching canonical row, applies the exact primary-key order, and only then selects the suffix. `Skip(0)` therefore still scans and orders all matches; exact-cardinality and over-cardinality counts return an empty sequence without materializing skipped entities. The count is frozen when the query object is constructed, matching the existing `Take` contract.

`Single` and `SingleOrDefault` establish cardinality over canonical rows before entity materialization or scalar conversion. Empty and multiple-match results therefore perform no entity or cache work, and an unordered multiple-match probe stops at the second matching row. A cold successful entity result materializes once; a warm result reuses the cached identity. Scalar results never materialize or cache entities. `Single` throws the standard `InvalidOperationException` for empty or multiple results. `SingleOrDefault` returns `null` for an empty entity result or `0` for an empty `Int32` scalar result, and throws the standard `InvalidOperationException` for multiple results.

Unsupported query shapes fail before memory row work. Nearby unsupported shapes include nullable comparison operands or null scalar bindings, strings, widened or boxed numerics, column-to-column comparisons, typed-ID member unwrapping, relational comparisons outside the exact direct `Int32` fence, membership with nullable element types or null elements or with string, widened, boxed, converter-backed, `Guid`, or typed-ID values, standalone Boolean constants, Boolean columns or functions, any Boolean tree containing an unsupported leaf, bare or unordered `Skip`, negative or non-exact `Skip` counts, repeated `Skip`, `Skip` combined with `Take`, post-`Skip` composition, `ThenBy`, `First`, `FirstOrDefault`, `Last`, `LastOrDefault`, `Single` or `SingleOrDefault` after `Take` or `Skip`, anonymous projections, joins, relation navigation, and grouping. A terminal after paging rejects as `Operation:Pushdown`. Public lookup is limited to an exact single-column primary key; composite lookup is not supported. `MemoryDatabase<TDatabase>` and the public neutral read-source contract expose no post-seed insert/update/delete, transaction, connection, provider, command, or raw-SQL service, and the preview does not support durability, persistence, arbitrary projections, or general LINQ parity. Generated model types remain eligible for shared core SQL-only APIs: `Get(...)` is static, and transaction-taking mutation uses shared extensions; neither is a Memory operation. The legacy inherited `GetDataSource()` member and parameterless `Delete()` extension reject before provider or backend work. No generated lookup overload has a source parameter typed as `MemoryDatabase<TDatabase>` or `IDataLinqReadSource` alone; the existing overloads require the SQL-capable `IDataSourceAccess` contract.

This checkpoint advances only bounded M1-F exact `Single`/`SingleOrDefault` over the existing one-root query island. It does not complete M1; M1 as a whole and M2 remain open.

The memory store keeps canonical provider values but never applies SQL wire/storage codecs. Comparisons against SQL providers are development evidence only and are not part of this package's supported contract.
