using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Linq.Planning.Sql;

internal sealed partial class SqlQueryPlanBackend : IQueryPlanBackend, IAsyncQueryPlanBackend
{
    private readonly DataSourceAccess dataSource;

    public SqlQueryPlanBackend(DataSourceAccess dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
    }

    public QueryBackendCapabilities Capabilities => QueryBackendCapabilities.Sql;

    public IDataLinqReadSource Source => dataSource;

    public IQueryEntityCursor OpenEntityCursor(ValidatedQueryExecutionRequest request)
    {
        EnsureEntityRequest(request);
        request.Context.CancellationToken.ThrowIfCancellationRequested();
        DataSourceAccess.EnsureReadAllowed(dataSource, "execute a query plan");

        var rows = DataSourceAccess.ReadSequence(dataSource, "execute an entity query plan",
            owner => new QueryPlanSqlBuilder(request.Invocation, dataSource)
                .BuildSelect<object>().Execute(owner),
            cancellationToken: request.Context.CancellationToken);

        return new EnumeratorQueryEntityCursor(
            rows.GetEnumerator(),
            request.Context.CancellationToken);
    }

    public IQueryProjectionCursor<TResult> OpenProjectionCursor<TResult>(
        ValidatedQueryExecutionRequest request)
    {
        EnsureProjectionRequest<TResult>(request);
        request.Context.CancellationToken.ThrowIfCancellationRequested();
        DataSourceAccess.EnsureReadAllowed(dataSource, "execute a query plan");

        var results = DataSourceAccess.ReadSequence(dataSource, "execute a projection query plan",
            owner => CreateProjectionResults<TResult>(request, owner),
            cancellationToken: request.Context.CancellationToken);
        return new EnumeratorQueryProjectionCursor<TResult>(results.GetEnumerator(), request.Context.CancellationToken);
    }

    private IEnumerable<TResult> CreateProjectionResults<TResult>(
        ValidatedQueryExecutionRequest request, TransactionOperationGate.Step? owner) =>
        request.Invocation.Template.Projection switch
        {
            QueryPlanProjection.ScalarMember or
            QueryPlanProjection.SqlRow or
            QueryPlanProjection.GroupedAggregate =>
                new SqlDirectProjectionExecutor(
                        dataSource,
                        request.Context.CancellationToken, owner)
                    .Execute<TResult>(request.Invocation),
            QueryPlanProjection.Anonymous or
            QueryPlanProjection.ComputedRowLocal or
            QueryPlanProjection.JoinedRowLocal =>
                new SqlLocalProjectionExecutor(
                        dataSource,
                        request.Context.CancellationToken, owner)
                    .Execute<TResult>(request.Invocation),
            var projection => throw new InvalidOperationException(
                $"Projection '{projection.Kind}' is not an executable SQL projection.")
        };

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

        using var read = DataSourceAccess.BeginRead(dataSource, "execute a scalar query plan",
            cancellationToken: request.Context.CancellationToken);
        try
        {
            var value = new QueryPlanSqlBuilder(request.Invocation, dataSource)
                .BuildSelect<object>()
                .ExecuteScalar(request.Context.CancellationToken, read?.Step);

            return ConvertScalarResult<TResult>(value, request.Invocation.Template.Result);
        }
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    public bool TryExecuteTerminalEntity(
        ValidatedQueryExecutionRequest request,
        out IImmutableInstance? result)
    {
        EnsureEntityRequest(request);
        request.Context.CancellationToken.ThrowIfCancellationRequested();
        if (!TryGetTerminalScalarPrimaryKeyInvocation(
                request.Invocation,
                out var table,
                out var primaryKey,
                out var resultKind))
        {
            result = null;
            return false;
        }

        result = ExecuteTerminalPrimaryKeyLookup(dataSource, table, primaryKey, resultKind);
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

    private void EnsureProjectionRequest<TResult>(ValidatedQueryExecutionRequest request)
    {
        EnsureRequest(request);

        var projection = request.Invocation.Template.Projection;
        var result = request.Invocation.Template.Result;
        var supportsResult = projection switch
        {
            QueryPlanProjection.ScalarMember or QueryPlanProjection.SqlRow =>
                IsProjectionResult(result.Kind),
            QueryPlanProjection.GroupedAggregate =>
                result.Kind == QueryPlanResultKind.Sequence,
            QueryPlanProjection.Anonymous or
            QueryPlanProjection.ComputedRowLocal or
            QueryPlanProjection.JoinedRowLocal =>
                IsProjectionResult(result.Kind),
            _ => false
        };

        if (!supportsResult)
        {
            throw new InvalidOperationException(
                "The SQL projection backend requires a supported direct or retained local projection result.");
        }

        if (projection.ResultType != typeof(TResult) ||
            result.ResultType != typeof(TResult))
        {
            throw new InvalidOperationException(
                $"The SQL projection backend was asked for '{typeof(TResult).FullName}', but the validated query plan projects " +
                $"'{projection.ResultType.FullName}' and returns '{result.ResultType.FullName}'.");
        }
    }

    private void EnsureRequest(ValidatedQueryExecutionRequest request)
    {
        request.EnsureBackend(this);

        if (!ReferenceEquals(request.Context.Source, dataSource))
        {
            throw new InvalidOperationException(
                "The SQL query backend cannot execute a request created for another read source.");
        }
    }

    internal static IImmutableInstance? ExecuteTerminalPrimaryKeyLookup(
        IDataSourceAccess dataSource,
        TableDefinition table,
        object? primaryKey,
        QueryPlanResultKind resultKind)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(table);
        using var read = DataSourceAccess.BeginRead(dataSource, "execute an exact primary-key terminal query");
        try
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
                    : GetRowByScalarPrimaryKey(dataSource, table, primaryKey, read?.Step);

                var result = ExactPrimaryKeyTerminalExecution.ApplyResultSemantics(row, resultKind);
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
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    private static IImmutableInstance? GetRowByScalarPrimaryKey(
        IDataSourceAccess dataSource,
        TableDefinition table,
        object primaryKey, TransactionOperationGate.Step? owner)
    {
        var tableCache = dataSource.Provider.GetTableCache(table);
        if (tableCache.TryGetRowFromProviderKeyValue(primaryKey, dataSource, out var row, owner))
            return row;

        return tableCache.GetRow(DataLinqKey.FromValue(primaryKey), dataSource, owner);
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

    private static bool IsProjectionResult(QueryPlanResultKind resultKind)
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
    private readonly EnumeratorCallGate calls = new();

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
            using var call = calls.Enter();
            if (!hasCurrent || rows is null)
                throw new InvalidOperationException("The query cursor is not positioned on a row.");

            return rows.Current;
        }
    }

    public bool MoveNext()
    {
        using var call = calls.Enter();
        if (rows is null)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasCurrent = rows.MoveNext();
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasCurrent)
                DisposeCore();

            return hasCurrent;
        }
        catch
        {
            DisposeCore();
            throw;
        }
    }

    public void Dispose()
    {
        using var call = calls.Enter();
        DisposeCore();
    }

    private void DisposeCore()
    {
        hasCurrent = false;
        var currentRows = Interlocked.Exchange(ref rows, null);
        currentRows?.Dispose();
    }
}

internal sealed class EnumeratorQueryProjectionCursor<TResult> : IQueryProjectionCursor<TResult>
{
    private readonly CancellationToken cancellationToken;
    private IEnumerator<TResult>? rows;
    private bool hasCurrent;
    private readonly EnumeratorCallGate calls = new();

    public EnumeratorQueryProjectionCursor(
        IEnumerator<TResult> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        this.rows = rows;
        this.cancellationToken = cancellationToken;
    }

    public TResult Current
    {
        get
        {
            using var call = calls.Enter();
            if (!hasCurrent || rows is null)
                throw new InvalidOperationException("The query projection cursor is not positioned on a result.");

            return rows.Current;
        }
    }

    public bool MoveNext()
    {
        using var call = calls.Enter();
        if (rows is null)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasCurrent = rows.MoveNext();
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasCurrent)
                DisposeCore();

            return hasCurrent;
        }
        catch
        {
            DisposeCore();
            throw;
        }
    }

    public void Dispose()
    {
        using var call = calls.Enter();
        DisposeCore();
    }

    private void DisposeCore()
    {
        hasCurrent = false;
        var currentRows = Interlocked.Exchange(ref rows, null);
        currentRows?.Dispose();
    }
}
