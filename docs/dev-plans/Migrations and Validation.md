# Specification: Database Validation & Migrations

**Status:** Draft
**Goal:** Provide tools to ensure the C# Model (`DatabaseDefinition`) stays synchronized with the physical Database Schema. The approach prioritizes safety and transparency over "magic" automation.

---

## 1. Phase 1: Schema Validation (Drift Detection)

Before we can modify the database, we must be able to accurately detect differences between the Code and the Database.

### 1.1. The `SchemaComparer`
We will implement a service that takes two `DatabaseDefinition` objects (one from Code, one from the DB Provider) and produces a **Diff Report**.

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
*   **Runtime:** `database.ValidateSchema()` throws an exception on startup if drift is detected (configurable).
*   **CLI:** `datalinq validate` prints a human-readable report of differences.

---

## 2. Phase 2: Diff Script Generation (The "Fix It" Script)

Once differences are identified, we need to generate SQL to resolve them. This is **Stateless Diffing** (comparing current state A to current state B without knowing the history).

### 2.1. `ISqlDiffGenerator`
Each provider (MySQL, SQLite) must implement logic to generate `ALTER` statements.

*   **Additions:** `ALTER TABLE X ADD COLUMN Y`.
*   **Modifications:** `ALTER TABLE X MODIFY COLUMN Y` (MySQL) or specialized SQLite table-rebuild logic (since SQLite has limited ALTER support).
*   **Destructive Actions:** `DROP COLUMN` / `DROP TABLE`.
    *   *Safety Rule:* Destructive actions are commented out by default in the generated script with a `WARNING: DATA LOSS` tag.

### 2.2. CLI Command: `datalinq diff`
*   **Action:** Generates a SQL script (e.g., `update_schema.sql`).
*   **Philosophy:** The tool suggests the SQL; the developer reviews and executes it. We do not auto-apply structural changes at runtime by default.

---

## 3. Phase 3: Snapshot-Based Migrations (The "Lock File")

Stateless diffing has a flaw: it cannot detect **Renames**.
*   *Scenario:* Rename `User.Name` to `User.FullName`.
*   *Stateless View:* "Drop column `Name`. Add column `FullName`." (Data Loss).
*   *Desired View:* `RENAME COLUMN Name TO FullName`.

To solve this, we need a **Model Snapshot**.

### 3.1. The Snapshot File (`datalinq.snapshot.json`)
This file persists the `DatabaseDefinition` state of the *last successful migration*.

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

To ensure robust validation, we must support parsing and diffing these specific features:

*   [ ] **Check Constraints:** Parse `CHECK (...)` from SQL and validate against model attributes.
*   [ ] **Comments/Descriptions:** Sync C# XML Summaries or `[Description]` attributes with DB Column Comments.
*   [ ] **Collations:** Validate character set/collation compatibility.
*   [ ] **JSON Columns:** Ensure simple `string` properties correctly map to `JSON` DB types where valid.

---

## 6. Implementation Steps

1.  [ ] **Core:** Implement `SchemaComparer` logic (leveraging `IEquatable` from Metadata Architecture).
2.  [ ] **CLI:** Implement `datalinq validate` command.
3.  [ ] **Providers:** Implement `ISqlDiffGenerator` for MySQL.
4.  [ ] **Providers:** Implement `ISqlDiffGenerator` for SQLite (including table-rebuild logic for complex alters).
5.  [ ] **Future:** Design the Snapshot JSON format and Migration class structure.