> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 4B: Provider Fidelity Hardening

**Status:** Implemented.

## Scope

This folder tracks the follow-up phase created after reviewing the Phase 4 metadata support matrix.

Phase 4 did the right first job: it made the provider metadata support boundary explicit and gave Phase 5 a validation baseline. Phase 4B is a deliberately small hardening pass over the high-value gaps that still make the matrix look weaker than the code should be:

1. preserve foreign-key referential actions
2. avoid importing advanced provider indexes as ordinary indexes
3. guard against generated/computed columns being treated as mutable columns
4. preserve raw provider default expressions where DataLinq cannot reduce them to typed C# defaults
5. include views in schema validation at the safe presence/column boundary

## Starting Stance

This is not a full DDL compatibility project.

The right move is to add first-class metadata only where DataLinq can preserve the provider detail honestly. When a provider feature is not represented, the reader should warn and skip it instead of importing a misleading half-truth.

## Current Baseline

Relevant implementation files:

- `src/DataLinq.SharedCore/Attributes/ForeignKeyAttribute.cs`
- `src/DataLinq.SharedCore/Attributes/DefaultAttribute.cs`
- `src/DataLinq.SharedCore/Metadata/RelationDefinition.cs`
- `src/DataLinq.SharedCore/Factories/MetadataFactory.cs`
- `src/DataLinq.SharedCore/Factories/Models/ModelFileFactory.cs`
- `src/DataLinq.SharedCore/Validation/SchemaComparer.cs`
- `src/DataLinq.MySql/Shared/MetadataFromSqlFactory.cs`
- `src/DataLinq.MySql/MySql/MetadataFromMySqlFactory.cs`
- `src/DataLinq.MySql/MariaDB/MetadataFromMariaDBFactory.cs`
- `src/DataLinq.SQLite/MetadataFromSQLiteFactory.cs`

Relevant tests:

- `src/DataLinq.Tests.Unit/Core/SyntaxParserTests.cs`
- `src/DataLinq.Tests.Unit/Core/SchemaComparerTests.cs`
- `src/DataLinq.Tests.Unit/SQLite/MetadataFromSQLiteFactoryTests.cs`
- `src/DataLinq.Tests.Unit/SQLite/MetadataRoundtripTests.cs`
- `src/DataLinq.Tests.MySql/MetadataFromServerFactoryTests.cs`
- `src/DataLinq.Tests.MySql/ProviderMetadataRoundtripTests.cs`

Relevant docs:

- [`../../../support-matrices/Provider Metadata Support Matrix.md`](../../../support-matrices/Provider%20Metadata%20Support%20Matrix.md)
- [`../phase-4-provider-metadata-roundtrip-fidelity/Implementation Plan.md`](../phase-4-provider-metadata-roundtrip-fidelity/Implementation%20Plan.md)
- [`../phase-5-product-trust-features/Implementation Plan.md`](../phase-5-product-trust-features/Implementation%20Plan.md)

## Documents

- `Implementation Plan.md`
