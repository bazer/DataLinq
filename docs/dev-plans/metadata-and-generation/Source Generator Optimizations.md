> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Source Generator Optimizations

**Status:** Mostly implemented across Roadmap Phase 2, Phase 8, Phase 8B, and Phase 8C. Remaining generator/runtime ideas now belong to newer focused plans such as scalar converters, generated provider-key cache cleanup, and Phase 17 query-plan work.
**Goal:** Shift the heavy lifting of object instantiation, metadata discovery, and property mapping from **Runtime** (Reflection/Dictionaries) to **Compile Time** (Source Generation). This enables instant startup, Native AOT compatibility, and O(1) property access.

For the archived fail-fast generated hook plan, see [Generated Metadata Contract and Runtime Fallback Removal](../archive/metadata-and-generation/Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md).

## Current Implementation State

Phase 2 and Phase 8 implemented the low-risk parts that paid off immediately:

- generated immutable models now expose a fast factory hook, and the checked runtime source no longer contains the old `Expression.Compile()` constructor fallback
- `RowData` stores values densely and supports indexed column access
- column indices are assigned during metadata parsing
- the incremental generator uses normalized model declarations and stronger input comparers
- generator diagnostics are stronger, including default-value compatibility diagnostics

Still not fully implemented:

- `InstanceFactory` is not gone; it still owns materialization dispatch and last-resort factory-shape guards
- generated metadata startup does not emit a single direct `BuildMetadata()` object graph; instead it emits typed metadata drafts consumed by `MetadataDefinitionFactory`
- some compatibility and tooling paths remain metadata-driven rather than generated-only
- this work improves AOT-readiness, and Phase 8 proved a generated SQLite smoke boundary, but it does not make DataLinq broadly Native AOT-safe

Phase 8B removed the stale generated-hook compatibility path and built the immutable metadata foundation. Phase 8C then split Roslyn from the runtime surface, generated complete metadata startup, used indexed generated access, and removed ordinary runtime reflection metadata discovery from the generated startup path.

---

## 1. Generated Object Factories (The "New" Operator)

### 1.1. The Problem
Older versions of this plan assumed `DataLinq.Instances.InstanceFactory` would need to stop expression-compiling immutable constructors. The current runtime source has already moved past that specific issue: generated declarations carry immutable factory delegates and missing factories throw.

The remaining factory problem is stricter but simpler:

*   **Contract:** malformed generated table declarations should fail during provider initialization, not during row materialization.
*   **Runtime shape:** `InstanceFactory` still dispatches through a delegate stored on mutable metadata.
*   **Clarity:** stale generated hooks should not remain accepted as compatibility fallbacks.

### 1.2. The Solution
The Source Generator will emit a strongly-typed factory method for every model.

**Proposed Interface:**
```csharp
public interface IModelFactory<T>
{
    T CreateImmutable(IRowData rowData, IDataSourceAccess dataSource);
}
```

**Generated Implementation (e.g., `EmployeesDb.Factories.cs`):**
```csharp
public partial class EmployeesDb
{
    private static readonly EmployeeFactory _employeeFactory = new();
    
    private class EmployeeFactory : IModelFactory<Employee>
    {
        public Employee CreateImmutable(IRowData row, IDataSourceAccess ds) 
        {
            // Direct constructor call. No reflection. No delegates.
            return new ImmutableEmployee(row, ds);
        }
    }
}
```

---

## 2. Static Metadata Bootstrapping (Startup Optimization)

### 2.1. The Problem
Currently, `MetadataFromTypeFactory` uses Reflection at startup to scan all attributes (`[Table]`, `[Column]`) to build the `DatabaseDefinition` graph.
*   **Startup Latency:** Scanning large schemas takes measurable time.
*   **Redundancy:** The Source Generator *already* parsed this data to write the classes. Re-parsing it at runtime is wasteful.

### 2.2. The Solution
The Source Generator will emit a `Metadata` property that constructs the object graph via explicit C# code.

**Generated Code:**
```csharp
public partial class EmployeesDb
{
    private static DatabaseDefinition BuildMetadata()
    {
        var db = new DatabaseDefinition("EmployeesDb");
        
        var table = new TableDefinition("employees");
        // Hardcoded column definitions
        table.Columns.Add(new ColumnDefinition("emp_no", isPk: true, autoInc: true, type: typeof(int)));
        table.Columns.Add(new ColumnDefinition("first_name", isPk: false, autoInc: false, type: typeof(string)));
        
        db.Tables.Add(table);
        return db;
    }

    // Lazy instantiation to ensure thread safety
    public static DatabaseDefinition Metadata { get; } = BuildMetadata();
}
```

*   **Benefit:** Zero startup reflection. Instant loading.

---

## 3. Indexed Property Access (The Hot Path)

### 3.1. The Problem
Currently, generated property getters look up values by Column Definition or Name:
```csharp
public override int Id => (int)rowData.GetValue(Metadata().Columns["Id"]); 
```
This involves hashing (dictionary lookup) or linear scanning on every single property access.

### 3.2. The Solution
Combined with the **Memory Optimization** plan (which converts `RowData` to an `object[]`), the Source Generator can burn in the integer index of the column.

1.  **Deterministic Sort:** The Generator must order columns deterministically (e.g., PKs first, then alphabetical).
2.  **Generated Accessor:** Use the integer index directly.

**Generated Code:**
```csharp
// ImmutableEmployee.cs

// 0 = emp_no, 1 = first_name, 2 = last_name
public override int emp_no => (int)rowData.GetValue(0)!; 
public override string first_name => (string)rowData.GetValue(1)!;
```

*   **Performance:** Property access becomes an Array Indexer operation `ary[i]`. This is as fast as raw C#.

---

## 4. Implementation Checklist

1.  **Tighten generated hook contract:**
    *   [x] Remove the stale `GetDataLinqGeneratedTableModels()` shim.
    *   Validate generated table declarations during provider initialization.
    *   Require immutable factories for all generated models and mutable types for ordinary tables.
2.  **Metadata Generation:**
    *   [x] Update the generator to emit complete typed metadata drafts through `GetDataLinqGeneratedMetadata()`.
    *   [x] Change generic generated startup to build metadata through `MetadataDefinitionFactory` instead of runtime reflection over app model metadata.
3.  **Indexed Access (Dependency: Memory Optimization Phase 1):**
    *   [x] Ensure `RowData` exposes value access by `int index`.
    *   [x] Update generated metadata to carry stable column indices.
    *   [x] Update generated immutable/mutable/relation paths to use indexed access and generated metadata handles where the runtime benefits.
