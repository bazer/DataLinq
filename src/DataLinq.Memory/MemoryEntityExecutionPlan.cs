using System;
using System.Collections.Generic;
using System.Threading;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Linq.Planning;
using DataLinq.Metadata;

namespace DataLinq.Memory;

/// <summary>
/// Invocation-local interpreter state for the currently admitted memory entity-sequence closure.
/// </summary>
internal sealed class MemoryEntityExecutionPlan
{
    private const string ComparisonSourceName = "memory-query:equality";
    private readonly MemoryInt32EqualityPredicate[] predicates;

    private MemoryEntityExecutionPlan(MemoryInt32EqualityPredicate[] predicates)
    {
        this.predicates = predicates;
    }

    internal static MemoryEntityExecutionPlan Compile(
        ValidatedQueryExecutionRequest request,
        QueryPlanProjection.Entity entity)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(entity);

        var operations = request.Invocation.Template.Operations;
        var predicates = new List<MemoryInt32EqualityPredicate>(operations.Count);
        for (var index = 0; index < operations.Count; index++)
        {
            if (operations[index] is not QueryPlanOperation.Where { Predicate: QueryPlanPredicate.Compare comparison })
            {
                throw CapabilityInvariant(
                    $"operation {index} is not a direct equality filter admitted by this checkpoint.");
            }

            predicates.Add(CompileEquality(request.Invocation, entity, comparison, index));
        }

        return new MemoryEntityExecutionPlan(predicates.ToArray());
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
        QueryPlanProjection.Entity entity,
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

        ValidateColumn(entity, column, operationIndex);
        var canonicalValue = ResolveCanonicalValue(invocation, column.Column, scalar, operationIndex);
        return new MemoryInt32EqualityPredicate(column.Column, canonicalValue);
    }

    private static void ValidateColumn(
        QueryPlanProjection.Entity entity,
        QueryPlanColumnValue value,
        int operationIndex)
    {
        var column = value.Column;
        if (!ReferenceEquals(value.Source, entity.Source) ||
            !ReferenceEquals(column.Table, entity.Source.Table))
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
        new($"The memory capability profile admitted an invalid equality shape: {detail}");
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
