# DataLinq.Memory

`DataLinq.Memory` is an experimental, read-only in-memory backend for generated DataLinq models. It is a preview for DataLinq 0.9, not a general LINQ-to-Objects provider and not a replacement for the SQL providers.

## Package boundary

- Targets .NET 8, .NET 9, and .NET 10.
- Constructs one isolated store with `MemoryDatabase<TDatabase>`.
- Seeds generated mutable rows once per table with `Seed<TModel>(IEnumerable<Mutable<TModel>>)`. A successful seed is snapshotted and published atomically.
- Exposes the generated read-only query model through `Query()`.
- Reports invalid seed input with `MemorySeedException` and unsupported query shapes with `QueryBackendCapabilityException`.
- Depends on `DataLinq`; keep the core and Memory package versions aligned while this API is experimental.

## Current query subset

The preview intentionally supports only its capability-gated subset:

- root entity scans;
- exact non-null direct `Int32` column-to-scalar equality;
- exact non-null direct `Guid` and resolved Guid-backed typed-ID column-to-scalar equality;
- one ascending or descending ordering over a direct, non-null, converter-free `Int32` single-column primary key, optionally followed by one final `Take`;
- a final direct, non-null, converter-free `Int32` scalar projection; and
- selectorless `Any` and `Count` over admitted entity or scalar sequences.

Unsupported shapes fail before memory row work. The preview does not support mutation, transactions, durability, persistence, raw SQL, relation navigation, joins, grouping, arbitrary projections, or general LINQ parity. There is no public `Find` or generated `Get` shortcut in the preview surface.

The memory store keeps canonical provider values but never applies SQL wire/storage codecs. Comparisons against SQL providers are development evidence only and are not part of this package's supported contract.
