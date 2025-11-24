# Specification: Source Generator Optimizations

**Status:** Draft
**Goal:** Shift the heavy lifting of object instantiation, metadata discovery, and property mapping from **Runtime** (Reflection/Dictionaries) to **Compile Time** (Source Generation). This enables instant startup, Native AOT compatibility, and O(1) property access.

---

## 1. Generated Object Factories (The "New" Operator)

### 1.1. The Problem
Currently, `DataLinq.Instances.InstanceFactory` uses `System.Linq.Expressions` to compile delegates at runtime for creating `Immutable<T>` instances.
*   **Performance:** Dictionary lookups on every row read.
*   **AOT:** Runtime code generation breaks Native AOT (Ahead-of-Time) compilation.
*   **Complexity:** Hard to debug runtime expression trees.

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
public override int emp_no => (int)rowData.GetFast(0); 
public override string first_name => (string)rowData.GetFast(1);
```

*   **Performance:** Property access becomes an Array Indexer operation `ary[i]`. This is as fast as raw C#.

---

## 4. Implementation Checklist

1.  [ ] **Kill `InstanceFactory`:**
    *   Create `IModelFactory<T>` interface in SharedCore.
    *   Update Generator to emit inner Factory classes or static methods.
    *   Update `Database<T>` to use these factories instead of `InstanceFactory`.
2.  **Metadata Generation:**
    *   Update Generator to emit a `BuildMetadata()` method returning the full `DatabaseDefinition`.
    *   Change runtime to prefer this static metadata over Reflection-based loading.
3.  **Indexed Access (Dependency: Memory Optimization Phase 1):**
    *   Ensure `RowData` exposes value access by `int index`.
    *   Update Generator to calculate column indices during generation.
    *   Update `Immutable` template to emit `GetFast(int)` calls.