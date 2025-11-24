# Specification: Projections, Implicit Joins, and Client-Side Views

**Status:** Draft
**Goal:** Evolve DataLinq from a "Row Fetcher" into a "Shape Shifter." This specification outlines how to implement LINQ Projections (`Select`) and Joins efficiently, and how to upgrade standard Projections into persistent, reactive **Client-Side Views**.

---

## 1. Core Philosophy

In DataLinq, we reject the traditional ORM divide between "Entities" and "DTOs."
*   A **Projection** (`Select`) is just a View that is computed on-demand (Transient).
*   A **Cached Result** is a View that is computed once and maintained (Persistent).

By unifying these concepts, we can use the same Source Generation and Caching machinery for both.

---

## 2. Phase 1: Implicit Joins (The Prerequisite)

Before we can project complex shapes, we must be able to query across relationships without verbose manual `Join` syntax.

### 2.1. The `AliasManager`
The `QueryBuilder` must be upgraded to track property traversal paths.

*   **Scenario:** `db.Employees.Where(x => x.Department.Name == "IT")`
*   **Logic:**
    1.  `WhereVisitor` encounters `x.Department`.
    2.  It checks metadata: `Department` is a `RelationProperty` pointing to `Departments` table.
    3.  It asks `AliasManager`: "Do we have an alias for path `x.Department`?"
    4.  **If No:** `AliasManager` registers a new `LEFT JOIN` (or `INNER`) in the `SqlQuery` and assigns alias `t1`.
    5.  The visitor rewrites the expression to target `t1.Name`.

### 2.2. Scope
*   Support traversal in `Where` clauses.
*   Support traversal in `OrderBy` clauses.
*   Support traversal in `Select` clauses (Phase 2).

---

## 3. Phase 2: High-Performance Projections (`Select`)

We need to support transforming database rows into custom shapes without the heavy performance cost of runtime reflection or `Expression.Compile`.

### 3.1. The Problem with Anonymous Types
`Select(x => new { x.Name })` creates an anonymous type. The Source Generator cannot easily see this at compile time to generate a helper.
*   **Strategy:** We will support anonymous types via standard runtime compilation (slower, cached delegates), but we will *optimize* for **Nominal Types** (classes/records).

### 3.2. Source-Generated Materializers (`Select<T>`)
We introduce a convention-based API:
```csharp
var dtos = db.Employees.Select<EmployeeDto>().ToList();
```

**The Generator Logic:**
1.  The Source Generator detects usage of `Select<T>`.
2.  It analyzes target type `T` (e.g., `EmployeeDto`).
3.  It matches properties of `T` to the source model `Employee` (by name/type).
4.  It generates a static **Materializer**:
    ```csharp
    // Generated internal helper
    public static EmployeeDto Materialize(IDataReader r)
    {
        return new EmployeeDto(
            r.GetString(0), // Name
            r.GetString(1)  // DeptName (via implicit join)
        );
    }
    ```
5.  **Runtime:** The `QueryExecutor` uses this generated method directly. Zero reflection. Zero boxing.

### 3.3. Flattening via Implicit Joins
If `EmployeeDto` has a property `DepartmentName`, and the Source Generator sees `Employee` has a relation `Department` with property `Name`:
*   The Generator configures the `SqlQuery` to include the join.
*   The Generator maps `t1.Name` to `dto.DepartmentName`.

---

## 4. Phase 3: Client-Side Views (Result Caching)

This transforms a Projection from a "Query" into a "Live Data Structure."

### 4.1. The `IView<T>` Definition
Users define a view as a class, similar to a database model.

```csharp
public class HighValueOrderView : IClientView<OrderDto>
{
    public IQueryable<OrderDto> Define(Database<ShopDb> db)
    {
        return db.Orders
            .Where(o => o.Total > 1000)
            .Select<OrderDto>(); // Uses the optimized projection
    }
}
```

### 4.2. Storage & Lifecycle
When this view is registered/accessed:
1.  DataLinq executes the query **once**.
2.  The results (`ImmutableList<OrderDto>`) are stored in the **Global Cache**.
3.  **Dependency Tracking:** DataLinq records that `HighValueOrderView` depends on `Table:Orders`.

### 4.3. Reactive Invalidation
When a transaction commits changes to `Orders`:
1.  The `TableCache` for `Orders` updates.
2.  It notifies the dependency graph.
3.  `HighValueOrderView` is marked **Stale**.
4.  **Policy Options:**
    *   *Lazy:* Recompute next time it is accessed.
    *   *Eager:* Recompute immediately in the background (keeping the old version available for lock-free reads until finished).

### 4.4. Integration with In-Memory DB
If the **In-Memory Provider** is active:
*   The View is literally just another `ImmutableDictionary` in RAM.
*   Queries against the View (`db.Views.HighValueOrders.Where(...)`) are executed purely in memory, with no SQL generation.

## 5. Implementation Steps

1.  [ ] **Implement `AliasManager`:** Refactor `QueryBuilder` to handle path-based alias tracking for Implicit Joins.
2.  [ ] **Implement `Select` Support:** Update `QueryExecutor` to handle projection expressions (rewriting the `SELECT` SQL part).
3.  [ ] **Source Generator Upgrade:** Detect `Select<T>` calls and generate static materializers for DTOs.
4.  [ ] **View Registry:** Create the system to register `IClientView<T>` definitions and track dependencies.
5.  [ ] **Reactive Cache:** Hook into `OnRowChanged` to invalidate Views.