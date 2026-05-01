> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 4: Provider Metadata Roundtrip Fidelity

**Status:** Planning.

## Scope

This folder tracks the execution plan for the fourth roadmap phase described in [Roadmap.md](../../Roadmap.md).

Phase 4 is a provider metadata fidelity gate before schema validation and migrations:

1. audit MySQL, MariaDB, and SQLite metadata support
2. define the supported roundtrip subset explicitly
3. add provider roundtrip tests for that subset
4. fix ordinary metadata holes before the validation comparer depends on them
5. document unsupported provider syntax honestly

## Starting Stance

The repo already has metadata readers and create-SQL generators for SQLite, MySQL, and MariaDB, but they do not yet form a proven roundtrip contract.

That distinction matters. Schema validation should compare the database DataLinq claims to understand, not the database DataLinq accidentally simplified.

## Documents

- `Implementation Plan.md`

## Related Plans

- [`../../providers-and-features/Provider Metadata Roundtrip Fidelity.md`](../../providers-and-features/Provider%20Metadata%20Roundtrip%20Fidelity.md)
- [`../../providers-and-features/Migrations and Validation.md`](../../providers-and-features/Migrations%20and%20Validation.md)
- [`../../Roadmap.md`](../../Roadmap.md)
