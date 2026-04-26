# Specification: Native AOT & WebAssembly Strategy

**Status:** Draft
**Goal:** Eliminate runtime code generation (JIT) and reflection from the "Hot Path" to enable DataLinq to run efficiently in **Native AOT** environments (AWS Lambda, CLI tools) and **WebAssembly** (Blazor Wasm in the browser).

---

## 1. The Core Problem: The Interpreter Fallback

In standard .NET, `Expression.Compile()` emits IL and JIT-compiles it to machine code. It is fast.
In AOT/Wasm, **there is no JIT**. `Expression.Compile()` falls back to an **Interpreter**.

**The Impact:**
1.  **Performance:** Interpreted code is significantly slower than compiled code.
2.  **Allocations:** The interpreter allocates objects to represent stack frames and variables.

**The Hot Path:**
Currently, DataLinq uses `Expression.Compile()` in `QueryExecutor` to map **every single row** returned from the database to an object. In a Wasm app fetching 1,000 rows, this will cause perceptible lag and GC pressure.

---

## 2. Solution: Source Generated Materializers (The "Anti-Mapper")

We must move the logic of "Map `RowData` to `Employee`" from Runtime to Compile Time.

### 2.1. The `IMaterializer<T>` Interface
We define a contract for mapping a raw data row to a typed object.

```csharp
public interface IMaterializer<T>
{
    // The "Hot" method. Must be allocation-free (besides the result).
    T Materialize(IRowData row);
}
```

### 2.2. The Generator Implementation
The Source Generator will inspect the `Employee` model and emit a static materializer class.

**Current (Runtime Reflection/Expression):**
```csharp
// Slow in AOT
var func = Expression.Lambda(new ImmutableEmployee(row.GetValue("Name"), ...)).Compile();
```

**Target (Generated Code):**
```csharp
// Fast in AOT (Direct IL/Assembly)
public sealed class EmployeeMaterializer : IMaterializer<Employee>
{
    // Indices are burned in via the Metadata/Memory Optimization refactor
    public Employee Materialize(IRowData row)
    {
        return new ImmutableEmployee(
            (int)row.GetFast(0),       // Id
            (string)row.GetFast(1),    // Name
            row                        // Underlying data for lazy loading
        );
    }
}
```

### 2.3. Integration
The `Database<T>` class will hold a registry of `IMaterializer<T>` instances, populated at startup. `QueryExecutor` will use these instead of building Expression trees.

---

## 3. Solution: Generated Factories

`InstanceFactory.cs` currently uses `Expression.New(...).Compile()` to create instances. This suffers the same AOT penalty.

**Optimization:**
The Source Generator already creates the concrete `Immutable*` and `Mutable*` classes. It must also generate a factory method or class (`IModelFactory<T>`) to instantiate them without reflection.

---

## 4. The "Partial Eval" Strategy (Expression Evaluation)

DataLinq uses `Evaluator.PartialEval` to convert local variables in LINQ queries into constants (e.g., `x.Date > DateTime.Now.AddDays(-1)`). This also uses `Compile()`.

**Verdict:** **Acceptable (for now).**
*   This code runs **Once Per Query**, not Once Per Row.
*   The interpreter overhead (microseconds) is negligible compared to the database I/O latency.
*   *Future Optimization:* If profiling shows this is a bottleneck, we can implement a lightweight "Constant Folding" interpreter that walks the tree and executes basic math/member access via Reflection (which is surprisingly faster than the Expression Interpreter for simple cases).

---

## 5. WebAssembly Specifics (Browser Support)

DataLinq has a massive opportunity to replace EF Core in Blazor Wasm due to binary size.

### 5.1. SQLite in the Browser
*   **Dependency:** `Microsoft.Data.Sqlite` supports Wasm.
*   **Storage:** DataLinq must allow configuring the connection string to target the **Origin Private File System (OPFS)** (e.g., `Filename=/opfs/db.sqlite`).
*   **Threading:** Wasm is currently single-threaded (mostly). Our internal concurrency locks (`ConcurrentDictionary`) work, but `ThreadWorker` (Cache Cleanup) might need adjustment to use `System.Threading.Timer` which is more Wasm-friendly than `Task.Factory.StartNew(LongRunning)`.

### 5.2. Trimming (Binary Size)
We must ensure `DataLinq.dll` is "Trimmable".
*   **Action:** Add `<IsTrimmable>true</IsTrimmable>` to the `.csproj`.
*   **Audit:** Remove any usage of `Assembly.GetTypes()` or dynamic loading that isn't compatible with aggressive trimming.

---

## 6. Implementation Plan

1.  [ ] **Phase 1 (The Fix):** Implement `IMaterializer<T>` generation in the Source Generator.
2.  [ ] **Phase 2 (The Refactor):** Switch `QueryExecutor` to use Materializers.
3.  [ ] **Phase 3 (The Proof):** Create a sample `DataLinq.BlazorWasm` project.
    *   Target `browser-wasm`.
    *   Use SQLite.
    *   Measure payload size vs EF Core.