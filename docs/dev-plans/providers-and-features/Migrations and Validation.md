> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Database Validation & Migrations

**Status:** Draft specification with validation, conservative diffing, and snapshot DTO slices implemented; full migration execution remains future work.
**Goal:** Provide tools to ensure the C# Model (`DatabaseDefinition`) stays synchronized with the physical Database Schema. The approach prioritizes safety and transparency over "magic" automation.

**Roadmap placement:** Main roadmap Phase 5, after the provider metadata roundtrip fidelity phase and before AOT, async, cache, and broader capability expansion.

## Current Implementation State

The repo already has useful raw material for this plan:

- `DatabaseDefinition`, `TableDefinition`, `ColumnDefinition`, indices, relations, defaults, and source locations exist in metadata
- SQLite, MySQL, and MariaDB can read live database metadata through provider-specific `MetadataFromSqlFactory` implementations
- SQLite, MySQL, and MariaDB can generate create-table SQL from metadata through provider-specific `SqlFromMetadataFactory` implementations
- tests already cover meaningful metadata-from-server and SQL-from-metadata behavior

Phase 4 added the provider metadata support matrix and roundtrip tests. Validation should only compare metadata that the provider readers and SQL generators are known to preserve.

Implemented implementation-plan slices:

- `SchemaDifference`, severity, safety classification, and `SchemaComparer` exist for the supported metadata subset
- `datalinq validate` compares source metadata against live SQLite, MySQL, and MariaDB metadata and returns CI-oriented exit codes
- `datalinq diff` generates conservative SQL suggestion scripts for additive changes and comments out destructive or ambiguous drift
- `SchemaMigrationSnapshot` serializes a deterministic snapshot of the validation-supported metadata subset
- destructive schema changes have a first safety model in validation and diff-script output
- provider metadata support boundaries are consumed through `SchemaValidationCapabilities`

What is still missing:

- no `add-migration` command exists
- no `update-database` command exists
- no applied-migration table implementation exists
- no runtime migration API exists
- no automatic snapshot workflow exists around generated migration artifacts
- no confirmed rename operation model exists

The remaining implementation work should therefore be versioned migration execution, not more stateless drift detection. If the migration workflow cannot represent history and explicit renames, it will turn real schema evolution into guessed drop/add scripts.

---

## 1. Phase 1: Schema Validation (Drift Detection)

Before we can modify the database, we must be able to accurately detect differences between the Code and the Database.

### 1.1. The `SchemaComparer`
The implemented comparer takes two `DatabaseDefinition` objects (one from Code, one from the DB Provider) and produces a **Diff Report** for the provider-supported metadata subset.

*   **Input:** `SourceDef` (Code), `TargetDef` (Live DB).
*   **Output:** `IEnumerable<SchemaDifference>`.

```csharp
public record SchemaDifference(
    DifferenceType Type, // MissingTable, MissingColumn, TypeMismatch, IndexMismatch
    IDefinition Source,  // The Code definition
    IDefinition Target,  // The DB definition
    string Message
);
```

### 1.2. Validation Features
The comparer must handle:
*   **Table Existence:** Tables present in Code but missing in DB (and vice versa).
*   **Column Mismatches:** Type differences (e.g., `int` vs `bigint`), Nullability, AutoIncrement status.
*   **Indices & Keys:** Missing indices or mismatches in Unique constraints.
*   **Default Values:** Discrepancies in default constraints (e.g., `'0'` vs `false`).

### 1.3. Usage
*   **Runtime:** `database.ValidateSchema()` remains planned; startup validation is not implemented.
*   **CLI:** `datalinq validate` prints a human-readable report of differences, supports JSON output, and returns CI-oriented exit codes.

---

## 2. Phase 2: Diff Script Generation (The "Fix It" Script)

Once differences are identified, we need to generate SQL to resolve them. This is **Stateless Diffing** (comparing current state A to current state B without knowing the history).

### 2.1. `ISqlDiffGenerator`
The first implementation is a conservative tooling-layer generator, not a broad provider runtime abstraction. It emits executable SQL only for supported additive changes and comments out destructive or ambiguous changes.

*   **Additions:** `ALTER TABLE X ADD COLUMN Y`.
*   **Modifications:** `ALTER TABLE X MODIFY COLUMN Y` (MySQL) or specialized SQLite table-rebuild logic (since SQLite has limited ALTER support).
*   **Destructive Actions:** `DROP COLUMN` / `DROP TABLE`.
    *   *Safety Rule:* Destructive actions are commented out by default in the generated script with a `WARNING: DATA LOSS` tag.

### 2.2. CLI Command: `datalinq diff`
*   **Action:** Generates a SQL script suggestion (e.g., `update_schema.sql`).
*   **Philosophy:** The tool suggests the SQL; the developer reviews and executes it. We do not auto-apply structural changes at runtime by default.

---

## 3. Phase 3: Snapshot-Based Migrations (The "Lock File")

Stateless diffing has a flaw: it cannot detect **Renames**.
*   *Scenario:* Rename `User.Name` to `User.FullName`.
*   *Stateless View:* "Drop column `Name`. Add column `FullName`." (Data Loss).
*   *Desired View:* `RENAME COLUMN Name TO FullName`.

To solve this, we need a **Model Snapshot**.

### 3.1. The Snapshot File (`datalinq.snapshot.json`)
The implemented `SchemaMigrationSnapshot` DTO defines the versioned JSON shape. A future migration workflow should persist this as the `DatabaseDefinition` state of the *last successful migration*.

### 3.2. The Migration Workflow
1.  **Developer changes code:** Renames property.
2.  **Run `datalinq add-migration <Name>`:**
    *   Compares **Code** vs **Snapshot**.
    *   Detects the change.
    *   If a Rename is ambiguous, prompts the user: *"Did you rename Name to FullName?"*
    *   Generates a **Migration Class** (C#) or **SQL Script** with a timestamp.
    *   Updates `datalinq.snapshot.json`.

---

## 4. Phase 4: Migration Execution

Managing the application of versioned scripts.

### 4.1. The `__datalinq_migrations` Table
A system table tracking which migration IDs have been applied.

### 4.2. Execution Strategies
1.  **CLI:** `datalinq update-database` (Applies pending migrations).
2.  **Runtime:** `database.Migrate()` (Optional, for simple apps/tests).
3.  **Script:** `datalinq script` (Generates a full SQL script for DevOps pipelines).

---

## 5. Schema Features Checklist

To ensure robust validation, we must consume the provider support boundary from `Provider Metadata Roundtrip Fidelity.md` and support parsing/diffing the agreed subset of these features:

*   [x] **Check Constraints:** Validate provider-supported check metadata for MySQL/MariaDB; SQLite checks remain intentionally ignored by capability rules.
*   [x] **Comments/Descriptions:** Validate provider-supported table and column comments for MySQL/MariaDB; SQLite comments remain intentionally ignored by capability rules.
*   [ ] **Collations:** Validate character set/collation compatibility.
*   [ ] **JSON Columns:** Ensure simple `string` properties correctly map to `JSON` DB types where valid.
*   [ ] **Composite Relations:** Validate composite primary keys, composite foreign keys, and columns that are both primary keys and foreign keys.
*   [ ] **Identifier Fidelity:** Validate columns and tables with spaces, reserved words, punctuation, and provider-specific casing rules.

---

## 6. Implementation Steps

1.  [x] **Core:** Implement `SchemaComparer` logic for the supported metadata subset.
2.  [x] **CLI:** Implement `datalinq validate` command.
3.  [x] **Tools:** Implement conservative diff-script generation for MySQL/MariaDB and SQLite additive changes.
4.  [x] **Core:** Define the snapshot JSON format and migration-history contract.
5.  [ ] **Future:** Implement `add-migration`, explicit rename operations, migration artifacts, and applied-migration tracking.
