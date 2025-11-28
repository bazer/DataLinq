# Specification: Metadata Architecture, Incremental Build & Diagnostics

**Status:** Draft
**Goal:** Refactor the metadata engine to support four critical requirements:
1.  **Null Safety:** Eliminate circular nullability warnings by separating construction from definition.
2.  **Incremental Caching:** Implement deep structural equality to prevent the Source Generator from running unnecessarily.
3.  **Optimization Prep:** Calculate and freeze column indices (for dense arrays) during the build phase.
4.  **Rich Diagnostics:** Capture Source Locations (`Microsoft.CodeAnalysis.Location`) to enable clickable error messages in the IDE.

---

## 1. The Core Pattern: Builder vs. Definition

We will separate the *mutable* construction logic from the *immutable* runtime representation.

### 1.1. The Builders (Transient, Mutable)
Builders are used by Parsers (SQL, Roslyn) to assemble the schema. They allow string-based references and hold **Source Locations** for error reporting.

```csharp
// Must reference Microsoft.CodeAnalysis for Location
public class TableBuilder
{
    public string Name { get; set; }
    public Location? Location { get; set; } // Points to the class definition
    public List<ColumnBuilder> Columns { get; } = new();
    
    // Fluent API
    public TableBuilder AddColumn(string name, Location? location = null);
}

public class ColumnBuilder
{
    public string Name { get; set; }
    public Location? Location { get; set; } // Points to the property definition
    
    // Deferred Relation Definition (String-based)
    public string? ForeignKeyTable { get; set; } 
    public string? ForeignKeyColumn { get; set; }
}
```

### 1.2. The Definitions (Persistent, Immutable)
These are the classes used by the ORM runtime (`Database<T>`). They enforce the graph structure and implement value equality.

*   **Properties:** Read-only (`get;`).
*   **Collections:** `ImmutableArray<T>` or `ReadOnlyList<T>`.
*   **Equality:** Implements `IEquatable<T>` for structural comparison.
*   **Diagnostics:** Stores `Location` for deferred validation errors (e.g. "Table not found").

---

## 2. Solving Nullability (The Circular Dependency)

To allow `Table.Database` and `Database.Tables` to both be non-null and immutable, we use a "Constructor + Freeze" pattern inside the `Build()` method.

**The Construction Flow:**
1.  **Instantiate:** Create all `TableDefinition` and `ColumnDefinition` objects.
    *   *Trick:* Pass the parent `DatabaseDefinition` into the `TableDefinition` constructor.
2.  **Populate:** Fill simple properties (Name, Type) and the `Location`.
3.  **Resolve:** Link object references.
    *   Match `ColumnBuilder.ForeignKeyTable` string -> `TableDefinition` instance.
    *   *Validation:* If a target table is not found, use `ColumnBuilder.Location` to report the error exactly where the user defined the FK.
4.  **Index:** Assign integer indices to columns (0, 1, 2...) for the Memory Optimization features (Dense Arrays).
5.  **Freeze:** Return the root `DatabaseDefinition`.

---

## 3. Structural Equality (The Source Generator Fix)

To stop the Roslyn CPU burn, the `Definition` classes must implement **Value Equality**, not Reference Equality.

### 3.1. The Equality Contract
**Critical:** Equality must **IGNORE** the `Location` property.
*   *Scenario:* User adds a newline at the top of a file.
*   *Effect:* All `Location` properties change (different line numbers).
*   *Desired Behavior:* The logical schema is identical. Equality should return `true` so code generation is skipped.

### 3.2. Implementation Strategy
We cannot use C# `record` default equality because of circular references (StackOverflow) and the Location exclusion requirement.

**Logic for `TableDefinition.Equals(other)`:**
1.  Check `Name == other.Name`.
2.  Check `Columns.SequenceEqual(other.Columns)`.
    *   *Note:* Since `ColumnDefinition` implements Equals, this verifies the whole table structure.
3.  **Do not** check `Location`.
4.  **Do not** recursively check `Database.Equals` (infinite loop).

**Logic for `DatabaseDefinition.Equals(other)`:**
1.  Check `Name`.
2.  Check `Tables.SequenceEqual(other.Tables)`.

---

## 4. Diagnostics & Error Reporting

We must integrate Roslyn Locations into the failure pipeline to make errors clickable.

### 4.1. `IDLOptionFailure` Refactoring
Update the shared error handling class to carry the location context.

```csharp
public abstract class IDLOptionFailure
{
    public string Message { get; }
    public Location? Location { get; } // New Property
}

public static class DLOptionFailure 
{
    // Helper to capture location from SyntaxNode immediately
    public static DLOptionFailure<T> Fail<T>(string msg, SyntaxNode node) 
        => new(msg, node.GetLocation());
}
```

### 4.2. Validation Phase
Errors happen in two stages:
1.  **Parsing Phase:** (e.g., `[Table]` attribute has no arguments).
    *   *Action:* Return `DLOptionFailure` immediately using `attributeSyntax.GetLocation()`.
2.  **Build/Resolution Phase:** (e.g., Foreign Key points to non-existent table).
    *   *Action:* The `Builder` logic looks up the `ColumnBuilder.Location` stored earlier and returns a failure pointing to the specific property.

---

## 5. The Incremental Pipeline

This architecture enables a highly efficient Source Generator pipeline.

### 5.1. The Pipeline Stages
1.  **Parse (Incremental):**
    *   Input: `ClassDeclarationSyntax`.
    *   Output: `TableBuilder`.
    *   *Optimization:* This step happens in parallel. Only modified files are re-parsed.
2.  **Collect:**
    *   Input: `ImmutableArray<TableBuilder>`.
    *   Output: `ImmutableArray<TableBuilder>`.
3.  **Build (The Transformation):**
    *   Input: List of Builders.
    *   Logic: `new DatabaseBuilder(list).Build()`.
    *   Output: `DatabaseDefinition` (The Immutable Snapshot).
4.  **Compare (The Gatekeeper):**
    *   Roslyn compares `NewSnapshot.Equals(OldSnapshot)`.
    *   *Logic:* Since `Equals` ignores `Location`, formatting changes do **not** trigger regeneration.
    *   If True: **STOP.** (CPU Saved).
    *   If False: Generate Code.

---

## 6. Implementation Steps

1.  [ ] **Infrastructure:**
    *   Update `IDLOptionFailure` to support `Location`.
    *   Update `SyntaxParser` to capture `Location` from SyntaxNodes.
2.  **Create Builders:**
    *   Define `DatabaseBuilder`, `TableBuilder`, `ColumnBuilder` in `DataLinq.Metadata`.
    *   Ensure they hold `Location` properties.
3.  **Refactor Definitions:**
    *   Remove public setters (make immutable).
    *   Add `IEquatable<T>` implementation (ignoring `Location`).
    *   Add `Index` property to `ColumnDefinition` (for dense arrays).
4.  **Implement `Build()`:**
    *   Implement resolution logic for string-based Foreign Keys.
    *   Implement column sorting and index assignment.
    *   Implement error reporting using stored Locations if resolution fails.
5.  **Update Factories & Generator:**
    *   Update `MetadataFromSqlFactory` / `MetadataFromModelsFactory` to use Builders.
    *   Update `ModelGenerator.cs` to handle the new pipeline.
  
---

## 7. A few small things to check off
*   **Attributes & Comments:** *"Hämta kommentarer"* (Fetch DB comments).
    *   *Action:* Add `Description` property to `TableBuilder`/`ColumnBuilder`. Extract from `information_schema` (MySQL) or `sqlite_master`. Generate `<summary>` XML docs in C#.
*   **Validation:** *"Ge kompileringsfel när Default datatyp inte stämmer..."*
    *   *Action:* Add validation logic to `MetadataBuilder.Build()`. If `DefaultValue` type doesn't match `CsType`, return a `DLOptionFailure` with `Location`.
*   **Default Value Parsing:** *"Hantera default = '0' från databasen..."*
    *   *Action:* The `MetadataFromSqlFactory` needs a smarter parser. `'0'` -> `false`, `b'0'` -> `false`, `0` -> `Enum.Value`.
*   **Nullable Directives:** *"Lägg till #nullable directiv..."*
    *   *Action:* Trivial fix in `GeneratorFileFactory`.
*   **Extension Null Checks:** *"Behövs null kontroller på extensionmetoderna"*
    *   *Action:* Add `ArgumentNullException.ThrowIfNull` in the generated extension methods.
*   **Handling Spaces:** *"Hantera kolumner med space i namnet"*
    *   *Action:* The `MetadataBuilder` must sanitize names (`My Column` -> `My_Column` for C# property), but keep the original string for the `[Column("My Column")]` attribute.