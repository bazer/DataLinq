> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 4: Provider Metadata Roundtrip Fidelity

**Status:** Implemented for the Phase 5 validation support boundary.

## Scope

This folder tracks the execution plan for the fourth roadmap phase described in [Roadmap.md](../../Roadmap.md).

Phase 4 is a provider metadata fidelity gate before schema validation and migrations:

1. audit MySQL, MariaDB, and SQLite metadata support
2. define the supported roundtrip subset explicitly
3. add provider roundtrip tests for that subset
4. fix ordinary metadata holes before the validation comparer depends on them
5. document unsupported provider syntax honestly

## Starting Stance

The repo now has metadata readers, create-SQL generators, and provider roundtrip tests for the supported SQLite, MySQL, and MariaDB schema subset. It is not a full provider DDL contract, and it should not pretend to be one.

That distinction matters. Schema validation compares the database shape DataLinq claims to understand, not every possible feature a provider can parse.

## Current Status

Done:

- provider metadata support matrix exists in the central support-matrices folder and is linked from the phase
- create-read-generate-create-read roundtrip tests exist for the supported subset
- ordinary simple, unique, and composite indexes preserve ordered database column identity
- composite foreign keys are grouped into ordered relation metadata and emitted as one provider constraint
- duplicate same-target relation names are deterministic enough for the supported cases
- quoted identifiers, spaces, punctuation, C# keyword-shaped names, and leading digits preserve database names through generated attributes and SQL
- MySQL/MariaDB checks and comments roundtrip as raw provider-scoped attributes
- SQLite advanced checks/comments and provider-specific index details are explicitly unsupported rather than guessed

Deferred:

- referential actions such as cascade/restrict/set-null
- collation and charset metadata
- generated/computed columns
- expression, partial, descending, prefix-length, and invisible indexes
- a general SQL DDL parser

## Documents

- `Implementation Plan.md`
- [`Provider Metadata Support Matrix.md`](../../../support-matrices/Provider%20Metadata%20Support%20Matrix.md)

## Related Plans

- [`../../providers-and-features/Provider Metadata Roundtrip Fidelity.md`](../../providers-and-features/Provider%20Metadata%20Roundtrip%20Fidelity.md)
- [`../../providers-and-features/Migrations and Validation.md`](../../providers-and-features/Migrations%20and%20Validation.md)
- [`../../Roadmap.md`](../../Roadmap.md)
