# Specification: Query Pipeline Abstraction

**Status:** Draft
**Goal:** Decouple the LINQ expression translation from SQL string generation. This allows DataLinq to support non-SQL backends (In-Memory, JSON, NoSQL) by treating the query logic as an abstract syntax tree (AST) that can be visited by different executors.

---

## 1. The Current Limitation

Currently, `QueryExecutor` and `QueryBuilder` are tightly coupled to `SqlQuery`, `WhereGroup`, and text-based SQL generation. Transforming a LINQ expression immediately results in a SQL string. This prevents us from executing queries against in-memory objects or other formats efficiently.

## 2. The Abstraction Layer

We will introduce an Intermediate Representation (IR) or leverage the existing `Remotion.Linq` `QueryModel` more effectively, separating the *Intent* from the *Execution*.

### 2.1. `IQueryTranslater` Interface
Each provider must implement a translator that converts the generic `QueryModel` into something it can execute.

```csharp
public interface IQueryTranslater<TResult>
{
    // For SQL: Returns SqlQuery object
    // For Memory: Returns Func<IEnumerable<T>, IEnumerable<T>>
    TResult Translate(QueryModel queryModel);
}
```

### 2.2. Refactoring `QueryBuilder`
The `QueryBuilder` logic (analyzing `MethodCallExpression`, `BinaryExpression`, etc.) is valuable and should be preserved. It needs to be refactored into a **Visitor Pattern** that emits abstract commands rather than SQL strings.

*   **Abstract Commands:** `Equals`, `Contains`, `GreaterThan`, `And`, `Or`.
*   **SqlVisitor:** Converts commands to `param = @p1`.
*   **ObjectVisitor:** Converts commands to `x => x.Prop == val`.

---

## 3. In-Memory Query Execution

For the In-Memory provider (and potentially the Cache), we need to execute these queries against C# objects.

### 3.1. Interpreter vs. Compilation
We cannot use `Expression.Compile()` for every query; the JIT overhead is too high (milliseconds per query).

**Strategy: The Interpreter**
We will implement a lightweight interpreter that traverses the abstract query tree and evaluates it against `RowData` directly.

*   **Input:** `RowData` (Array), `ColumnDefinition` (Index), Operator (`Equal`), Value.
*   **Execution:** `return row.Values[col.Index].Equals(value);`

This allows "Zero-Compilation" querying, which is essential for high-throughput in-memory lookups.

---

## 4. Provider-Specific Capabilities

Not all providers support all LINQ features (e.g., `string.Contains` might work in Memory but fail in a basic CSV provider).

We will introduce a `Capabilities` definition to the `IDatabaseProvider`. The `QueryExecutor` will check this before attempting translation, allowing fail-fast behavior for unsupported operations on specific backends.

## 5. Implementation Steps

1.  [ ] Extract `SqlQuery` generation logic out of `WhereVisitor` / `QueryBuilder`.
2.  [ ] Define the abstract `Comparison` and `Operation` nodes (this is partially done in `DataLinq.Query`).
3.  [ ] Create `SqlQueryTranslator` (moves existing logic here).
4.  [ ] Create `InMemoryQueryTranslator` (new logic).
5.  [ ] Refactor `DatabaseProvider` to expose its specific translator.