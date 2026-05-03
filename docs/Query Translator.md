## Query Translation and Execution

DataLinq's LINQ translation layer is real, but it is intentionally narrower than the whole `Query` namespace might make you assume at first glance.

The important distinction is this:

- the LINQ translator supports a tested subset of query shapes
- the lower-level SQL builder classes also exist for manual query construction

Do not look at `Join.cs`, `Insert.cs`, or `Update.cs` and conclude that LINQ `Join`, arbitrary projections, or arbitrary aggregates are therefore supported. That is not how this codebase works.

## 1. Expression Simplification and Evaluation

**Evaluator.cs**
- **Purpose:** Before translation begins, DataLinq partially evaluates the expression tree to simplify constant sub-expressions.
- **Key Components:**
  - **Nominator:** Traverses the tree to determine which nodes can be evaluated locally. Parameters are explicitly excluded so that only independent expressions are replaced.
  - **SubtreeEvaluator:** Replaces nominated subtrees with constant expressions by compiling and invoking them.
- **Outcome:** This reduces the complexity of the expression tree and ensures that only the relevant, variable-dependent parts are translated into SQL.

## 2. Queryable Interface and Integration

**Queryable.cs**
- **Role:** This class provides the entry point for LINQ queries on DataLinq. It integrates with Remotion.Linq to interpret the LINQ expression trees.
- **Mechanism:**
  - The default query parser is used to generate a `QueryModel`.
  - The `Queryable` then hands off the `QueryModel` to the custom query executor.

## 3. Query Execution via `QueryExecutor`

**QueryExecutor.cs**
- **Overview:**
  - `QueryExecutor` is responsible for turning a `QueryModel` into an executable query.
- **Steps in translation:**
  - **Extract QueryModel:**
    - The executor recursively unwraps subqueries, member access, method calls, and unary expressions until it finds the innermost source `QueryModel`.
    - Nested query models are composed inner-first: the inner model builds the base `SqlQuery<T>`, and the outer model then applies its own body clauses and result operators. This is what preserves normal fluent composition such as `Where(a).Where(b)` and `Where(a).OrderBy(...).Where(b)`.
  - **Parse body clauses:**
    - In practice, the current implementation handles `WhereClause` and `OrderByClause`.
  - **Apply result operators:**
    - `Take`, `Skip`, `First`, `Single`, and `Any` affect `LIMIT` and `OFFSET` behavior.
    - Final terminal semantics are still applied in the executor itself.
  - **Build projections:**
    - `GetSelectFunc<T>` supports the entity itself, member-access chains, and simple constructor-based anonymous projections.
    - Unsupported selector shapes fail explicitly with `NotImplementedException`.
- **Execution:**
  - The executor builds a `SqlQuery<T>`, executes it, and maps results through projection delegates.
  - For entity reads, that path integrates with DataLinq's cache-aware row materialization.

## 4. Clause Visitors

**OrderByVisitor.cs**
- Walks the expression tree for `OrderBy` clauses.
- Extracts column information from member expressions and adds ordering instructions to the SQL query.

**WhereVisitor.cs**
- Traverses the expression tree representing a `Where` clause.
- Handles comparisons, logical composition, string methods, list-based `Contains`, projected local collection extraction, and local `Any(predicate)` equality-membership cases that can become `IN` predicates.
- Unsupported methods fail with `NotImplementedException` instead of quietly degrading into nonsense.

## 5. Building WHERE Clauses

**Where.cs and WhereGroup.cs**
- **Where.cs**
  - Represents individual predicates.
  - Supports parameterized equality, inequality, `LIKE`, `IN`, and range comparisons.
- **WhereGroup.cs**
  - Groups predicates with Boolean logic.
  - Supports nesting and parenthesized combinations.
  - Works together with `WhereVisitor` to build the full WHERE tree.

## 6. SQL Query Construction

**SqlQuery.cs**
- Aggregates query pieces into a complete SQL statement.
- Handles table names, aliases, selected columns, ordering, and limits.
- Delegates clause construction to the query builder and visitors.

**Sql.cs**
- Acts as a mutable SQL builder.
- Stores parameter state and command text.
- Produces the final SQL string and parameter collection.

## 7. What Is Not Part of LINQ Translation

- `Insert.cs`, `Update.cs`, and `Delete.cs` support the write/query-builder side of the library.
- `Join.cs` supports the lower-level SQL builder.
- Those classes are useful, but they are not proof that equivalent high-level LINQ operators are implemented.

## 8. Miscellaneous Utilities

- **Literal.cs**
  - Represents literal SQL fragments when the lower-level query builder needs them.
- **QueryUtils.cs**
  - Contains helpers for parsing table names, aliases, and related query metadata.
- **OrderBy.cs**
  - Encapsulates ordering details such as column, alias, and direction.

## Summary

The translator's real job is narrow and useful:

- parse supported LINQ through Remotion.Linq
- translate `Where` and `OrderBy` plus a tested subset of result operators
- project entities, scalar members, and simple anonymous shapes
- fail loudly when the expression shape is outside the implemented surface

If you want the exact supported surface, the [Supported LINQ Queries](Supported%20LINQ%20Queries.md) page is the better source than this internals overview.
