using System;
using System.Collections.Generic;
using System.Threading;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Linq.Planning;
using DataLinq.Metadata;

namespace DataLinq.Memory;

/// <summary>
/// Invocation-local interpreter state for the admitted memory row-selection closure.
/// </summary>
internal sealed class MemoryRowExecutionPlan
{
    private const string ComparisonSourceName = "memory-query:comparison";
    private readonly IMemoryRowPredicate[] predicates;
    private readonly MemoryInt32PrimaryKeyOrdering? ordering;
    private readonly int? skipCount;
    private readonly int? takeCount;

    private MemoryRowExecutionPlan(
        IMemoryRowPredicate[] predicates,
        MemoryInt32PrimaryKeyOrdering? ordering,
        int? skipCount,
        int? takeCount)
    {
        this.predicates = predicates;
        this.ordering = ordering;
        this.skipCount = skipCount;
        this.takeCount = takeCount;
    }

    internal bool RequiresBufferedOrdering => ordering is not null;

    internal static MemoryRowExecutionPlan Compile(
        ValidatedQueryExecutionRequest request,
        QueryPlanSourceSlot rootSource)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rootSource);
        if (rootSource.Kind != QueryPlanSourceKind.RootTable)
            throw CapabilityInvariant("the selected row source is not a root table.");

        var operations = request.Invocation.Template.Operations;
        var predicates = new List<IMemoryRowPredicate>(operations.Count);
        MemoryInt32PrimaryKeyOrdering? ordering = null;
        int? skipCount = null;
        int? takeCount = null;
        var hasSeenSkip = false;
        var hasSeenTake = false;
        for (var index = 0; index < operations.Count; index++)
        {
            switch (operations[index])
            {
                case QueryPlanOperation.Where where:
                    if (hasSeenSkip || hasSeenTake)
                    {
                        throw CapabilityInvariant(
                            $"operation {index} applies a filter after paging.");
                    }

                    predicates.Add(CompilePredicate(
                        request.Invocation,
                        rootSource,
                        where.Predicate,
                        index,
                        request.Context.CancellationToken));
                    break;

                case QueryPlanOperation.OrderBy orderBy:
                    if (ordering is not null || hasSeenSkip || hasSeenTake)
                    {
                        throw CapabilityInvariant(
                            $"operation {index} introduces a repeated or post-paging ordering.");
                    }

                    ordering = CompileOrdering(rootSource, orderBy, index);
                    break;

                case QueryPlanOperation.Skip skip:
                    if (ordering is null || hasSeenSkip || hasSeenTake)
                    {
                        throw CapabilityInvariant(
                            $"operation {index} is not the single final Skip following one ordering admitted by this checkpoint.");
                    }

                    skipCount = ResolvePagingCount(
                        request.Invocation,
                        skip.Count,
                        QueryPlanOperationKind.Skip,
                        index);
                    hasSeenSkip = true;
                    break;

                case QueryPlanOperation.Take take:
                    if (ordering is null || hasSeenSkip || hasSeenTake)
                    {
                        throw CapabilityInvariant(
                            $"operation {index} is not the single Take following one ordering admitted by this checkpoint.");
                    }

                    takeCount = ResolvePagingCount(
                        request.Invocation,
                        take.Count,
                        QueryPlanOperationKind.Take,
                        index);
                    hasSeenTake = true;
                    break;

                default:
                    throw CapabilityInvariant(
                        $"operation {index} is not admitted by the memory entity-sequence checkpoint.");
            }
        }

        return new MemoryRowExecutionPlan(predicates.ToArray(), ordering, skipCount, takeCount);
    }

    internal IReadOnlyList<CanonicalProviderValueRow> PrepareOrderedRows(
        IReadOnlyList<CanonicalProviderValueRow> rows,
        MemoryReadSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(source);
        var currentOrdering = ordering ?? throw CapabilityInvariant(
            "buffered row preparation was requested without a validated ordering.");

        cancellationToken.ThrowIfCancellationRequested();
        if (takeCount == 0)
            return Array.Empty<CanonicalProviderValueRow>();

        var matches = new List<CanonicalProviderValueRow>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index];
            source.RecordScanRowVisited();
            cancellationToken.ThrowIfCancellationRequested();
            if (Matches(row, source, cancellationToken))
                matches.Add(row);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ordered = currentOrdering.Sort(matches, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var startIndex = skipCount ?? 0;
        if (startIndex >= ordered.Length)
            return Array.Empty<CanonicalProviderValueRow>();

        var availableCount = ordered.Length - startIndex;
        var resultCount = takeCount is { } limit
            ? Math.Min(limit, availableCount)
            : availableCount;
        if (startIndex == 0 && resultCount == ordered.Length)
            return ordered;

        var selected = new CanonicalProviderValueRow[resultCount];
        for (var index = 0; index < resultCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            selected[index] = ordered[startIndex + index];
        }

        return selected;
    }

    internal bool Matches(
        CanonicalProviderValueRow row,
        MemoryReadSource source,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < predicates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matched = predicates[index].Matches(row);
            source.RecordPredicateEvaluation(matched);
            if (!matched)
                return false;
        }

        return true;
    }

    private static IMemoryRowPredicate CompilePredicate(
        QueryPlanInvocation invocation,
        QueryPlanSourceSlot rootSource,
        QueryPlanPredicate predicate,
        int operationIndex,
        CancellationToken cancellationToken)
    {
        return predicate switch
        {
            QueryPlanPredicate.Compare comparison =>
                CompileComparison(invocation, rootSource, comparison, operationIndex),
            QueryPlanPredicate.In membership =>
                CompileMembership(
                    invocation,
                    rootSource,
                    membership,
                    operationIndex,
                    cancellationToken),
            QueryPlanPredicate.And and =>
                new MemoryAndPredicate(
                    CompilePredicateTerms(
                        invocation,
                        rootSource,
                        and.Terms,
                        operationIndex,
                        cancellationToken)),
            QueryPlanPredicate.Or or =>
                new MemoryOrPredicate(
                    CompilePredicateTerms(
                        invocation,
                        rootSource,
                        or.Terms,
                        operationIndex,
                        cancellationToken)),
            QueryPlanPredicate.Not not =>
                new MemoryNotPredicate(
                    CompilePredicate(
                        invocation,
                        rootSource,
                        not.Predicate,
                        operationIndex,
                        cancellationToken)),
            _ => throw CapabilityInvariant(
                $"operation {operationIndex} contains unsupported validated predicate '{predicate.Kind}'.")
        };
    }

    private static IMemoryRowPredicate[] CompilePredicateTerms(
        QueryPlanInvocation invocation,
        QueryPlanSourceSlot rootSource,
        IReadOnlyList<QueryPlanPredicate> terms,
        int operationIndex,
        CancellationToken cancellationToken)
    {
        var compiled = new IMemoryRowPredicate[terms.Count];
        for (var index = 0; index < terms.Count; index++)
        {
            compiled[index] = CompilePredicate(
                invocation,
                rootSource,
                terms[index],
                operationIndex,
                cancellationToken);
        }

        return compiled;
    }

    private static IMemoryRowPredicate CompileComparison(
        QueryPlanInvocation invocation,
        QueryPlanSourceSlot rootSource,
        QueryPlanPredicate.Compare comparison,
        int operationIndex)
    {
        if (comparison.Operator is not (
                QueryPlanComparisonOperator.Equal or
                QueryPlanComparisonOperator.NotEqual or
                QueryPlanComparisonOperator.GreaterThan or
                QueryPlanComparisonOperator.GreaterThanOrEqual or
                QueryPlanComparisonOperator.LessThan or
                QueryPlanComparisonOperator.LessThanOrEqual) ||
            comparison.NullSemantics != QueryPlanNullSemantics.Default)
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} uses comparison '{comparison.Operator}' with null semantics '{comparison.NullSemantics}'.");
        }

        var comparisonShape = QueryPlanComparisonShapeFacts.IsDirectNonNullableInt32ColumnAndScalar(
            comparison.Left,
            comparison.Right,
            invocation.Template.BindingDeclarations)
                ? QueryPlanComparisonShape.DirectNonNullableInt32ColumnAndScalar
                : comparison.Operator is (
                    QueryPlanComparisonOperator.Equal or
                    QueryPlanComparisonOperator.NotEqual) &&
                    QueryPlanComparisonShapeFacts.IsNonNullableCanonicalGuidColumnAndScalar(
                        comparison.Left,
                        comparison.Right,
                        invocation.Template.BindingDeclarations)
                    ? QueryPlanComparisonShape.NonNullableCanonicalGuidColumnAndScalar
                    : QueryPlanComparisonShape.DefaultNullSemantics;
        if (comparisonShape == QueryPlanComparisonShape.DefaultNullSemantics)
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} is not an exact non-nullable Int32 or canonical Guid " +
                "column-to-scalar shape admitted by a validated capability token.");
        }

        var (column, scalar, columnIsLeft) = (comparison.Left, comparison.Right) switch
        {
            (QueryPlanColumnValue leftColumn, QueryPlanScalarBindingReference rightScalar) =>
                (leftColumn, rightScalar, true),
            (QueryPlanScalarBindingReference leftScalar, QueryPlanColumnValue rightColumn) =>
                (rightColumn, leftScalar, false),
            _ => throw CapabilityInvariant(
                $"operation {operationIndex} has operands inconsistent with its validated comparison shape.")
        };

        ValidateColumn(rootSource, column, operationIndex);
        return comparisonShape switch
        {
            QueryPlanComparisonShape.DirectNonNullableInt32ColumnAndScalar =>
                new MemoryInt32ComparisonPredicate(
                    column.Column,
                    ResolveCanonicalValue<int>(invocation, column.Column, scalar, operationIndex),
                    NormalizeColumnComparisonOperator(comparison.Operator, columnIsLeft)),
            QueryPlanComparisonShape.NonNullableCanonicalGuidColumnAndScalar =>
                new MemoryGuidComparisonPredicate(
                    column.Column,
                    ResolveCanonicalValue<Guid>(invocation, column.Column, scalar, operationIndex),
                    comparison.Operator),
            _ => throw CapabilityInvariant(
                $"operation {operationIndex} has unsupported validated comparison shape '{comparisonShape}'.")
        };
    }

    private static QueryPlanComparisonOperator NormalizeColumnComparisonOperator(
        QueryPlanComparisonOperator comparisonOperator,
        bool columnIsLeft)
    {
        if (columnIsLeft)
            return comparisonOperator;

        return comparisonOperator switch
        {
            QueryPlanComparisonOperator.Equal => QueryPlanComparisonOperator.Equal,
            QueryPlanComparisonOperator.NotEqual => QueryPlanComparisonOperator.NotEqual,
            QueryPlanComparisonOperator.GreaterThan => QueryPlanComparisonOperator.LessThan,
            QueryPlanComparisonOperator.GreaterThanOrEqual => QueryPlanComparisonOperator.LessThanOrEqual,
            QueryPlanComparisonOperator.LessThan => QueryPlanComparisonOperator.GreaterThan,
            QueryPlanComparisonOperator.LessThanOrEqual => QueryPlanComparisonOperator.GreaterThanOrEqual,
            _ => throw new ArgumentOutOfRangeException(
                nameof(comparisonOperator),
                comparisonOperator,
                "Memory Int32 comparison received an unknown operator.")
        };
    }

    private static IMemoryRowPredicate CompileMembership(
        QueryPlanInvocation invocation,
        QueryPlanSourceSlot rootSource,
        QueryPlanPredicate.In membership,
        int operationIndex,
        CancellationToken cancellationToken)
    {
        if (!QueryPlanMembershipShapeFacts.IsDirectNonNullableInt32ColumnAndLocalSequence(
                membership.Item,
                membership.Sequence,
                invocation.Template.BindingDeclarations) ||
            membership.Item is not QueryPlanColumnValue column)
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} is not an exact non-nullable Int32 column/local-sequence membership shape.");
        }

        ValidateColumn(rootSource, column, operationIndex);
        if (!invocation.Values.TryGet(membership.Sequence.BindingId, out var binding) ||
            binding is not QueryPlanInvocationValue.LocalSequence sequence)
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} has no local sequence value for binding '{membership.Sequence.BindingId}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var canonicalValues = new HashSet<int>();
        for (var index = 0; index < sequence.Values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sequence.Values[index] is not int value)
            {
                throw CapabilityInvariant(
                    $"operation {operationIndex} local sequence binding '{membership.Sequence.BindingId}' " +
                    $"contains '{sequence.Values[index]?.GetType().FullName ?? "null"}' at index {index} after Int32 capability validation.");
            }

            canonicalValues.Add(value);
        }

        return new MemoryInt32MembershipPredicate(
            column.Column,
            canonicalValues,
            membership.IsNegated);
    }

    private static MemoryInt32PrimaryKeyOrdering CompileOrdering(
        QueryPlanSourceSlot rootSource,
        QueryPlanOperation.OrderBy orderBy,
        int operationIndex)
    {
        if (orderBy.Orderings.Count != 1 ||
            orderBy.Orderings[0] is not { Value: QueryPlanColumnValue column } ordering)
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} is not a single direct-column ordering.");
        }

        ValidateColumn(rootSource, column, operationIndex);
        var definition = column.Column;
        if (column.ClrType != typeof(int) ||
            definition.Nullable ||
            definition.HasScalarConverter ||
            definition.ModelClrType != typeof(int) ||
            definition.ProviderClrType != typeof(int) ||
            rootSource.Table.PrimaryKeyColumns.Count != 1 ||
            !ReferenceEquals(rootSource.Table.PrimaryKeyColumns[0], definition))
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} is not the exact non-nullable Int32 primary-key ordering " +
                "admitted by the validated capability token.");
        }

        if (ordering.Direction is not QueryPlanOrderingDirection.Ascending and
            not QueryPlanOrderingDirection.Descending)
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} has unknown ordering direction '{ordering.Direction}'.");
        }

        return new MemoryInt32PrimaryKeyOrdering(definition, ordering.Direction);
    }

    private static int ResolvePagingCount(
        QueryPlanInvocation invocation,
        QueryPlanValue count,
        QueryPlanOperationKind operationKind,
        int operationIndex)
    {
        if (count is not QueryPlanScalarBindingReference { ClrType: var countType } scalar ||
            countType != typeof(int) ||
            !invocation.Template.BindingDeclarations.TryGet(scalar.BindingId, out var declaration) ||
            declaration.Kind != QueryPlanBindingKind.Scalar ||
            declaration.ModelType != typeof(int) ||
            declaration.ProviderType != typeof(int) ||
            declaration.AllowsNull ||
            !invocation.Values.TryGet(scalar.BindingId, out var binding) ||
            binding is not QueryPlanInvocationValue.Scalar { Value: int value } ||
            value < 0)
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} is not a direct non-negative Int32 scalar-binding {operationKind}.");
        }

        return value;
    }

    private static void ValidateColumn(
        QueryPlanSourceSlot rootSource,
        QueryPlanColumnValue value,
        int operationIndex)
    {
        var column = value.Column;
        if (!ReferenceEquals(value.Source, rootSource) ||
            !ReferenceEquals(column.Table, rootSource.Table))
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} references a column outside the root entity source.");
        }
    }

    private static TCanonical ResolveCanonicalValue<TCanonical>(
        QueryPlanInvocation invocation,
        ColumnDefinition column,
        QueryPlanScalarBindingReference scalar,
        int operationIndex)
    {
        if (!invocation.Values.TryGet(scalar.BindingId, out var binding) ||
            binding is not QueryPlanInvocationValue.Scalar { Value: { } modelValue })
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} has no non-null scalar value for binding '{scalar.BindingId}'.");
        }

        try
        {
            var canonicalValue = ModelValueConverter.ToCanonicalProviderValue(
                    column,
                    modelValue,
                    ComparisonSourceName)
                ?? throw CapabilityInvariant(
                    $"operation {operationIndex} normalized a non-null binding to null.");

            return canonicalValue is TCanonical value
                ? value
                : throw CapabilityInvariant(
                    $"operation {operationIndex} normalized a binding to canonical type " +
                    $"'{canonicalValue.GetType().FullName}', expected '{typeof(TCanonical).FullName}'.");
        }
        catch (ModelValueConversionException)
        {
            throw new QueryTranslationException(
                $"Backend 'memory' could not normalize scalar binding '{scalar.BindingId}' for " +
                $"column '{column.Table.DbName}.{column.DbName}' without exposing its value.");
        }
    }

    private static InvalidOperationException CapabilityInvariant(string detail) =>
        new($"The memory capability profile admitted an invalid row-selection shape: {detail}");
}

internal interface IMemoryRowPredicate
{
    bool Matches(CanonicalProviderValueRow row);
}

internal sealed class MemoryAndPredicate : IMemoryRowPredicate
{
    private readonly IMemoryRowPredicate[] terms;

    internal MemoryAndPredicate(IMemoryRowPredicate[] terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        this.terms = terms;
    }

    public bool Matches(CanonicalProviderValueRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        for (var index = 0; index < terms.Length; index++)
        {
            if (!terms[index].Matches(row))
                return false;
        }

        return true;
    }
}

internal sealed class MemoryOrPredicate : IMemoryRowPredicate
{
    private readonly IMemoryRowPredicate[] terms;

    internal MemoryOrPredicate(IMemoryRowPredicate[] terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        this.terms = terms;
    }

    public bool Matches(CanonicalProviderValueRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        for (var index = 0; index < terms.Length; index++)
        {
            if (terms[index].Matches(row))
                return true;
        }

        return false;
    }
}

internal sealed class MemoryNotPredicate : IMemoryRowPredicate
{
    private readonly IMemoryRowPredicate predicate;

    internal MemoryNotPredicate(IMemoryRowPredicate predicate)
    {
        this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool Matches(CanonicalProviderValueRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return !predicate.Matches(row);
    }
}

internal sealed class MemoryInt32ComparisonPredicate : IMemoryRowPredicate
{
    private readonly ColumnDefinition column;
    private readonly int canonicalValue;
    private readonly QueryPlanComparisonOperator comparisonOperator;

    internal MemoryInt32ComparisonPredicate(
        ColumnDefinition column,
        int canonicalValue,
        QueryPlanComparisonOperator comparisonOperator)
    {
        this.column = column ?? throw new ArgumentNullException(nameof(column));
        this.canonicalValue = canonicalValue;
        this.comparisonOperator = comparisonOperator switch
        {
            QueryPlanComparisonOperator.Equal or
            QueryPlanComparisonOperator.NotEqual or
            QueryPlanComparisonOperator.GreaterThan or
            QueryPlanComparisonOperator.GreaterThanOrEqual or
            QueryPlanComparisonOperator.LessThan or
            QueryPlanComparisonOperator.LessThanOrEqual => comparisonOperator,
            _ => throw new ArgumentOutOfRangeException(
                nameof(comparisonOperator),
                comparisonOperator,
                "Memory Int32 comparison received an unknown operator.")
        };
    }

    public bool Matches(CanonicalProviderValueRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rowValue = row[column];
        return rowValue is int value
            ? comparisonOperator switch
            {
                QueryPlanComparisonOperator.Equal => value == canonicalValue,
                QueryPlanComparisonOperator.NotEqual => value != canonicalValue,
                QueryPlanComparisonOperator.GreaterThan => value > canonicalValue,
                QueryPlanComparisonOperator.GreaterThanOrEqual => value >= canonicalValue,
                QueryPlanComparisonOperator.LessThan => value < canonicalValue,
                QueryPlanComparisonOperator.LessThanOrEqual => value <= canonicalValue,
                _ => throw new InvalidOperationException(
                    $"Memory Int32 comparison retained unknown operator '{comparisonOperator}'.")
            }
            : throw new InvalidOperationException(
                $"Canonical memory row column '{column.Table.DbName}.{column.DbName}' contained " +
                $"'{rowValue?.GetType().FullName ?? "null"}' after Int32 capability validation.");
    }
}

internal sealed class MemoryGuidComparisonPredicate : IMemoryRowPredicate
{
    private readonly ColumnDefinition column;
    private readonly Guid canonicalValue;
    private readonly bool expectEquality;

    internal MemoryGuidComparisonPredicate(
        ColumnDefinition column,
        Guid canonicalValue,
        QueryPlanComparisonOperator comparisonOperator)
    {
        this.column = column ?? throw new ArgumentNullException(nameof(column));
        this.canonicalValue = canonicalValue;
        expectEquality = comparisonOperator switch
        {
            QueryPlanComparisonOperator.Equal => true,
            QueryPlanComparisonOperator.NotEqual => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(comparisonOperator),
                comparisonOperator,
                "Memory Guid comparison supports only equality and inequality.")
        };
    }

    public bool Matches(CanonicalProviderValueRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rowValue = row[column];
        return rowValue is Guid value
            ? (value == canonicalValue) == expectEquality
            : throw new InvalidOperationException(
                $"Canonical memory row column '{column.Table.DbName}.{column.DbName}' contained " +
                $"'{rowValue?.GetType().FullName ?? "null"}' after Guid capability validation.");
    }
}

internal sealed class MemoryInt32MembershipPredicate : IMemoryRowPredicate
{
    private readonly ColumnDefinition column;
    private readonly HashSet<int> canonicalValues;
    private readonly bool isNegated;

    internal MemoryInt32MembershipPredicate(
        ColumnDefinition column,
        HashSet<int> canonicalValues,
        bool isNegated)
    {
        this.column = column ?? throw new ArgumentNullException(nameof(column));
        this.canonicalValues = canonicalValues ?? throw new ArgumentNullException(nameof(canonicalValues));
        this.isNegated = isNegated;
    }

    public bool Matches(CanonicalProviderValueRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rowValue = row[column];
        return rowValue is int value
            ? canonicalValues.Contains(value) != isNegated
            : throw new InvalidOperationException(
                $"Canonical memory row column '{column.Table.DbName}.{column.DbName}' contained " +
                $"'{rowValue?.GetType().FullName ?? "null"}' after Int32 membership capability validation.");
    }
}

internal sealed class MemoryInt32PrimaryKeyOrdering
{
    private readonly ColumnDefinition column;
    private readonly QueryPlanOrderingDirection direction;

    internal MemoryInt32PrimaryKeyOrdering(
        ColumnDefinition column,
        QueryPlanOrderingDirection direction)
    {
        this.column = column ?? throw new ArgumentNullException(nameof(column));
        this.direction = direction;
    }

    internal CanonicalProviderValueRow[] Sort(
        IReadOnlyList<CanonicalProviderValueRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        cancellationToken.ThrowIfCancellationRequested();

        var source = new CanonicalProviderValueRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source[index] = rows[index];
        }

        if (source.Length < 2)
            return source;

        var destination = new CanonicalProviderValueRow[source.Length];
        for (var width = 1; width < source.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runLength = (long)width * 2;
            for (long start = 0; start < source.Length; start += runLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var left = (int)start;
                var middle = (int)Math.Min(start + width, source.Length);
                var right = middle;
                var end = (int)Math.Min(start + runLength, source.Length);
                var target = left;

                while (left < middle && right < end)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    destination[target++] = Compare(source[left], source[right]) <= 0
                        ? source[left++]
                        : source[right++];
                }

                while (left < middle)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    destination[target++] = source[left++];
                }

                while (right < end)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    destination[target++] = source[right++];
                }
            }

            (source, destination) = (destination, source);
            width = width > source.Length / 2 ? source.Length : width * 2;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return source;
    }

    private int Compare(CanonicalProviderValueRow leftRow, CanonicalProviderValueRow rightRow)
    {
        var left = GetKey(leftRow);
        var right = GetKey(rightRow);
        var comparison = left < right ? -1 : left > right ? 1 : 0;
        return direction == QueryPlanOrderingDirection.Ascending
            ? comparison
            : -comparison;
    }

    private int GetKey(CanonicalProviderValueRow row)
    {
        var value = row[column];
        return value is int int32Value
            ? int32Value
            : throw new InvalidOperationException(
                $"Canonical memory row primary-key column '{column.Table.DbName}.{column.DbName}' contained " +
                $"'{value?.GetType().FullName ?? "null"}' after Int32 ordering capability validation.");
    }
}
