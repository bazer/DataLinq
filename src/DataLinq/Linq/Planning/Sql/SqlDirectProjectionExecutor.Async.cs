using System;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Linq.Planning.Sql;

internal sealed partial class SqlDirectProjectionExecutor
{
    internal AsyncReaderInvocation<T> CaptureAsync<T>(QueryPlanInvocation invocation)
    {
        var select = new QueryPlanSqlBuilder(invocation, dataSource).BuildSelect<T>();
        var factory = IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess).CaptureInvocation()
            ?? throw new InvalidOperationException("The async reader factory returned no invocation snapshot.");
        var source = factory.BindReader(CapturedSql.Capture(select.ToSql()));
        var projection = invocation.Template.Projection;
        var sourceName = $"sql:{dataSource.Provider.DatabaseType}:async-projection";
        return new(source, reader => ReadAsyncProjection<T>(reader, projection, sourceName), dataSource as Transaction,
            Identity: ReadExecutionIdentity.Capture(dataSource, ExecutionOperationKind.Query));
    }

    private static T ReadAsyncProjection<T>(IDataLinqDataReader reader, QueryPlanProjection projection, string sourceName)
    {
        if (projection is QueryPlanProjection.ScalarMember scalar)
            return QueryProjectionResultMaterializer.ConvertResult<T>(ReadOwnedColumn(reader, scalar.Column,
                reader.GetOrdinal(QueryPlanSqlBuilder.ScalarProjectionAlias), sourceName, scalar.ResultType));
        if (projection is QueryPlanProjection.SqlRow row)
        {
            var values = new object?[row.Members.Count];
            for (var i = 0; i < values.Length; i++)
            {
                var member = row.Members[i];
                var ordinal = reader.GetOrdinal(member.Name);
                values[i] = TryReadOwnedColumn(reader, member.Value, ordinal, sourceName, out var modelValue)
                    ? modelValue : ReadOwnedValue(reader, ordinal, member.Value.ClrType);
            }
            return QueryProjectionResultMaterializer.CreateRow<T>(row.Constructor, values);
        }
        if (projection is QueryPlanProjection.GroupedAggregate grouped)
        {
            var values = new object?[grouped.Members.Count];
            for (var i = 0; i < values.Length; i++)
            {
                var member = grouped.Members[i];
                values[i] = member.Value is QueryPlanGroupKeyValue key &&
                    IsModelCompatibleProjectedColumnShape(key.Key, key.ClrType) &&
                    TryReadOwnedColumn(reader, key.Key, i, sourceName, out var modelValue)
                    ? QueryProjectionResultMaterializer.ConvertValue(modelValue, key.ClrType)
                    : ReadOwnedValue(reader, reader.GetOrdinal(member.Name), member.Value.ClrType);
            }
            return QueryProjectionResultMaterializer.CreateRow<T>(grouped.Constructor, values);
        }
        throw new InvalidOperationException("The captured projection is not a direct SQL projection.");
    }

    private static object? ReadOwnedValue(IDataLinqDataReader reader, int ordinal, Type type) =>
        QueryProjectionResultMaterializer.ConvertValue(
            reader.IsDbNull(ordinal) ? null : CanonicalProviderValueRow.CopyMutableValue(reader.GetValue(ordinal)), type);

    private static object? ReadOwnedColumn(IDataLinqDataReader reader, ColumnDefinition column, int ordinal, string sourceName, Type type)
    {
        // A user converter may retain its input. Own mutable provider storage before
        // conversion, not merely an identity-mapped byte[] result afterwards.
        var canonical = CanonicalProviderValueRow.CopyMutableValue(
            ProviderRowDecoder.DecodeCanonicalValue(reader, column, ordinal, sourceName, useColumnAwareGuid: true));
        return QueryProjectionResultMaterializer.ConvertValue(ProviderRowMaterializer.MaterializeValue(column, canonical, sourceName), type);
    }

    private static bool TryReadOwnedColumn(IDataLinqDataReader reader, QueryPlanValue value, int ordinal, string sourceName, out object? result)
    {
        if (value is QueryPlanConvertedValue converted && TryReadOwnedColumn(reader, converted.Value, ordinal, sourceName, out var inner))
        {
            result = QueryProjectionResultMaterializer.ConvertValue(inner, converted.TargetType);
            return true;
        }
        if (value is QueryPlanColumnValue column)
        {
            result = ReadOwnedColumn(reader, column.Column, ordinal, sourceName, column.ClrType);
            return true;
        }
        result = null;
        return false;
    }
}
