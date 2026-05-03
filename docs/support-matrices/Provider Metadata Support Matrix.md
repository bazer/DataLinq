> [!NOTE]
> This matrix is the current metadata-fidelity support boundary. It is not an exhaustive DDL compatibility promise. If a provider feature is not listed as supported and backed by tests, treat it as unsupported.
# Provider Metadata Support Matrix

**Status:** Current provider metadata matrix; moved from the Phase 4 roadmap folder after the Phase 5 validation boundary consumed it.

This matrix records the metadata subset DataLinq preserves through provider roundtrips for MySQL, MariaDB, and SQLite, plus the provider details that remain outside the current contract.

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
| Tables | Supported | Supported | Supported | Ordinary table read/write roundtrips are covered by provider metadata suites. |
| Views | Partially supported | Partially supported | Partially supported | View definitions are read and generated, and validation now compares view presence, table/view type, and columns. Full view-definition normalization remains deferred. |
| Column names | Supported | Supported | Supported | Ordinary names and quoted identifiers with spaces, punctuation, C# keyword-shaped names, and leading digits roundtrip through provider metadata, generated attributes, and generated SQL. Original database names remain canonical. |
| Column order | Supported | Supported | Supported | MySQL/MariaDB import columns by `ORDINAL_POSITION`; SQLite uses `pragma_table_info` order. Roundtrip comparison asserts ordered column sequences. |
| Nullability | Supported | Supported | Supported | Ordinary nullable/non-nullable columns roundtrip. Generated columns are intentionally skipped instead of treated as ordinary nullable columns. |
| Primary keys | Supported | Supported | Supported | Single and composite primary keys are represented and ordered. |
| Autoincrement | Supported | Supported | Supported | MySQL/MariaDB `AUTO_INCREMENT` and the tested SQLite `INTEGER PRIMARY KEY AUTOINCREMENT` shape roundtrip. |
| Defaults | Supported | Supported | Partially supported | MySQL/MariaDB preserve typed literals, current date/time/timestamp aliases, UUID defaults, and raw expression defaults through provider-scoped `[DefaultSql]`. SQLite preserves typed literals and current date/time/timestamp defaults, while unsupported expression/blob defaults still warn and skip. |
| Simple indexes | Supported | Supported | Supported | Ordinary named indexes preserve ordered database column names through metadata, generated model attributes, generated SQL, and provider re-read. |
| Unique indexes | Supported | Supported | Supported | Named unique indexes preserve ordered database column names. SQLite emits named unique indexes instead of anonymous table constraints so provider re-read keeps stable names. |
| Composite indexes | Supported | Supported | Supported | Metadata carries ordered columns, and generated model attributes are class-level for composite indexes. Advanced provider-specific index options remain unsupported. |
| Foreign keys | Supported | Supported | Supported | Single-column and composite key identity roundtrip through ordered relation metadata and generated SQL. `ON UPDATE` and `ON DELETE` actions are represented for provider-supported actions. |
| Primary-key columns that are also foreign keys | Supported | Supported | Supported | Covered by provider metadata and generator regression tests. |
| Multiple foreign keys to the same table | Supported | Supported | Supported | Metadata import, generated relation names, and transaction-scoped runtime relation loading are covered for duplicate same-target FKs. |
| Composite foreign keys | Supported | Supported | Supported | Participating `[ForeignKey]` attributes are grouped by constraint into one ordered `RelationDefinition`, generated relation attributes preserve ordered column arrays, and provider SQL emits one composite `FOREIGN KEY`. |
| Relation property names | Supported | Supported | Supported | Duplicate same-target candidate-side names derive from semantic constraint names when providers expose them, with column-name fallback for provider ordinals such as SQLite FK ids. Composite foreign-key relation names are stable for the supported grouping shape. |
| Check constraints | Supported | Supported | Unsupported | MySQL/MariaDB check clauses import into raw provider-specific `[Check(DatabaseType, name, expression)]` model attributes, emit back to provider SQL, and roundtrip through the supported-subset comparison. Structured, provider-neutral check metadata is deferred. SQLite check parsing remains intentionally unsupported. |
| Table comments | Supported | Supported | Unsupported | MySQL/MariaDB comments import into `[Comment]` model attributes, generated C#, generated SQL `COMMENT` table options, and provider roundtrip comparison. SQLite has no native table comments. |
| Column comments | Supported | Supported | Unsupported | MySQL/MariaDB comments import into `[Comment]` property attributes, generated C#, generated SQL column `COMMENT` clauses, and provider roundtrip comparison. SQLite has no native column comments. |
| Identifier casing comparison | Partially supported | Partially supported | Partially supported | Roundtrip comparison is exact because it compares provider-reported metadata snapshots. Validation must add provider-aware matching: SQLite preserves declaration text but resolves ordinary identifiers case-insensitively; MySQL/MariaDB table-name casing depends on server settings such as `lower_case_table_names`, while column and index names should be treated case-insensitively. |
| Expression indexes | Unsupported | Unsupported | Unsupported | Outside the current metadata contract. SQLite detects expression indexes with `pragma_index_xinfo`, warns, and skips them. MySQL expression index rows are skipped when `information_schema.STATISTICS` exposes them as expressions. |
| Partial indexes | Unsupported | Unsupported | Unsupported | Outside the current metadata contract. SQLite detects partial indexes, warns, and skips them. |
| Descending index parts | Unsupported | Unsupported | Unsupported | Provider-specific index ordering is not represented. SQLite, MySQL, and MariaDB detect descending parts where metadata exposes them, warn, and skip the index. |
| Prefix-length index parts | Unsupported | Unsupported | Unsupported | MySQL/MariaDB-specific detail is not represented. Prefix-length indexes warn and skip instead of importing as ordinary indexes. |
| Invisible or ignored indexes | Unsupported | Unsupported | Unsupported | Provider-specific detail is not represented. MySQL invisible indexes and MariaDB ignored indexes warn and skip when metadata exposes them. |
| Generated/computed columns | Unsupported | Unsupported | Unsupported | No first-class read-only/generated column shape exists yet. MySQL/MariaDB generated columns warn and skip instead of becoming mutable value properties. |
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
- `src/DataLinq.SharedCore/Attributes/CheckAttribute.cs`
- `src/DataLinq.SharedCore/Attributes/CommentAttribute.cs`
- `src/DataLinq.SharedCore/Attributes/DefaultAttribute.cs`
- `src/DataLinq.SharedCore/Attributes/ForeignKeyAttribute.cs`
- `src/DataLinq.SharedCore/Factories/MetadataFactory.cs`
- `src/DataLinq.SharedCore/Factories/Models/ModelFileFactory.cs`

Initial provider tests:

- `src/DataLinq.Tests.Unit/SQLite/MetadataFromSQLiteFactoryTests.cs`
- `src/DataLinq.Tests.Unit/SQLite/MetadataRoundtripTests.cs`
- `src/DataLinq.Tests.Unit/Core/SyntaxParserTests.cs`
- `src/DataLinq.Tests.MySql/MetadataFromServerFactoryTests.cs`
- `src/DataLinq.Tests.MySql/ProviderMetadataRoundtripTests.cs`

## Phase 4 Closeout Status

Phase 4 moved these entries from partially supported or unknown toward supported only where roundtrip tests prove them:

- column names with spaces, punctuation, reserved C# word shapes, and leading digits
- foreign-key columns that also have ordinary indexes
- primary-key columns that are also foreign keys
- composite foreign-key grouping and naming
- simple, unique, and composite indexes for the ordinary ordered-column subset
- broader deterministic relation property naming beyond the duplicate same-target cases now covered
- MySQL/MariaDB raw check expressions and comments as provider-scoped metadata
- unsupported advanced index, collation, generated-column, and referential-action details are documented rather than treated as comparable validation facts

Identifier casing comparison is documented here as provider behavior and consumed in Phase 5 as validation semantics. SQLite table and column presence checks are case-insensitive. MySQL/MariaDB column matching is case-insensitive, while table-name casing still depends on provider/server configuration such as `lower_case_table_names`.

This matrix should continue to be updated when DataLinq adds first-class metadata for deferred provider features. Until then, unsupported details should stay out of authoritative schema validation.

## Phase 4B Closeout Status

Phase 4B hardened the practical gaps found in the Phase 4 review:

- foreign-key referential actions are represented on `[ForeignKey]` and `RelationDefinition`
- generated provider SQL preserves explicit `ON UPDATE` and `ON DELETE` actions
- validation and roundtrip comparison include referential actions
- MySQL/MariaDB column import uses provider ordinal positions
- MySQL/MariaDB raw expression defaults preserve provider SQL through `[DefaultSql(DatabaseType, expression)]`
- raw provider defaults are not assigned as fake C# constructor defaults
- MySQL/MariaDB generated columns warn and skip
- MySQL/MariaDB prefix-length indexes warn and skip, with additional guardrails for other lossy index shapes where metadata exposes them
- schema validation now includes views for presence, table/view type, and columns
