using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Linq.Planning.Sql;

namespace DataLinq.Linq.Planning.Expressions;

internal sealed class ExpressionQueryPlanProvider : IQueryProvider
{
    private readonly DatabaseDefinition metadata;
    private readonly IDataLinqReadSource? readSource;

    public ExpressionQueryPlanProvider(DatabaseDefinition metadata)
    {
        this.metadata = metadata;
    }

    private ExpressionQueryPlanProvider(IDataLinqReadSource readSource)
    {
        this.readSource = readSource;
        metadata = readSource.Metadata;
    }

    public static ExpressionQueryPlanProvider ForExecution(DataSourceAccess dataSource)
        => ForExecution((IDataLinqReadSource)dataSource);

    public static ExpressionQueryPlanProvider ForExecution(IDataLinqReadSource readSource)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        return new ExpressionQueryPlanProvider(readSource);
    }

    public IQueryable<TElement> CreateRoot<TElement>()
        => new ExpressionPlanQueryable<TElement>(this);

    public IQueryable CreateQuery(Expression expression)
        => throw new NotSupportedException("Non-generic query creation is not supported by the DataLinq expression plan provider.");

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new ExpressionPlanQueryable<TElement>(this, expression);

    public object? Execute(Expression expression)
        => Execute<object?>(expression);

    public TResult Execute<TResult>(Expression expression)
    {
        var plan = Parse(expression, typeof(TResult));
        var dataSource = GetSqlDataSource();
        if (ExpressionQueryPlanExecutor.TryExecuteTerminalPrimaryKeyInvocation(
                dataSource,
                plan,
                out TResult primaryKeyResult))
        {
            return primaryKeyResult;
        }

        return ExpressionQueryPlanExecutor.Execute<TResult>(dataSource, plan);
    }

    public IEnumerable<TElement> ExecuteEnumerable<TElement>(Expression expression)
    {
        var plan = Parse(expression, typeof(TElement));
        return ExpressionQueryPlanExecutor.ExecuteEnumerable<TElement>(GetSqlDataSource(), plan);
    }

    public QueryPlanInvocation Parse(Expression expression, Type resultType)
        => ExpressionQueryPlanParser.Convert(metadata, expression, resultType);

    private DataSourceAccess GetSqlDataSource()
    {
        if (readSource is DataSourceAccess dataSource)
            return dataSource;

        if (readSource is null)
        {
            throw new NotSupportedException(
                "The DataLinq expression plan provider was created for parsing only and cannot execute queries.");
        }

        throw new NotSupportedException(
            $"Read source type '{readSource.GetType().FullName}' does not yet provide query-plan execution. " +
            "Use a SQL DataSourceAccess or a read backend with DataLinq query-plan execution services.");
    }
}

internal sealed class ExpressionPlanQueryable<T> : IOrderedQueryable<T>
{
    private readonly ExpressionQueryPlanProvider provider;

    public ExpressionPlanQueryable(ExpressionQueryPlanProvider provider)
        : this(provider, null)
    {
    }

    public ExpressionPlanQueryable(ExpressionQueryPlanProvider provider, Expression? expression)
    {
        this.provider = provider;
        Expression = expression ?? Expression.Constant(this);
    }

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider => provider;

    public IEnumerator<T> GetEnumerator()
        => provider.ExecuteEnumerable<T>(Expression).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}

internal static class ExpressionQueryPlanExecutor
{
    internal static bool TryExecuteTerminalPrimaryKeyInvocation<TResult>(
        DataSourceAccess dataSource,
        QueryPlanInvocation invocation,
        out TResult result)
    {
        result = default!;

        if (!TryGetTerminalScalarPrimaryKeyInvocation(
                invocation,
                out var table,
                out var primaryKey,
                out var resultKind))
        {
            return false;
        }

        result = ExecuteTerminalPrimaryKeyLookup<TResult>(dataSource, table, primaryKey, resultKind);
        return true;
    }

    public static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan)
        => ExecuteEnumerable<TElement>(
            dataSource,
            plan,
            ProjectionEvaluationOptions.Default);

    internal static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions)
    {
        var template = plan.Template;
        if (template.Result.Kind != QueryPlanResultKind.Sequence)
            throw new QueryTranslationException($"Expression parser route expected a sequence result, but the plan result is '{template.Result.Kind}'.");

        ValidateProjectionDisposition(template.Projection, projectionOptions);

        if (template.Projection is QueryPlanProjection.Entity)
        {
            return new QueryPlanSqlBuilder(plan, dataSource)
                .BuildSelect<object>()
                .Execute()
                .Cast<TElement>();
        }

        if (template.Projection is QueryPlanProjection.GroupedAggregate groupedAggregate)
            return ExecuteGroupedAggregateProjection<TElement>(dataSource, plan, groupedAggregate);

        if (template.Projection is QueryPlanProjection.ScalarMember scalarMember)
            return ExecuteScalarProjection<TElement>(dataSource, plan, scalarMember);

        if (template.Projection is QueryPlanProjection.SqlRow sqlRow)
            return ExecuteSqlRowProjection<TElement>(dataSource, plan, sqlRow);

        return ExecuteProjectedSequence<TElement>(dataSource, plan, projectionOptions);
    }

    public static TResult Execute<TResult>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan)
        => Execute<TResult>(dataSource, plan, ProjectionEvaluationOptions.Default);

    internal static TResult Execute<TResult>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions)
    {
        ValidateProjectionDisposition(plan.Template.Projection, projectionOptions);

        return plan.Template.Result.Kind switch
        {
            QueryPlanResultKind.Count or
            QueryPlanResultKind.Any or
            QueryPlanResultKind.Sum or
            QueryPlanResultKind.Min or
            QueryPlanResultKind.Max or
            QueryPlanResultKind.Average => ExecuteScalar<TResult>(dataSource, plan),
            QueryPlanResultKind.First => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.First()),
            QueryPlanResultKind.FirstOrDefault => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.FirstOrDefault()),
            QueryPlanResultKind.Single => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.Single()),
            QueryPlanResultKind.SingleOrDefault => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.SingleOrDefault()),
            QueryPlanResultKind.Last => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.Last()),
            QueryPlanResultKind.LastOrDefault => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.LastOrDefault()),
            var kind => throw new QueryTranslationException($"Expression parser route cannot execute query plan result '{kind}'.")
        };
    }

    private static TResult ExecuteTerminalPrimaryKeyLookup<TResult>(
        DataSourceAccess dataSource,
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
                : GetRowByScalarPrimaryKey(dataSource, table, primaryKey);

            var result = ConvertPrimaryKeyLookupResult<TResult>(row, resultKind);
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

    private static IImmutableInstance? GetRowByScalarPrimaryKey(
        DataSourceAccess dataSource,
        TableDefinition table,
        object primaryKey)
    {
        var tableCache = dataSource.Provider.GetTableCache(table);
        if (tableCache.TryGetRowFromProviderKeyValue(primaryKey, dataSource, out var row))
            return row;

        return tableCache.GetRow(DataLinqKey.FromValue(primaryKey), dataSource);
    }

    private static TResult ConvertPrimaryKeyLookupResult<TResult>(
        IImmutableInstance? row,
        QueryPlanResultKind resultKind)
    {
        if (row is not null)
            return (TResult)(object)row;

        return resultKind switch
        {
            QueryPlanResultKind.SingleOrDefault or QueryPlanResultKind.FirstOrDefault => default!,
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

    private static TResult ExecuteSingle<TResult>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions,
        Func<IEnumerable<TResult>, TResult?> selector)
    {
        if (plan.Template.Projection is not QueryPlanProjection.Entity)
        {
            if (plan.Template.Projection is QueryPlanProjection.ScalarMember scalarMember)
                return selector(ExecuteScalarProjection<TResult>(dataSource, plan, scalarMember))!;

            if (plan.Template.Projection is QueryPlanProjection.SqlRow sqlRow)
                return selector(ExecuteSqlRowProjection<TResult>(dataSource, plan, sqlRow))!;

            return selector(ExecuteProjectedSequence<TResult>(dataSource, plan, projectionOptions))!;
        }

        var sequence = new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<TResult>()
            .ExecuteAs<TResult>();
        return selector(sequence)!;
    }

    private static TResult ExecuteScalar<TResult>(DataSourceAccess dataSource, QueryPlanInvocation plan)
    {
        var select = new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<TResult>();
        var result = select.ExecuteScalar();

        return ConvertScalarResult<TResult>(result, plan.Template.Result);
    }

    private static IEnumerable<TElement> ExecuteProjectedSequence<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions)
    {
        var recipe = GetProjectionRecipe(plan.Template.Projection);
        var planSqlBuilder = new QueryPlanSqlBuilder(plan, dataSource);
        var joinedSources = planSqlBuilder.GetJoinedSources().ToArray();
        return joinedSources.Length > 1
            ? ExecuteJoinedProjection<TElement>(
                dataSource,
                plan,
                recipe,
                projectionOptions,
                planSqlBuilder,
                joinedSources)
            : ExecuteSingleSourceProjection<TElement>(dataSource, plan, recipe, projectionOptions);
    }

    private static IEnumerable<TElement> ExecuteSingleSourceProjection<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        QueryPlanProjectionRecipe recipe,
        ProjectionEvaluationOptions projectionOptions)
    {
        var rootSource = plan.Template.Sources.First(static source => source.Kind == QueryPlanSourceKind.RootTable);
        var entityPlan = ReprojectAsEntity(plan, rootSource);
        foreach (var row in ExecuteEntityRows(dataSource, entityPlan))
        {
            var sourceValues = new Dictionary<QueryPlanSourceSlot, object?>
            {
                [rootSource] = row
            };
            yield return ConvertProjectionResult<TElement>(
                QueryPlanProjectionRecipeEvaluator.Evaluate(
                    recipe,
                    sourceValues,
                    plan.Values,
                    projectionOptions));
        }
    }

    private static IEnumerable<TElement> ExecuteJoinedProjection<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        QueryPlanProjectionRecipe recipe,
        ProjectionEvaluationOptions projectionOptions,
        QueryPlanSqlBuilder planSqlBuilder,
        QueryPlanSourceSlot[] joinedSources)
    {
        var select = planSqlBuilder.BuildSelect<TElement>();
        select.What(planSqlBuilder.GetJoinedPrimaryKeySelectors().ToArray());

        int[][]? primaryKeyOrdinalsBySource = null;
        var joinedPrimaryKeyRows = new List<object[]>();
        foreach (var reader in select.ReadReader())
        {
            primaryKeyOrdinalsBySource ??= GetJoinedPrimaryKeyOrdinals(reader, joinedSources);
            var primaryKeysBySource = new object[joinedSources.Length];
            for (var sourceIndex = 0; sourceIndex < joinedSources.Length; sourceIndex++)
                primaryKeysBySource[sourceIndex] = ReadPrimaryKey(reader, joinedSources[sourceIndex], primaryKeyOrdinalsBySource[sourceIndex]);

            joinedPrimaryKeyRows.Add(primaryKeysBySource);
        }

        foreach (var primaryKeysBySource in joinedPrimaryKeyRows)
        {
            var sourceValues = new Dictionary<QueryPlanSourceSlot, object?>(joinedSources.Length);
            for (var sourceIndex = 0; sourceIndex < joinedSources.Length; sourceIndex++)
            {
                var source = joinedSources[sourceIndex];
                sourceValues[source] = GetJoinedRow(dataSource, source, primaryKeysBySource[sourceIndex])
                    ?? throw new InvalidOperationException($"Joined row for table '{source.Table.DbName}' could not be materialized from its provider primary key.");
            }

            yield return ConvertProjectionResult<TElement>(
                QueryPlanProjectionRecipeEvaluator.Evaluate(
                    recipe,
                    sourceValues,
                    plan.Values,
                    projectionOptions));
        }
    }

    private static IImmutableInstance? GetJoinedRow(
        DataSourceAccess dataSource,
        QueryPlanSourceSlot source,
        object primaryKey)
    {
        var tableCache = dataSource.Provider.GetTableCache(source.Table);
        if (tableCache.TryGetRowFromProviderKeyValue(primaryKey, dataSource, out var row))
            return row;

        return primaryKey is DataLinqKey dataLinqKey
            ? tableCache.GetRow(dataLinqKey, dataSource)
            : null;
    }

    private static object ReadPrimaryKey(IDataLinqDataReader reader, QueryPlanSourceSlot source, IReadOnlyList<int> primaryKeyOrdinals)
    {
        var primaryKeyColumns = source.Table.PrimaryKeyColumns;
        if (primaryKeyColumns.Count == 1)
            return reader.GetValue<object>(primaryKeyColumns[0], primaryKeyOrdinals[0])!;

        var values = new object?[primaryKeyColumns.Count];
        for (var index = 0; index < values.Length; index++)
            values[index] = reader.GetValue<object>(primaryKeyColumns[index], primaryKeyOrdinals[index]);

        return DataLinqKey.FromValues(values);
    }

    private static IEnumerable<TElement> ExecuteGroupedAggregateProjection<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        QueryPlanProjection.GroupedAggregate projection)
    {
        var select = new QueryPlanSqlBuilder(plan, dataSource).BuildSelect<TElement>();

        foreach (var reader in select.ReadReader())
        {
            var values = new object?[projection.Members.Count];
            for (var index = 0; index < projection.Members.Count; index++)
            {
                var member = projection.Members[index];
                var ordinal = reader.GetOrdinal(member.Name);
                var rawValue = reader.IsDbNull(ordinal) ? null : reader.GetValue(ordinal);
                values[index] = ConvertReaderValue(rawValue, member.Value.ClrType);
            }

            yield return CreateProjectionRow<TElement>(projection.Constructor, values);
        }
    }

    private static IEnumerable<TElement> ExecuteScalarProjection<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        QueryPlanProjection.ScalarMember projection)
    {
        var select = new QueryPlanSqlBuilder(plan, dataSource).BuildSelect<TElement>();
        var sourceName = $"sql:{dataSource.Provider.DatabaseType}:scalar-projection";

        foreach (var reader in select.ReadReader())
        {
            var ordinal = reader.GetOrdinal(QueryPlanSqlBuilder.ScalarProjectionAlias);
            if (projection.Column.HasScalarConverter)
            {
                var canonicalValue = ProviderRowDecoder.DecodeCanonicalValue(
                    reader,
                    projection.Column,
                    ordinal,
                    sourceName);
                var modelValue = ProviderRowMaterializer.MaterializeValue(
                    projection.Column,
                    canonicalValue,
                    sourceName);
                yield return ConvertProjectionResult<TElement>(modelValue);
                continue;
            }

            var rawValue = reader.IsDbNull(ordinal) ? null : reader.GetValue(ordinal);
            yield return ConvertProjectionResult<TElement>(ConvertReaderValue(rawValue, projection.ResultType));
        }
    }

    private static IEnumerable<TElement> ExecuteSqlRowProjection<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        QueryPlanProjection.SqlRow projection)
    {
        var select = new QueryPlanSqlBuilder(plan, dataSource).BuildSelect<TElement>();
        var sourceName = $"sql:{dataSource.Provider.DatabaseType}:row-projection";

        foreach (var reader in select.ReadReader())
        {
            var values = new object?[projection.Members.Count];
            for (var index = 0; index < projection.Members.Count; index++)
            {
                var member = projection.Members[index];
                var ordinal = reader.GetOrdinal(member.Name);
                if (TryReadConvertedSqlRowValue(
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
                values[index] = ConvertReaderValue(rawValue, member.Value.ClrType);
            }

            yield return CreateProjectionRow<TElement>(projection.Constructor, values);
        }
    }

    private static bool TryReadConvertedSqlRowValue(
        IDataLinqDataReader reader,
        QueryPlanValue value,
        int ordinal,
        string sourceName,
        out object? modelValue)
    {
        if (value is QueryPlanConvertedValue converted)
        {
            if (!TryReadConvertedSqlRowValue(
                    reader,
                    converted.Value,
                    ordinal,
                    sourceName,
                    out var innerValue))
            {
                modelValue = null;
                return false;
            }

            modelValue = ConvertReaderValue(innerValue, converted.TargetType);
            return true;
        }

        if (value is not QueryPlanColumnValue { Column.HasScalarConverter: true } columnValue)
        {
            modelValue = null;
            return false;
        }

        var canonicalValue = ProviderRowDecoder.DecodeCanonicalValue(
            reader,
            columnValue.Column,
            ordinal,
            sourceName);
        modelValue = ProviderRowMaterializer.MaterializeValue(
            columnValue.Column,
            canonicalValue,
            sourceName);
        modelValue = ConvertReaderValue(modelValue, columnValue.ClrType);
        return true;
    }

    private static TElement CreateProjectionRow<TElement>(ConstructorInfo constructor, IReadOnlyList<object?> values)
    {
        var parameters = constructor.GetParameters();
        if (parameters.Length != values.Count)
        {
            throw new QueryTranslationException(
                $"Projection constructor expects {parameters.Length} values, but the query plan supplied {values.Count}.");
        }

        var arguments = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
            arguments[index] = ConvertReaderValue(values[index], parameters[index].ParameterType);

        return ConvertProjectionResult<TElement>(constructor.Invoke(arguments));
    }

    private static int[][] GetJoinedPrimaryKeyOrdinals(IDataLinqDataReader reader, IReadOnlyList<QueryPlanSourceSlot> sources)
    {
        var ordinals = new int[sources.Count][];
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            ordinals[sourceIndex] = new int[source.Table.PrimaryKeyColumns.Length];
            for (var columnIndex = 0; columnIndex < ordinals[sourceIndex].Length; columnIndex++)
                ordinals[sourceIndex][columnIndex] = reader.GetOrdinal(QueryPlanSqlBuilder.GetJoinedPrimaryKeyAlias(sourceIndex, columnIndex));
        }

        return ordinals;
    }

    private static QueryPlanInvocation ReprojectAsEntity(QueryPlanInvocation plan, QueryPlanSourceSlot source)
    {
        var template = plan.Template;
        var entityTemplate = new QueryPlanTemplate(
            template.Sources,
            template.Operations,
            new QueryPlanProjection.Entity(source),
            template.Result,
            template.BindingDeclarations,
            template.Specialization);

        return QueryPlanInvocation.Bind(entityTemplate, plan.Values.Items);
    }

    private static IEnumerable<object?> ExecuteEntityRows(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan)
        => new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<object>()
            .Execute()
            .Cast<object?>();

    private static QueryPlanProjectionRecipe GetProjectionRecipe(QueryPlanProjection projection)
        => projection switch
        {
            QueryPlanProjection.Anonymous anonymous => anonymous.Recipe,
            QueryPlanProjection.ComputedRowLocal computed => computed.Recipe,
            QueryPlanProjection.JoinedRowLocal joined => joined.Recipe,
            _ => throw new QueryTranslationException(
                $"Projection '{projection.Kind}' does not define a normalized row-local execution recipe.")
        };

    private static void ValidateProjectionDisposition(
        QueryPlanProjection projection,
        ProjectionEvaluationOptions options)
    {
        if (projection.Disposition == QueryPlanProjectionDisposition.Unsupported)
        {
            throw new QueryTranslationException(
                $"Projection '{projection.Kind}' is an internal parser shape and cannot be executed as a final query projection.");
        }

        if (projection.Disposition == QueryPlanProjectionDisposition.SqlOnlyCompatibility &&
            (!options.AllowCompatibilityObjectConstruction ||
             !options.AllowCompatibilityMemberReflection))
        {
            throw new QueryTranslationException(
                $"Projection '{projection.Kind}' requires SQL-only compatibility execution and cannot execute in AOT-strict mode.");
        }
    }

    private static T ConvertProjectionResult<T>(object? value)
    {
        if (value is null)
            return default!;

        if (value is T typed)
            return typed;

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static object? ConvertReaderValue(object? value, Type targetType)
    {
        if (value is DBNull)
            value = null;

        if (value is null)
            return null;

        var nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableTarget.IsInstanceOfType(value))
            return value;

        if (nonNullableTarget.IsEnum)
        {
            return value is string stringValue
                ? Enum.Parse(nonNullableTarget, stringValue, ignoreCase: false)
                : Enum.ToObject(nonNullableTarget, value);
        }

        return Convert.ChangeType(value, nonNullableTarget, CultureInfo.InvariantCulture);
    }

    private static TResult ConvertScalarResult<TResult>(object? result, QueryPlanResult planResult)
    {
        if (result is DBNull)
            result = null;

        if (planResult.Kind == QueryPlanResultKind.Any)
            return (TResult)(object)(Convert.ToInt64(result ?? 0, CultureInfo.InvariantCulture) > 0);

        if (result is null)
        {
            if (planResult.Kind == QueryPlanResultKind.Sum || Nullable.GetUnderlyingType(typeof(TResult)) is not null)
                return default!;

            throw new InvalidOperationException($"Scalar query plan result '{planResult.Kind}' returned no value.");
        }

        var targetType = Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult);
        if (targetType.IsInstanceOfType(result))
            return (TResult)result;

        return (TResult)Convert.ChangeType(result, targetType, CultureInfo.InvariantCulture);
    }
}
