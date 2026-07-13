using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
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
        var request = ValidatedQueryExecutionRequest.Prepare(CreateExecutionRequest(plan));
        return ExpressionQueryPlanExecutor.Execute<TResult>(request);
    }

    public IEnumerable<TElement> ExecuteEnumerable<TElement>(Expression expression)
    {
        var plan = Parse(expression, typeof(TElement));
        var request = ValidatedQueryExecutionRequest.Prepare(CreateExecutionRequest(plan));
        return ExpressionQueryPlanExecutor.ExecuteEnumerable<TElement>(request);
    }

    public QueryPlanInvocation Parse(Expression expression, Type resultType)
        => ExpressionQueryPlanParser.Convert(metadata, expression, resultType);

    private QueryExecutionRequest CreateExecutionRequest(QueryPlanInvocation invocation)
    {
        if (readSource is null)
        {
            throw new NotSupportedException(
                "The DataLinq expression plan provider was created for parsing only and cannot execute queries.");
        }

        return new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(readSource, CancellationToken.None));
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
    public static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        IDataLinqReadSource source,
        QueryPlanInvocation plan)
        => ExecuteEnumerable<TElement>(
            Prepare(source, plan),
            ProjectionEvaluationOptions.Default);

    internal static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        IDataLinqReadSource source,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions)
        => ExecuteEnumerable<TElement>(
            Prepare(source, plan),
            projectionOptions);

    internal static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        ValidatedQueryExecutionRequest request)
        => ExecuteEnumerable<TElement>(request, ProjectionEvaluationOptions.Default);

    private static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        ValidatedQueryExecutionRequest request,
        ProjectionEvaluationOptions projectionOptions)
    {
        var plan = request.Invocation;
        var template = plan.Template;
        if (template.Result.Kind != QueryPlanResultKind.Sequence)
            throw new QueryTranslationException($"Expression parser route expected a sequence result, but the plan result is '{template.Result.Kind}'.");

        ValidateProjectionDisposition(template.Projection, projectionOptions);

        if (template.Projection is QueryPlanProjection.Entity)
            return ExecuteEntitySequence<TElement>(request);

        var dataSource = GetSqlCompatibilityDataSource(request);

        if (template.Projection is QueryPlanProjection.GroupedAggregate groupedAggregate)
            return ExecuteGroupedAggregateProjection<TElement>(dataSource, plan, groupedAggregate);

        if (template.Projection is QueryPlanProjection.ScalarMember scalarMember)
            return ExecuteScalarProjection<TElement>(dataSource, plan, scalarMember);

        if (template.Projection is QueryPlanProjection.SqlRow sqlRow)
            return ExecuteSqlRowProjection<TElement>(dataSource, plan, sqlRow);

        return ExecuteProjectedSequence<TElement>(dataSource, plan, projectionOptions);
    }

    public static TResult Execute<TResult>(
        IDataLinqReadSource source,
        QueryPlanInvocation plan)
        => Execute<TResult>(Prepare(source, plan), ProjectionEvaluationOptions.Default);

    internal static TResult Execute<TResult>(
        IDataLinqReadSource source,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions)
        => Execute<TResult>(Prepare(source, plan), projectionOptions);

    internal static TResult Execute<TResult>(ValidatedQueryExecutionRequest request)
        => Execute<TResult>(request, ProjectionEvaluationOptions.Default);

    private static TResult Execute<TResult>(
        ValidatedQueryExecutionRequest request,
        ProjectionEvaluationOptions projectionOptions)
    {
        var plan = request.Invocation;
        ValidateProjectionDisposition(plan.Template.Projection, projectionOptions);

        if (plan.Template.Projection is QueryPlanProjection.Entity &&
            IsEntityTerminalResult(plan.Template.Result.Kind))
        {
            return ExecuteEntityTerminal<TResult>(request);
        }

        if (plan.Template.Result.IsScalarResult)
            return ExecuteScalar<TResult>(request);

        var dataSource = GetSqlCompatibilityDataSource(request);
        return plan.Template.Result.Kind switch
        {
            QueryPlanResultKind.First => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.First()),
            QueryPlanResultKind.FirstOrDefault => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.FirstOrDefault()),
            QueryPlanResultKind.Single => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.Single()),
            QueryPlanResultKind.SingleOrDefault => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.SingleOrDefault()),
            QueryPlanResultKind.Last => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.Last()),
            QueryPlanResultKind.LastOrDefault => ExecuteSingle<TResult>(dataSource, plan, projectionOptions, static sequence => sequence.LastOrDefault()),
            var kind => throw new QueryTranslationException($"Expression parser route cannot execute query plan result '{kind}'.")
        };
    }

    private static ValidatedQueryExecutionRequest Prepare(
        IDataLinqReadSource source,
        QueryPlanInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(source, CancellationToken.None)));
    }

    private static DataSourceAccess GetSqlCompatibilityDataSource(
        ValidatedQueryExecutionRequest request)
    {
        if (request.Backend is SqlQueryPlanBackend sqlBackend)
        {
            request.EnsureBackend(sqlBackend);
            if (!ReferenceEquals(request.Context.Source, sqlBackend.DataSource))
            {
                throw new InvalidOperationException(
                    "The SQL compatibility executor cannot use a backend bound to another read source.");
            }

            return sqlBackend.DataSource;
        }

        throw new NotSupportedException(
            $"Query backend '{request.Backend.Capabilities.BackendName}' does not support the retained SQL compatibility executor.");
    }

    private static IEnumerable<TElement> ExecuteEntitySequence<TElement>(
        ValidatedQueryExecutionRequest request)
    {
        using var cursor = request.Backend.OpenEntityCursor(request);
        while (cursor.MoveNext())
            yield return (TElement)(object)cursor.Current;
    }

    private static TResult ExecuteEntityTerminal<TResult>(
        ValidatedQueryExecutionRequest request)
    {
        if (request.Backend.TryExecuteTerminalEntity(request, out var optimizedResult))
        {
            return optimizedResult is null
                ? default!
                : (TResult)(object)optimizedResult;
        }

        var sequence = ExecuteEntitySequence<TResult>(request);
        return request.Invocation.Template.Result.Kind switch
        {
            // The backend receives the original First result shape and therefore bounds the
            // cursor to one row. Single forces the final MoveNext that records successful
            // completion in the underlying lazy SQL iterator instead of reporting early disposal.
            QueryPlanResultKind.First => sequence.Single(),
            QueryPlanResultKind.FirstOrDefault => sequence.SingleOrDefault()!,
            QueryPlanResultKind.Single => sequence.Single(),
            QueryPlanResultKind.SingleOrDefault => sequence.SingleOrDefault()!,
            QueryPlanResultKind.Last => sequence.Last(),
            QueryPlanResultKind.LastOrDefault => sequence.LastOrDefault()!,
            var kind => throw new QueryTranslationException(
                $"Expression parser route expected an entity terminal result, but the plan result is '{kind}'.")
        };
    }

    private static bool IsEntityTerminalResult(QueryPlanResultKind resultKind)
        => resultKind is QueryPlanResultKind.First or
            QueryPlanResultKind.FirstOrDefault or
            QueryPlanResultKind.Single or
            QueryPlanResultKind.SingleOrDefault or
            QueryPlanResultKind.Last or
            QueryPlanResultKind.LastOrDefault;

    private static TResult ExecuteSingle<TResult>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions,
        Func<IEnumerable<TResult>, TResult?> selector)
    {
        if (plan.Template.Projection is QueryPlanProjection.Entity)
        {
            throw new QueryTranslationException(
                "Entity terminal results must execute through the selected query backend.");
        }

        if (plan.Template.Projection is QueryPlanProjection.ScalarMember scalarMember)
            return selector(ExecuteScalarProjection<TResult>(dataSource, plan, scalarMember))!;

        if (plan.Template.Projection is QueryPlanProjection.SqlRow sqlRow)
            return selector(ExecuteSqlRowProjection<TResult>(dataSource, plan, sqlRow))!;

        return selector(ExecuteProjectedSequence<TResult>(dataSource, plan, projectionOptions))!;
    }

    private static TResult ExecuteScalar<TResult>(ValidatedQueryExecutionRequest request)
        => request.Backend.ExecuteScalar<TResult>(request);

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
        var sourceName = $"sql:{dataSource.Provider.DatabaseType}:grouped-projection";

        foreach (var reader in select.ReadReader())
        {
            var values = new object?[projection.Members.Count];
            for (var index = 0; index < projection.Members.Count; index++)
            {
                var member = projection.Members[index];
                // Grouped SELECT emits one selector per projection member in this order.
                // The slot ordinal prevents alias comparison from pairing the value with different column metadata.
                if (member.Value is QueryPlanGroupKeyValue groupKey &&
                    IsModelCompatibleConvertedColumnShape(groupKey.Key, groupKey.ClrType) &&
                    TryReadScalarConvertedColumnValue(
                        reader,
                        groupKey.Key,
                        index,
                        sourceName,
                        out var modelValue))
                {
                    values[index] = ConvertReaderValue(modelValue, groupKey.ClrType);
                    continue;
                }

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
                if (TryReadScalarConvertedColumnValue(
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

    private static bool TryReadScalarConvertedColumnValue(
        IDataLinqDataReader reader,
        QueryPlanValue value,
        int ordinal,
        string sourceName,
        out object? modelValue)
    {
        if (value is QueryPlanConvertedValue converted)
        {
            if (!TryReadScalarConvertedColumnValue(
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

    private static bool IsModelCompatibleConvertedColumnShape(
        QueryPlanValue value,
        Type resultType)
    {
        var leaf = value;
        while (leaf is QueryPlanConvertedValue converted)
            leaf = converted.Value;

        if (leaf is not QueryPlanColumnValue { Column.HasScalarConverter: true } columnValue)
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

}
