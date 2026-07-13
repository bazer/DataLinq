using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class SqlQueryPlanBackend : IQueryPlanBackend
{
    private readonly DataSourceAccess dataSource;

    public SqlQueryPlanBackend(DataSourceAccess dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
    }

    public QueryBackendCapabilities Capabilities => QueryBackendCapabilities.Sql;

    public IDataLinqReadSource Source => dataSource;

    internal DataSourceAccess DataSource => dataSource;

    public IQueryEntityCursor OpenEntityCursor(ValidatedQueryExecutionRequest request)
    {
        EnsureEntityRequest(request);
        request.Context.CancellationToken.ThrowIfCancellationRequested();
        DataSourceAccess.EnsureReadAllowed(dataSource, "execute a query plan");

        var rows = new QueryPlanSqlBuilder(request.Invocation, dataSource)
            .BuildSelect<object>()
            .Execute();

        return new EnumeratorQueryEntityCursor(
            rows.GetEnumerator(),
            request.Context.CancellationToken);
    }

    public TResult ExecuteScalar<TResult>(ValidatedQueryExecutionRequest request)
    {
        EnsureScalarRequest(request);
        request.Context.CancellationToken.ThrowIfCancellationRequested();

        if (request.Invocation.Template.Result.ResultType != typeof(TResult))
        {
            throw new InvalidOperationException(
                $"The SQL scalar backend was asked for '{typeof(TResult).FullName}', but the validated query plan returns " +
                $"'{request.Invocation.Template.Result.ResultType.FullName}'.");
        }

        DataSourceAccess.EnsureReadAllowed(dataSource, "execute a query plan");

        var value = new QueryPlanSqlBuilder(request.Invocation, dataSource)
            .BuildSelect<object>()
            .ExecuteScalar(request.Context.CancellationToken);

        return ConvertScalarResult<TResult>(value, request.Invocation.Template.Result);
    }

    public bool TryExecuteTerminalEntity(
        ValidatedQueryExecutionRequest request,
        out IImmutableInstance? result)
    {
        EnsureEntityRequest(request);
        request.Context.CancellationToken.ThrowIfCancellationRequested();
        DataSourceAccess.EnsureReadAllowed(dataSource, "execute a query plan");

        if (!TryGetTerminalScalarPrimaryKeyInvocation(
                request.Invocation,
                out var table,
                out var primaryKey,
                out var resultKind))
        {
            result = null;
            return false;
        }

        result = ExecuteTerminalPrimaryKeyLookup(table, primaryKey, resultKind);
        return true;
    }

    private void EnsureEntityRequest(ValidatedQueryExecutionRequest request)
    {
        EnsureRequest(request);

        if (request.Invocation.Template.Projection is not QueryPlanProjection.Entity ||
            !IsEntityResult(request.Invocation.Template.Result.Kind))
        {
            throw new InvalidOperationException(
                "The SQL entity backend requires an entity sequence or entity terminal result.");
        }
    }

    private void EnsureScalarRequest(ValidatedQueryExecutionRequest request)
    {
        EnsureRequest(request);

        if (!request.Invocation.Template.Result.IsScalarResult)
        {
            throw new InvalidOperationException(
                "The SQL scalar backend requires a Count, Any, Sum, Min, Max, or Average result.");
        }
    }

    private void EnsureRequest(ValidatedQueryExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureBackend(this);

        if (!ReferenceEquals(request.Context.Source, dataSource))
        {
            throw new InvalidOperationException(
                "The SQL query backend cannot execute a request created for another read source.");
        }
    }

    private IImmutableInstance? ExecuteTerminalPrimaryKeyLookup(
        TableDefinition table,
        object? primaryKey,
        QueryPlanResultKind resultKind)
    {
        var telemetryContext = DataLinqTelemetryContext.FromProvider(dataSource.Provider);
        var activity = DataLinqTelemetry.StartQueryActivity(
            telemetryContext,
            table.DbName,
            "entity",
            dataSource is Transaction);
        var startedAt = Stopwatch.GetTimestamp();
        var succeeded = false;

        DataLinqMetrics.RecordEntityQueryExecution(dataSource.Provider);

        try
        {
            var row = primaryKey is null
                ? null
                : GetRowByScalarPrimaryKey(table, primaryKey);

            var result = ConvertPrimaryKeyLookupResult(row, resultKind);
            succeeded = true;
            return result;
        }
        catch (Exception exception)
        {
            DataLinqTelemetry.RecordException(activity, exception);
            throw;
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(startedAt);
            DataLinqTelemetry.RecordQueryExecution(
                telemetryContext,
                table.DbName,
                "entity",
                dataSource is Transaction,
                succeeded,
                duration);

            if (activity is not null)
            {
                if (!succeeded)
                    activity.SetStatus(ActivityStatusCode.Error);

                activity.SetTag("datalinq.outcome", succeeded ? "success" : "failure");
                activity.Dispose();
            }
        }
    }

    private IImmutableInstance? GetRowByScalarPrimaryKey(
        TableDefinition table,
        object primaryKey)
    {
        var tableCache = dataSource.Provider.GetTableCache(table);
        if (tableCache.TryGetRowFromProviderKeyValue(primaryKey, dataSource, out var row))
            return row;

        return tableCache.GetRow(DataLinqKey.FromValue(primaryKey), dataSource);
    }

    private static IImmutableInstance? ConvertPrimaryKeyLookupResult(
        IImmutableInstance? row,
        QueryPlanResultKind resultKind)
    {
        if (row is not null)
            return row;

        return resultKind switch
        {
            QueryPlanResultKind.SingleOrDefault or QueryPlanResultKind.FirstOrDefault => null,
            _ => throw new InvalidOperationException("Sequence contains no elements")
        };
    }

    private static bool TryGetTerminalScalarPrimaryKeyInvocation(
        QueryPlanInvocation invocation,
        out TableDefinition table,
        out object? primaryKey,
        out QueryPlanResultKind resultKind)
    {
        table = null!;
        primaryKey = null;
        resultKind = default;

        var template = invocation.Template;
        resultKind = template.Result.Kind;
        if (!IsTerminalPrimaryKeyResult(resultKind) ||
            template.Projection is not QueryPlanProjection.Entity
            {
                Source.Kind: QueryPlanSourceKind.RootTable
            } entity ||
            template.Sources.Count != 1 ||
            template.Operations.Count != 1 ||
            template.Operations[0] is not QueryPlanOperation.Where
            {
                Predicate: QueryPlanPredicate.Compare
                {
                    Operator: QueryPlanComparisonOperator.Equal
                } comparison
            })
        {
            return false;
        }

        table = entity.Source.Table;
        if (!table.PrimaryKeyShape.SupportsScalarProviderKeyStore ||
            table.PrimaryKeyColumns.Count != 1)
        {
            return false;
        }

        var primaryKeyColumn = table.PrimaryKeyColumns[0];
        if (!TryGetPrimaryKeyInvocationValue(
                comparison.Left,
                comparison.Right,
                entity.Source,
                primaryKeyColumn,
                invocation.Values,
                out primaryKey) &&
            !TryGetPrimaryKeyInvocationValue(
                comparison.Right,
                comparison.Left,
                entity.Source,
                primaryKeyColumn,
                invocation.Values,
                out primaryKey))
        {
            return false;
        }

        return primaryKey is null || table.PrimaryKeyShape.SupportsScalarProviderKey(primaryKey.GetType());
    }

    private static bool TryGetPrimaryKeyInvocationValue(
        QueryPlanValue columnCandidate,
        QueryPlanValue valueCandidate,
        QueryPlanSourceSlot source,
        ColumnDefinition primaryKeyColumn,
        QueryPlanBindingValues values,
        out object? primaryKey)
    {
        primaryKey = null;
        return columnCandidate is QueryPlanColumnValue column &&
            ReferenceEquals(column.Source, source) &&
            ReferenceEquals(column.Column, primaryKeyColumn) &&
            TryResolveInvocationScalar(valueCandidate, values, out primaryKey);
    }

    private static bool TryResolveInvocationScalar(
        QueryPlanValue value,
        QueryPlanBindingValues values,
        out object? result)
    {
        switch (value)
        {
            case QueryPlanIntrinsicValue { Intrinsic: QueryPlanIntrinsicKind.Null }:
                result = null;
                return true;
            case QueryPlanIntrinsicValue { Intrinsic: QueryPlanIntrinsicKind.BooleanTrue }:
                result = true;
                return true;
            case QueryPlanIntrinsicValue { Intrinsic: QueryPlanIntrinsicKind.BooleanFalse }:
                result = false;
                return true;
            case QueryPlanScalarBindingReference scalar
                when values.TryGet(scalar.BindingId, out var binding) &&
                     binding is QueryPlanInvocationValue.Scalar scalarValue:
                result = scalarValue.Value;
                return true;
            case QueryPlanConvertedValue converted
                when TryResolveInvocationScalar(converted.Value, values, out var sourceValue):
                return TryConvertInvocationScalar(sourceValue, converted.TargetType, out result);
            default:
                result = null;
                return false;
        }
    }

    private static bool TryConvertInvocationScalar(object? value, Type targetType, out object? result)
    {
        if (value is null)
        {
            result = null;
            return true;
        }

        var conversionType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (conversionType.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        try
        {
            result = conversionType.IsEnum
                ? Enum.ToObject(conversionType, value)
                : Convert.ChangeType(value, conversionType, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            result = null;
            return false;
        }
    }

    private static bool IsTerminalPrimaryKeyResult(QueryPlanResultKind resultKind)
        => resultKind is QueryPlanResultKind.Single or
            QueryPlanResultKind.SingleOrDefault or
            QueryPlanResultKind.First or
            QueryPlanResultKind.FirstOrDefault;

    private static bool IsEntityResult(QueryPlanResultKind resultKind)
        => resultKind is QueryPlanResultKind.Sequence or
            QueryPlanResultKind.Single or
            QueryPlanResultKind.SingleOrDefault or
            QueryPlanResultKind.First or
            QueryPlanResultKind.FirstOrDefault or
            QueryPlanResultKind.Last or
            QueryPlanResultKind.LastOrDefault;

    private static TResult ConvertScalarResult<TResult>(
        object? result,
        QueryPlanResult planResult)
    {
        if (result is DBNull)
            result = null;

        if (planResult.Kind == QueryPlanResultKind.Any)
        {
            return (TResult)(object)(
                Convert.ToInt64(result ?? 0, CultureInfo.InvariantCulture) > 0);
        }

        if (result is null)
        {
            if (planResult.Kind == QueryPlanResultKind.Sum ||
                Nullable.GetUnderlyingType(typeof(TResult)) is not null)
            {
                return default!;
            }

            throw new InvalidOperationException(
                $"Scalar query plan result '{planResult.Kind}' returned no value.");
        }

        var targetType = Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult);
        if (targetType.IsInstanceOfType(result))
            return (TResult)result;

        return (TResult)Convert.ChangeType(result, targetType, CultureInfo.InvariantCulture);
    }
}

internal sealed class EnumeratorQueryEntityCursor : IQueryEntityCursor
{
    private readonly CancellationToken cancellationToken;
    private IEnumerator<IImmutableInstance>? rows;
    private bool hasCurrent;

    public EnumeratorQueryEntityCursor(
        IEnumerator<IImmutableInstance> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        this.rows = rows;
        this.cancellationToken = cancellationToken;
    }

    public IImmutableInstance Current
    {
        get
        {
            if (!hasCurrent || rows is null)
                throw new InvalidOperationException("The query cursor is not positioned on a row.");

            return rows.Current;
        }
    }

    public bool MoveNext()
    {
        if (rows is null)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasCurrent = rows.MoveNext();
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasCurrent)
                Dispose();

            return hasCurrent;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        hasCurrent = false;
        var currentRows = Interlocked.Exchange(ref rows, null);
        currentRows?.Dispose();
    }
}
