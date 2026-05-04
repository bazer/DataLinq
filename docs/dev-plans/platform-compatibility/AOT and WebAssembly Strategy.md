# Specification: Native AOT & WebAssembly Strategy

**Status:** Draft
**Goal:** Eliminate runtime code generation (JIT) and reflection from the "Hot Path" to enable DataLinq to run efficiently in **Native AOT** environments (AWS Lambda, CLI tools) and **WebAssembly** (Blazor Wasm in the browser).

**Roadmap placement:** Main roadmap Phase 8, after provider metadata fidelity, product-trust work, the LINQ translation coverage pass, and the LINQ feature-expansion pass, before the deeper cache/invalidation work.

**Follow-up:** Phase 8 proved the generated SQLite AOT/WASM AOT smoke path. The remaining practical work for clean warnings and reasonable payload size is tracked in [Practical AOT and Size Plan](Practical%20AOT%20and%20Size%20Plan.md).

---

## 1. The Core Problem: The Interpreter Fallback

In standard .NET, `Expression.Compile()` emits IL and JIT-compiles it to machine code. It is fast.
In AOT/Wasm, **there is no JIT**. `Expression.Compile()` falls back to an **Interpreter**.

**The Impact:**
1.  **Performance:** Interpreted code is significantly slower than compiled code.
2.  **Allocations:** The interpreter allocates objects to represent stack frames and variables.

**The Hot Path:**
Phase 2 introduced generated immutable factory hooks and metadata bootstrap hooks, so the old "everything is runtime construction" picture is no longer accurate.

The remaining problem is narrower but still real: DataLinq still has AOT-hostile fallback and query/projection paths that use `Expression.Compile()`. In AOT/Wasm, those paths fall back to the expression interpreter. In a Wasm app fetching or projecting 1,000 rows, that can still cause perceptible lag and GC pressure.

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

`InstanceFactory.cs` now prefers generated immutable factory hooks, but it still keeps expression-compiled fallback constructors for models that do not have generated hooks. Database model construction also has an expression-compiled fallback. Those fallbacks are useful for compatibility, but they are not Native AOT-friendly.

**Optimization:**
The Source Generator already creates the concrete `Immutable*` and `Mutable*` classes and emits generated immutable factory hooks. The AOT work should decide whether to:

* require generated hooks for Native AOT mode,
* replace expression-compiled fallbacks with reflection-free interpreted fallbacks where possible, or
* expose a clear diagnostic when a model cannot run in an AOT-safe configuration.

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

This document uses workstreams rather than numbered phases so it does not conflict with the main roadmap phases.

1.  [ ] **Workstream A: AOT-hostile path audit**
    *   inventory remaining `Expression.Compile()` usage
    *   classify each call site as hot-path, once-per-query, startup-only, or fallback-only
    *   decide which paths must be eliminated for the AOT/WebAssembly phase and which can stay behind explicit compatibility warnings
2.  [ ] **Workstream B: Generated materializers and selectors**
    *   implement generated materializers where row or projection materialization still depends on runtime expression compilation
    *   keep the common entity-selection path fast and reflection-free
    *   define the fallback behavior for anonymous projections and unsupported selectors
3.  [ ] **Workstream C: AOT-safe factories and diagnostics**
    *   require or prefer generated immutable/database construction hooks in AOT mode
    *   replace expression-compiled fallbacks only where the replacement is simple and defensible
    *   emit clear diagnostics when runtime generation would be required
4.  [ ] **Workstream D: Trimming and reflection audit**
    *   evaluate `<IsTrimmable>true</IsTrimmable>` only after reflection usage is annotated or removed
    *   audit `Assembly.GetTypes()`, `Type.GetMethod()`, dynamic loading, and generated hook discovery
    *   add trimming/AOT smoke coverage for representative model generation paths
5.  [ ] **Workstream E: WebAssembly proof**
    *   create a sample `DataLinq.BlazorWasm` project
    *   target `browser-wasm`
    *   use SQLite
    *   test OPFS-oriented configuration
    *   measure payload size and startup/query behavior against a small EF Core baseline
