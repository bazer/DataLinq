using System;
using System.Collections.Generic;
using System.Threading;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class SqlDirectProjectionExecutor
{
    private readonly DataSourceAccess dataSource;
    private readonly CancellationToken cancellationToken;

    public SqlDirectProjectionExecutor(
        DataSourceAccess dataSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
        this.cancellationToken = cancellationToken;
    }

    public IEnumerable<TResult> Execute<TResult>(QueryPlanInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        return invocation.Template.Projection switch
        {
            QueryPlanProjection.ScalarMember scalarMember =>
                ExecuteScalarProjection<TResult>(invocation, scalarMember),
            QueryPlanProjection.SqlRow sqlRow =>
                ExecuteSqlRowProjection<TResult>(invocation, sqlRow),
            QueryPlanProjection.GroupedAggregate groupedAggregate =>
                ExecuteGroupedAggregateProjection<TResult>(invocation, groupedAggregate),
            var projection => throw new QueryTranslationException(
                $"Projection '{projection.Kind}' is not a direct SQL projection.")
        };
    }

    private IEnumerable<TResult> ExecuteGroupedAggregateProjection<TResult>(
        QueryPlanInvocation invocation,
        QueryPlanProjection.GroupedAggregate projection)
    {
        var select = new QueryPlanSqlBuilder(invocation, dataSource).BuildSelect<TResult>();
        var sourceName = $"sql:{dataSource.Provider.DatabaseType}:grouped-projection";

        foreach (var reader in select.ReadReader(cancellationToken))
        {
            var values = new object?[projection.Members.Count];
            for (var index = 0; index < projection.Members.Count; index++)
            {
                var member = projection.Members[index];
                // Grouped SELECT emits one selector per projection member in this order.
                // The slot ordinal prevents alias comparison from pairing the value with different column metadata.
                if (member.Value is QueryPlanGroupKeyValue groupKey &&
                    IsModelCompatibleProjectedColumnShape(groupKey.Key, groupKey.ClrType) &&
                    TryReadProjectedColumnValue(
                        reader,
                        groupKey.Key,
                        index,
                        sourceName,
                        out var modelValue))
                {
                    values[index] = QueryProjectionResultMaterializer.ConvertValue(
                        modelValue,
                        groupKey.ClrType);
                    continue;
                }

                var ordinal = reader.GetOrdinal(member.Name);
                var rawValue = reader.IsDbNull(ordinal) ? null : reader.GetValue(ordinal);
                values[index] = QueryProjectionResultMaterializer.ConvertValue(
                    rawValue,
                    member.Value.ClrType);
            }

            yield return QueryProjectionResultMaterializer.CreateRow<TResult>(
                projection.Constructor,
                values);
        }
    }

    private IEnumerable<TResult> ExecuteScalarProjection<TResult>(
        QueryPlanInvocation invocation,
        QueryPlanProjection.ScalarMember projection)
    {
        var select = new QueryPlanSqlBuilder(invocation, dataSource).BuildSelect<TResult>();
        var sourceName = $"sql:{dataSource.Provider.DatabaseType}:scalar-projection";

        foreach (var reader in select.ReadReader(cancellationToken))
        {
            var ordinal = reader.GetOrdinal(QueryPlanSqlBuilder.ScalarProjectionAlias);
            var modelValue = ReadProjectedColumnValue(
                reader,
                projection.Column,
                ordinal,
                sourceName,
                projection.ResultType);
            yield return QueryProjectionResultMaterializer.ConvertResult<TResult>(
                modelValue);
        }
    }

    private IEnumerable<TResult> ExecuteSqlRowProjection<TResult>(
        QueryPlanInvocation invocation,
        QueryPlanProjection.SqlRow projection)
    {
        var select = new QueryPlanSqlBuilder(invocation, dataSource).BuildSelect<TResult>();
        var sourceName = $"sql:{dataSource.Provider.DatabaseType}:row-projection";

        foreach (var reader in select.ReadReader(cancellationToken))
        {
            var values = new object?[projection.Members.Count];
            for (var index = 0; index < projection.Members.Count; index++)
            {
                var member = projection.Members[index];
                var ordinal = reader.GetOrdinal(member.Name);
                if (TryReadProjectedColumnValue(
                        reader,
                        member.Value,
                        ordinal,
                        sourceName,
                        out var modelValue))
                {
                    values[index] = modelValue;
                    continue;
                }

                var rawValue = reader.IsDbNull(ordinal) ? null : reader.GetValue(ordinal);
                values[index] = QueryProjectionResultMaterializer.ConvertValue(
                    rawValue,
                    member.Value.ClrType);
            }

            yield return QueryProjectionResultMaterializer.CreateRow<TResult>(
                projection.Constructor,
                values);
        }
    }

    private static bool TryReadProjectedColumnValue(
        IDataLinqDataReader reader,
        QueryPlanValue value,
        int ordinal,
        string sourceName,
        out object? modelValue)
    {
        if (value is QueryPlanConvertedValue converted)
        {
            if (!TryReadProjectedColumnValue(
                    reader,
                    converted.Value,
                    ordinal,
                    sourceName,
                    out var innerValue))
            {
                modelValue = null;
                return false;
            }

            modelValue = QueryProjectionResultMaterializer.ConvertValue(
                innerValue,
                converted.TargetType);
            return true;
        }

        if (value is not QueryPlanColumnValue columnValue)
        {
            modelValue = null;
            return false;
        }

        modelValue = ReadProjectedColumnValue(
            reader,
            columnValue.Column,
            ordinal,
            sourceName,
            columnValue.ClrType);
        return true;
    }

    private static object? ReadProjectedColumnValue(
        IDataLinqDataReader reader,
        ColumnDefinition column,
        int ordinal,
        string sourceName,
        Type targetType)
    {
        var canonicalValue = ProviderRowDecoder.DecodeCanonicalValue(
            reader,
            column,
            ordinal,
            sourceName,
            useColumnAwareGuid: true);
        var modelValue = ProviderRowMaterializer.MaterializeValue(
            column,
            canonicalValue,
            sourceName);
        return QueryProjectionResultMaterializer.ConvertValue(modelValue, targetType);
    }

    private static bool IsModelCompatibleProjectedColumnShape(
        QueryPlanValue value,
        Type resultType)
    {
        var leaf = value;
        while (leaf is QueryPlanConvertedValue converted)
            leaf = converted.Value;

        if (leaf is not QueryPlanColumnValue columnValue)
            return false;

        var declaredModelType = columnValue.Column.ModelClrType ?? columnValue.ClrType;
        var modelType = Nullable.GetUnderlyingType(declaredModelType) ?? declaredModelType;
        if (!IsAssignableFromModelType(modelType, columnValue.ClrType) ||
            !IsAssignableFromModelType(modelType, resultType))
        {
            return false;
        }

        while (value is QueryPlanConvertedValue converted)
        {
            if (!IsAssignableFromModelType(modelType, converted.TargetType))
                return false;

            value = converted.Value;
        }

        return true;
    }

    private static bool IsAssignableFromModelType(Type modelType, Type targetType)
    {
        var nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return nonNullableTarget.IsAssignableFrom(modelType);
    }
}
