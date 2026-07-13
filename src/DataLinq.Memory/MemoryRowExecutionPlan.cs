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
    private const string ComparisonSourceName = "memory-query:equality";
    private readonly MemoryInt32EqualityPredicate[] predicates;
    private readonly MemoryInt32PrimaryKeyOrdering? ordering;
    private readonly int? takeCount;

    private MemoryRowExecutionPlan(
        MemoryInt32EqualityPredicate[] predicates,
        MemoryInt32PrimaryKeyOrdering? ordering,
        int? takeCount)
    {
        this.predicates = predicates;
        this.ordering = ordering;
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
        var predicates = new List<MemoryInt32EqualityPredicate>(operations.Count);
        MemoryInt32PrimaryKeyOrdering? ordering = null;
        int? takeCount = null;
        var hasSeenTake = false;
        for (var index = 0; index < operations.Count; index++)
        {
            switch (operations[index])
            {
                case QueryPlanOperation.Where { Predicate: QueryPlanPredicate.Compare comparison }:
                    if (hasSeenTake)
                    {
                        throw CapabilityInvariant(
                            $"operation {index} applies a filter after Take.");
                    }

                    predicates.Add(CompileEquality(request.Invocation, rootSource, comparison, index));
                    break;

                case QueryPlanOperation.OrderBy orderBy:
                    if (ordering is not null || hasSeenTake)
                    {
                        throw CapabilityInvariant(
                            $"operation {index} introduces a repeated or post-Take ordering.");
                    }

                    ordering = CompileOrdering(rootSource, orderBy, index);
                    break;

                case QueryPlanOperation.Take take:
                    if (ordering is null || hasSeenTake)
                    {
                        throw CapabilityInvariant(
                            $"operation {index} is not the single Take following one ordering admitted by this checkpoint.");
                    }

                    takeCount = ResolveTakeCount(request.Invocation, take.Count, index);
                    hasSeenTake = true;
                    break;

                default:
                    throw CapabilityInvariant(
                        $"operation {index} is not admitted by the memory entity-sequence checkpoint.");
            }
        }

        return new MemoryRowExecutionPlan(predicates.ToArray(), ordering, takeCount);
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

        var resultCount = takeCount is { } limit
            ? Math.Min(limit, ordered.Length)
            : ordered.Length;
        if (resultCount == ordered.Length)
            return ordered;

        var selected = new CanonicalProviderValueRow[resultCount];
        for (var index = 0; index < resultCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            selected[index] = ordered[index];
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

    private static MemoryInt32EqualityPredicate CompileEquality(
        QueryPlanInvocation invocation,
        QueryPlanSourceSlot rootSource,
        QueryPlanPredicate.Compare comparison,
        int operationIndex)
    {
        if (comparison.Operator != QueryPlanComparisonOperator.Equal ||
            comparison.NullSemantics != QueryPlanNullSemantics.Default)
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} uses comparison '{comparison.Operator}' with null semantics '{comparison.NullSemantics}'.");
        }

        if (!QueryPlanComparisonShapeFacts.IsDirectNonNullableInt32ColumnAndScalar(
                comparison.Left,
                comparison.Right,
                invocation.Template.BindingDeclarations))
        {
            throw CapabilityInvariant(
                $"operation {operationIndex} is not the direct non-nullable Int32 column-to-scalar " +
                "shape admitted by the validated capability token.");
        }

        var (column, scalar) = (comparison.Left, comparison.Right) switch
        {
            (QueryPlanColumnValue leftColumn, QueryPlanScalarBindingReference rightScalar) =>
                (leftColumn, rightScalar),
            (QueryPlanScalarBindingReference leftScalar, QueryPlanColumnValue rightColumn) =>
                (rightColumn, leftScalar),
            _ => throw CapabilityInvariant(
                $"operation {operationIndex} has operands inconsistent with its validated comparison shape.")
        };

        ValidateColumn(rootSource, column, operationIndex);
        var canonicalValue = ResolveCanonicalValue(invocation, column.Column, scalar, operationIndex);
        return new MemoryInt32EqualityPredicate(column.Column, canonicalValue);
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

    private static int ResolveTakeCount(
        QueryPlanInvocation invocation,
        QueryPlanValue count,
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
                $"operation {operationIndex} is not a direct non-negative Int32 scalar-binding Take.");
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

    private static int ResolveCanonicalValue(
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
                    $"operation {operationIndex} normalized a non-null Int32 binding to null.");

            return canonicalValue is int value
                ? value
                : throw CapabilityInvariant(
                    $"operation {operationIndex} normalized an Int32 binding to canonical type " +
                    $"'{canonicalValue.GetType().FullName}'.");
        }
        catch (ModelValueConversionException exception)
        {
            throw new QueryTranslationException(
                $"Backend 'memory' could not normalize scalar binding '{scalar.BindingId}' for " +
                $"column '{column.Table.DbName}.{column.DbName}' without exposing its value.",
                exception);
        }
    }

    private static InvalidOperationException CapabilityInvariant(string detail) =>
        new($"The memory capability profile admitted an invalid row-selection shape: {detail}");
}

internal sealed class MemoryInt32EqualityPredicate
{
    private readonly ColumnDefinition column;
    private readonly int canonicalValue;

    internal MemoryInt32EqualityPredicate(
        ColumnDefinition column,
        int canonicalValue)
    {
        this.column = column ?? throw new ArgumentNullException(nameof(column));
        this.canonicalValue = canonicalValue;
    }

    internal bool Matches(CanonicalProviderValueRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rowValue = row[column];
        return rowValue is int value
            ? value == canonicalValue
            : throw new InvalidOperationException(
                $"Canonical memory row column '{column.Table.DbName}.{column.DbName}' contained " +
                $"'{rowValue?.GetType().FullName ?? "null"}' after Int32 capability validation.");
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
