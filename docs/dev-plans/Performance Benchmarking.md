# Specification: Performance Benchmarking & Regression Testing

**Status:** Draft
**Goal:** Establish a rigorous, scientific benchmarking suite to quantify DataLinq's performance characteristics. The goal is to prove "Zero Allocation on Cache Hits" and identifying regressions in the hot path.

---

## 1. Philosophy & Metrics

DataLinq is an opinionated ORM. We trade **Memory Usage** (Caching) for **Read Speed** (CPU/Allocations). Therefore, our benchmarks must measure these specific trade-offs.

### 1.1. Key Metrics
1.  **Allocated Memory (Bytes/Op):** The most critical metric. On a cache hit, this should be **0 B**.
2.  **Gen 0/1/2 Collections:** High frequency of Gen 0 collections indicates "garbage" generation (strings, enumerators) that hurts throughput.
3.  **Mean Execution Time:** Raw speed.
4.  **Startup Time:** The "Cold Start" penalty (Metadata reflection, JIT).

### 1.2. The "Gladiator Arena" (Comparison Targets)
We benchmark against the industry standards to understand our position.
*   **Dapper:** The baseline for raw ADO.NET mapping speed.
*   **EF Core (NoTracking):** The standard for LINQ-based ORMs.
*   **EF Core (Tracking):** To demonstrate the overhead of change tracking vs DataLinq's Immutability.

---

## 2. Architecture: `src/DataLinq.Benchmarks`

We will separate benchmarks from unit tests. Benchmarks require Release builds and no debugger attachment.

### 2.1. Project Structure
```text
src/DataLinq.Benchmarks/
├── Config.cs               # BenchmarkDotNet configurations (MemoryDiagnoser, Exporters)
├── Program.cs              # Runner
├── Utils/
│   └── BenchmarkContext.cs # Setup logic (Docker spin-up, Seeding)
├── Scenarios/              # The actual tests
│   ├── Micro/
│   │   ├── KeyGeneration.cs
│   │   └── Materialization.cs
│   └── Macro/
│       ├── SingleFetch.cs
│       ├── RelationTraversal.cs (N+1)
│       └── BatchInsert.cs
└── Competitors/            # Adapter classes for other ORMs
    ├── EfCoreBenchmarkContext.cs
    └── DapperBenchmarkContext.cs
```

### 2.2. Data Strategy
Benchmarks must use **Deterministic Data** to be reproducible.
*   We will leverage the `DataLinq.Seeder` logic (Bogus with fixed seed) to populate a local SQLite file or In-Memory provider.
*   **Note:** Benchmarking against a real network database (MySQL/Docker) mostly tests *network latency*. For architectural optimization, **In-Memory** or **SQLite (File)** is preferred to expose CPU bottlenecks.

---

## 3. Benchmark Scenarios

### 3.1. Hot Path Micro-Benchmarks
These test internal components in isolation.
*   **`KeyFactory.Create`:** Creating `IntKey` vs `CompositeKey`.
    *   *Goal:* Verify struct-based keys allocate 0 bytes.
*   **`RowData` Access:** Reading a value from the new `RowData` array implementation vs Dictionary.
    *   *Goal:* Verify O(1) access speed.
*   **LINQ Translation:** Parsing a `Where` expression via Remotion.
    *   *Goal:* Measure the overhead of the parser before SQL generation.

### 3.2. Macro Scenarios (The User Experience)

#### Scenario A: The "Cache Hit" (DataLinq's Strongest Suit)
Fetch a record by PK that has already been loaded.
*   **DataLinq:** `db.Employees.Get(1)` (Should be instant, 0 allocations).
*   **EF Core:** `db.Employees.Find(1)` (Local view check).
*   **Dapper:** Impossible (Always hits DB).

#### Scenario B: The "N+1" Traversal
Fetch 100 Employees and access `employee.Department.Name`.
*   **DataLinq:** Accessing property triggers lazy load (or cache hit).
*   **EF Core:** `.Include(x => x.Department)`.
*   **Dapper:** Manual Multi-Mapping.
*   *Goal:* Prove that DataLinq's "Relation Cache" creates a graph traversal speed comparable to in-memory object navigation.

#### Scenario C: Cold Start & Metadata Loading
Measure the time from `Process.Start` to `First Query Result`.
*   *Goal:* This tracks the improvement from the "Source Generator Optimizations" (Static Metadata).

---

## 4. Regression Testing Strategy

Benchmarking is useless if nobody looks at it.

### 4.1. Historical Baseline
Since we cannot easily reference "DataLinq v0.5.dll" and "DataLinq v0.6.dll" in the same project:
1.  We commit benchmark results (JSON/Markdown) to `benchmarks/history/`.
2.  We treat these files as the "Baseline."

### 4.2. GitHub Actions Workflow
On every PR to `main` that touches `src/DataLinq`:
1.  Build `DataLinq.Benchmarks` in **Release** mode.
2.  Run the **Hot Path** benchmarks (fast execution).
3.  Compare results to the stored Baseline.
4.  **Fail (or Warn)** if Allocations increase > 5% or Speed decreases > 10%.

---

## 5. Implementation Steps

1.  [ ] Create `src/DataLinq.Benchmarks` project.
2.  [ ] Install `BenchmarkDotNet`.
3.  [ ] Implement `Competitors/EfCoreContext` mirroring `EmployeesDb`.
4.  [ ] Implement **Scenario A (Cache Hit)** and **Scenario B (Traversal)**.
5.  [ ] Create a script (`run-benchmarks.ps1`) to execute and export reports.