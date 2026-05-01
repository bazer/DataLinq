> [!WARNING]
> This document is roadmap/design material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Check Constraint Metadata Design

**Status:** Future design note.

## Purpose

The first provider metadata fidelity slice should support check constraints as raw provider-specific expressions on attributes. That is the pragmatic move: it preserves the database intent without pretending DataLinq can understand every SQL expression yet.

This document records what a later first-class `CheckConstraintDefinition` model would require, so the initial attribute work does not paint us into a corner.

## Initial Phase 4 Shape

Start with a raw expression attribute.

Conceptually:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = true)]
public sealed class CheckAttribute : Attribute
{
    public CheckAttribute(string expression)
    {
        Expression = expression;
    }

    public string Expression { get; }
}
```

The exact name can be decided during implementation. `CheckAttribute` is short; `CheckConstraintAttribute` is more explicit. I slightly prefer `CheckAttribute` for model code if it stays unambiguous beside existing attributes.

The important part is not the type name. The important part is that the expression string is the canonical roundtrip payload.

Examples:

```csharp
[Check("start_date <= end_date")]
public abstract partial class Subscription { }

[Check("age >= 0")]
public abstract int Age { get; }
```

Provider readers should import check expressions verbatim where possible:

- MySQL/MariaDB: import the provider expression from the available constraint metadata.
- SQLite: import simple table/column checks only if a limited parser can safely extract them from `sqlite_schema.sql`.

Provider SQL generators should emit the expression verbatim for the provider. No cross-provider expression translation should happen in this first slice.

## Why Raw Expressions First

Check constraints are SQL expressions, not simple metadata flags.

Even boring-looking checks can contain:

- provider functions
- casts
- collation-sensitive string comparisons
- `REGEXP` or provider-specific operators
- JSON expressions
- nested parentheses
- quoted identifiers
- string literals containing parentheses or commas
- column references with provider-specific quoting

A fake structured model that only handles `Column Operator Literal` would look impressive and then fail on real schemas. Raw expressions are honest.

## First-Class Model Later

A future metadata shape could look like this:

```csharp
public sealed class CheckConstraintDefinition
{
    public string? Name { get; }
    public TableDefinition Table { get; }
    public ColumnDefinition[] Columns { get; }
    public string Expression { get; }
    public DatabaseType? ProviderExpressionDialect { get; }
    public CheckExpressionNode? ParsedExpression { get; }
}
```

That model deliberately keeps the raw expression even if parsing exists. The parsed tree is optional analysis metadata; it should not replace the original provider expression.

## Required Capabilities

To justify first-class check constraints, DataLinq would need:

- storage on `TableDefinition` for table-level checks
- storage on `ColumnDefinition` for column-level checks or a way to associate table checks with the columns they reference
- constraint names where providers expose them
- stable ordering for generated SQL and schema comparison
- provider-aware normalization so equivalent expressions do not create noisy drift reports
- parser support good enough to avoid corrupting expressions
- clear unsupported-expression diagnostics

Without those pieces, `CheckConstraintDefinition` would mostly be a string in a fancier coat.

## Parsed Expression Design

If expression parsing becomes worthwhile, use a deliberately small tree:

```csharp
abstract record CheckExpressionNode;
record CheckBinary(CheckExpressionNode Left, string Operator, CheckExpressionNode Right) : CheckExpressionNode;
record CheckUnary(string Operator, CheckExpressionNode Operand) : CheckExpressionNode;
record CheckColumnReference(string Name) : CheckExpressionNode;
record CheckLiteral(object? Value) : CheckExpressionNode;
record CheckFunctionCall(string Name, IReadOnlyList<CheckExpressionNode> Arguments) : CheckExpressionNode;
record CheckRawExpression(string Sql) : CheckExpressionNode;
```

The `CheckRawExpression` escape hatch is essential. Provider SQL expression grammars are too large to model perfectly in this project without turning DataLinq into a SQL parser library.

## Validation Semantics

Validation should compare check constraints in layers:

1. exact expression match after trivial whitespace normalization
2. provider-specific normalization for identifier quoting and redundant parentheses
3. parsed-tree comparison only for expressions DataLinq confidently understands
4. informational warning for unsupported expressions rather than a false mismatch or false match

Blunt rule: if DataLinq cannot parse an expression confidently, it should still preserve it, but it should not claim semantic equivalence.

## Migration Semantics

Diff generation for check constraints needs provider-specific behavior:

- MySQL/MariaDB can generally add and drop named constraints with `ALTER TABLE`, but version differences matter.
- SQLite usually requires table rebuilds for constraint changes, so automatic check-constraint migration should be conservative.
- Unnamed constraints are hard to diff safely. Prefer preserving provider names where available, even if the first attribute API keeps the expression as the main payload.

## Recommendation

Implement Phase 4 with raw check expression attributes and provider roundtrip tests.

Defer `CheckConstraintDefinition` until one of these becomes necessary:

- schema validation needs better check-constraint comparison than raw strings can provide
- migration generation needs provider-specific add/drop behavior
- the metadata model gains first-class constraint collections for foreign keys and unique constraints too

When that happens, keep the raw expression as the source of truth and add parsing as optional metadata, not as a lossy replacement.
