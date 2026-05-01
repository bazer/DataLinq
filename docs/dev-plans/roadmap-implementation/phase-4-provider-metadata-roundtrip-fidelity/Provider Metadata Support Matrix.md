> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Provider Metadata Support Matrix

**Status:** Initial Phase 4 skeleton.

This matrix records the metadata subset DataLinq intends to preserve through provider roundtrips for MySQL, MariaDB, and SQLite.

Support labels:

- **Supported:** implemented and covered by provider tests.
- **Partially supported:** implemented for a narrower subset, or not yet covered across all required providers.
- **Unsupported:** intentionally outside the current metadata contract.
- **Unknown:** not audited closely enough to make a claim.

Blunt rule: if a feature is not covered by tests, it should not be promoted to supported.

## Roundtrip Contract

The Phase 4 roundtrip contract is:

1. create a provider schema
2. read it into `DatabaseDefinition`
3. generate provider SQL from that metadata
4. create a fresh schema from the generated SQL
5. read the fresh schema
6. compare the supported subset only

The comparison is deliberately lower-level than the future schema validation comparer. It exists to prove provider metadata fidelity, not to report production drift.

## Current Matrix

| Feature | MySQL | MariaDB | SQLite | Notes |
| --- | --- | --- | --- | --- |
| Tables | Partially supported | Partially supported | Partially supported | Basic table read/write exists; roundtrip harness coverage starts in this phase. |
| Views | Partially supported | Partially supported | Partially supported | View definitions are read and generated, but normalization and provider edge cases are not fully proven. |
| Column names | Partially supported | Partially supported | Partially supported | Ordinary names work. Quoted identifiers, spaces, punctuation, and reserved words need conformance tests. |
| Column order | Partially supported | Partially supported | Partially supported | Metadata has stable column indexes; roundtrip comparison should assert order. |
| Nullability | Partially supported | Partially supported | Partially supported | Basic read/write exists; provider quirks around primary keys and generated columns are not fully audited. |
| Primary keys | Partially supported | Partially supported | Partially supported | Single and composite primary keys are represented; ordering needs explicit roundtrip tests. |
| Autoincrement | Partially supported | Partially supported | Partially supported | Basic support exists; SQLite detection is narrow and should stay test-driven. |
| Defaults | Partially supported | Partially supported | Partially supported | Literal and current timestamp/date/time subsets exist. Expression defaults remain unsupported unless explicitly parsed. |
| Simple indexes | Partially supported | Partially supported | Partially supported | Basic shape exists; MySQL/MariaDB currently need coverage for indexes that overlap foreign-key columns. |
| Unique indexes | Partially supported | Partially supported | Partially supported | Simple unique indexes exist; composite unique model generation needs issue #6 regression coverage. |
| Composite indexes | Partially supported | Partially supported | Partially supported | Metadata can carry ordered columns. Generated model attributes should become class-level for composite indexes. |
| Foreign keys | Partially supported | Partially supported | Partially supported | Per-column metadata exists. Constraint-level grouping is still weak. |
| Primary-key columns that are also foreign keys | Partially supported | Partially supported | Partially supported | MySQL has some regression coverage; cross-provider roundtrip coverage is required. |
| Multiple foreign keys to the same table | Unknown | Unknown | Unknown | Needs deterministic relation naming and runtime relation loading tests. |
| Composite foreign keys | Partially supported | Partially supported | Partially supported | Current per-column representation is not strong enough for validation. Constraint-level metadata is likely needed later in Phase 4. |
| Relation property names | Partially supported | Partially supported | Partially supported | Existing generated names are useful but not deterministic enough for ambiguous duplicate cases. |
| Check constraints | Unsupported | Unsupported | Unsupported | Initial implementation should preserve raw provider expressions with `DatabaseType`; structured check metadata is deferred. |
| Table comments | Unsupported | Unsupported | Unsupported | MySQL/MariaDB expose comments in information_schema, but import/generation is not yet wired. SQLite has no native table comments. |
| Column comments | Unsupported | Unsupported | Unsupported | Same stance as table comments. |
| Identifier casing comparison | Unknown | Unknown | Unknown | Validation needs provider-aware rules; this phase should document the support boundary. |
| Expression indexes | Unsupported | Unsupported | Unsupported | Outside the current metadata contract. SQLite detection may warn using `pragma_index_xinfo` later. |
| Partial indexes | Unsupported | Unsupported | Unsupported | Outside the current metadata contract. |
| Descending index parts | Unsupported | Unsupported | Unsupported | Provider-specific index options are not preserved yet. |
| Prefix-length index parts | Unsupported | Unsupported | Unsupported | MySQL/MariaDB-specific detail is not represented. |
| Invisible indexes | Unsupported | Unsupported | Unsupported | Provider-specific detail is not represented. |
| Generated/computed columns | Unsupported | Unsupported | Unsupported | No first-class metadata shape exists. |
| Collation and character set | Unsupported | Unsupported | Unsupported | No first-class column/table metadata shape exists. |
| Deferrable foreign keys | Unsupported | Unsupported | Unsupported | SQLite exposes syntax DataLinq does not represent. |

## Implementation References

Metadata readers:

- `src/DataLinq.MySql/MySql/MetadataFromMySqlFactory.cs`
- `src/DataLinq.MySql/MariaDB/MetadataFromMariaDBFactory.cs`
- `src/DataLinq.MySql/Shared/MetadataFromSqlFactory.cs`
- `src/DataLinq.SQLite/MetadataFromSQLiteFactory.cs`

SQL generators:

- `src/DataLinq.MySql/Shared/SqlFromMetadataFactory.cs`
- `src/DataLinq.SQLite/SqlFromSQLiteFactory.cs`

Shared metadata and generation:

- `src/DataLinq.SharedCore/Metadata/ColumnIndex.cs`
- `src/DataLinq.SharedCore/Metadata/RelationDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/RelationPart.cs`
- `src/DataLinq.SharedCore/Factories/MetadataFactory.cs`
- `src/DataLinq.SharedCore/Factories/Models/ModelFileFactory.cs`

Initial provider tests:

- `src/DataLinq.Tests.Unit/SQLite/MetadataFromSQLiteFactoryTests.cs`
- `src/DataLinq.Tests.MySql/MetadataFromServerFactoryTests.cs`

## First Slice Status

The first implementation slice should move these entries from partially supported or unknown toward supported only when the new roundtrip tests prove them:

- column names with spaces
- foreign-key columns that also have ordinary indexes
- primary-key columns that are also foreign keys
- multiple foreign keys to the same target table
- deterministic relation property names for duplicate relation cases
