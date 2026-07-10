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
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Linq.Planning.Sql;

namespace DataLinq.Linq.Planning.Expressions;

internal sealed class ExpressionQueryPlanProvider : IQueryProvider
{
    private readonly DatabaseDefinition metadata;
    private readonly DataSourceAccess? dataSource;

    public ExpressionQueryPlanProvider(DatabaseDefinition metadata)
    {
        this.metadata = metadata;
    }

    private ExpressionQueryPlanProvider(DataSourceAccess dataSource)
    {
        this.dataSource = dataSource;
        metadata = dataSource.Provider.Metadata;
    }

    public static ExpressionQueryPlanProvider ForExecution(DataSourceAccess dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        return new ExpressionQueryPlanProvider(dataSource);
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
        if (dataSource is not null &&
            ExpressionQueryPlanExecutor.TryExecuteTerminalPrimaryKeyExpression(
                dataSource,
                metadata,
                expression,
                out TResult primaryKeyResult))
        {
            return primaryKeyResult;
        }

        var plan = Parse(expression, typeof(TResult));
        return ExpressionQueryPlanExecutor.Execute<TResult>(GetDataSource(), plan, expression);
    }

    public IEnumerable<TElement> ExecuteEnumerable<TElement>(Expression expression)
    {
        var plan = Parse(expression, typeof(TElement));
        return ExpressionQueryPlanExecutor.ExecuteEnumerable<TElement>(GetDataSource(), plan, expression);
    }

    public QueryPlanInvocation Parse(Expression expression, Type resultType)
        => ExpressionQueryPlanParser.Convert(metadata, expression, resultType);

    private DataSourceAccess GetDataSource()
        => dataSource ?? throw new NotSupportedException("The DataLinq expression plan provider was created for parsing only and cannot execute queries.");
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
    internal static bool TryExecuteTerminalPrimaryKeyExpression<TResult>(
        DataSourceAccess dataSource,
        DatabaseDefinition metadata,
        Expression expression,
        out TResult result)
    {
        result = default!;

        if (!TryGetTerminalScalarPrimaryKeyExpression(
                metadata,
                expression,
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
        QueryPlanInvocation plan,
        Expression expression)
    {
        var template = plan.Template;
        if (template.Result.Kind != QueryPlanResultKind.Sequence)
            throw new QueryTranslationException($"Expression parser route expected a sequence result, but the plan result is '{template.Result.Kind}'.");

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

        return ExecuteProjectedSequence<TElement>(dataSource, plan, expression);
    }

    public static TResult Execute<TResult>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        Expression expression)
    {
        return plan.Template.Result.Kind switch
        {
            QueryPlanResultKind.Count or
            QueryPlanResultKind.Any or
            QueryPlanResultKind.Sum or
            QueryPlanResultKind.Min or
            QueryPlanResultKind.Max or
            QueryPlanResultKind.Average => ExecuteScalar<TResult>(dataSource, plan),
            QueryPlanResultKind.First => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.First()),
            QueryPlanResultKind.FirstOrDefault => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.FirstOrDefault()),
            QueryPlanResultKind.Single => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.Single()),
            QueryPlanResultKind.SingleOrDefault => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.SingleOrDefault()),
            QueryPlanResultKind.Last => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.Last()),
            QueryPlanResultKind.LastOrDefault => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.LastOrDefault()),
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

    private static bool TryGetTerminalScalarPrimaryKeyExpression(
        DatabaseDefinition metadata,
        Expression expression,
        out TableDefinition table,
        out object? primaryKey,
        out QueryPlanResultKind resultKind)
    {
        table = null!;
        primaryKey = null;
        resultKind = default;

        expression = UnwrapConvert(expression);
        if (expression is not MethodCallExpression methodCall ||
            !IsQueryableMethod(methodCall) ||
            methodCall.Arguments.Count != 2 ||
            !TryGetTerminalResultKind(methodCall.Method.Name, out resultKind))
        {
            return false;
        }

        if (!TryGetRootTable(metadata, methodCall.Arguments[0], out table) ||
            !table.PrimaryKeyShape.SupportsScalarProviderKeyStore)
        {
            return false;
        }

        if (!TryUnwrapLambda(methodCall.Arguments[1], out var predicate) ||
            predicate.Parameters.Count != 1)
        {
            return false;
        }

        var primaryKeyColumn = table.PrimaryKeyColumns[0];
        return TryGetScalarPrimaryKeyExpressionValue(
            predicate.Body,
            predicate.Parameters[0],
            table,
            primaryKeyColumn,
            out primaryKey);
    }

    private static bool TryGetTerminalResultKind(string methodName, out QueryPlanResultKind resultKind)
    {
        resultKind = methodName switch
        {
            nameof(Queryable.Single) => QueryPlanResultKind.Single,
            nameof(Queryable.SingleOrDefault) => QueryPlanResultKind.SingleOrDefault,
            nameof(Queryable.First) => QueryPlanResultKind.First,
            nameof(Queryable.FirstOrDefault) => QueryPlanResultKind.FirstOrDefault,
            _ => default
        };

        return resultKind != default;
    }

    private static bool TryGetRootTable(
        DatabaseDefinition metadata,
        Expression expression,
        out TableDefinition table)
    {
        table = null!;
        expression = UnwrapConvert(expression);

        if (expression is not ConstantExpression { Value: IQueryable queryable })
            return false;

        if (!metadata.TryGetTableModel(queryable.ElementType, out var model))
            return false;

        table = model.Table;
        return true;
    }

    private static bool TryGetScalarPrimaryKeyExpressionValue(
        Expression expression,
        ParameterExpression parameter,
        TableDefinition table,
        ColumnDefinition primaryKeyColumn,
        out object? primaryKey)
    {
        primaryKey = null;

        expression = UnwrapConvert(expression);
        if (expression is not BinaryExpression { NodeType: ExpressionType.Equal } binary)
            return false;

        if (IsPrimaryKeyColumnExpression(binary.Left, parameter, table, primaryKeyColumn) &&
            TryEvaluateLocalScalar(binary.Right, parameter, out primaryKey))
        {
            return true;
        }

        if (IsPrimaryKeyColumnExpression(binary.Right, parameter, table, primaryKeyColumn) &&
            TryEvaluateLocalScalar(binary.Left, parameter, out primaryKey))
        {
            return true;
        }

        return false;
    }

    private static bool IsPrimaryKeyColumnExpression(
        Expression expression,
        ParameterExpression parameter,
        TableDefinition table,
        ColumnDefinition primaryKeyColumn)
    {
        expression = UnwrapQueryColumnAccess(expression);
        return expression is MemberExpression memberExpression &&
            ReferenceEquals(memberExpression.Expression, parameter) &&
            table.TryGetColumnByPropertyName(memberExpression.Member.Name, out var column) &&
            ReferenceEquals(column, primaryKeyColumn);
    }

    private static bool TryEvaluateLocalScalar(
        Expression expression,
        ParameterExpression parameter,
        out object? value)
    {
        expression = UnwrapConvert(expression);

        switch (expression)
        {
            case ConstantExpression constant:
                value = constant.Value;
                return true;

            case MemberExpression memberExpression
                when TryEvaluateMemberInstance(memberExpression.Expression, parameter, out var instance):
                return TryGetMemberValue(memberExpression.Member, instance, out value);

            default:
                value = null;
                return false;
        }
    }

    private static bool TryEvaluateMemberInstance(
        Expression? expression,
        ParameterExpression parameter,
        out object? instance)
    {
        if (expression is null)
        {
            instance = null;
            return true;
        }

        if (ReferenceEquals(expression, parameter))
        {
            instance = null;
            return false;
        }

        return TryEvaluateLocalScalar(expression, parameter, out instance);
    }

    private static bool TryGetMemberValue(MemberInfo member, object? instance, out object? value)
    {
        switch (member)
        {
            case FieldInfo field:
                value = field.GetValue(instance);
                return true;

            case PropertyInfo property:
                value = property.GetValue(instance);
                return true;

            default:
                value = null;
                return false;
        }
    }

    private static bool TryUnwrapLambda(Expression expression, out LambdaExpression lambda)
    {
        expression = UnwrapConvert(expression);
        if (expression is LambdaExpression directLambda)
        {
            lambda = directLambda;
            return true;
        }

        if (expression is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quotedLambda })
        {
            lambda = quotedLambda;
            return true;
        }

        lambda = null!;
        return false;
    }

    private static Expression UnwrapQueryColumnAccess(Expression expression)
    {
        expression = UnwrapConvert(expression);
        if (expression is MemberExpression { Member.Name: "Value", Expression: not null } memberExpression &&
            Nullable.GetUnderlyingType(memberExpression.Expression.Type) != null)
        {
            expression = memberExpression.Expression;
        }

        return UnwrapConvert(expression);
    }

    private static TResult ExecuteSingle<TResult>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        Expression expression,
        Func<IEnumerable<TResult>, TResult?> selector)
    {
        if (plan.Template.Projection is not QueryPlanProjection.Entity)
        {
            if (plan.Template.Projection is QueryPlanProjection.ScalarMember scalarMember)
                return selector(ExecuteScalarProjection<TResult>(dataSource, plan, scalarMember))!;

            if (plan.Template.Projection is QueryPlanProjection.SqlRow sqlRow)
                return selector(ExecuteSqlRowProjection<TResult>(dataSource, plan, sqlRow))!;

            return selector(ExecuteProjectedSequence<TResult>(dataSource, plan, expression))!;
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
        Expression expression)
    {
        var selector = GetProjectionLambda(expression);
        return plan.Template.Operations.Any(static operation => operation is QueryPlanOperation.Join)
            ? ExecuteJoinedProjection<TElement>(dataSource, plan, selector)
            : ExecuteSingleSourceProjection<TElement>(dataSource, plan, selector);
    }

    private static IEnumerable<TElement> ExecuteSingleSourceProjection<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        LambdaExpression selector)
    {
        if (selector.Parameters.Count != 1)
            throw new QueryTranslationException($"Projection selector '{selector}' is not supported for a single-source query.");

        var rootSource = plan.Template.Sources.First(static source => source.Kind == QueryPlanSourceKind.RootTable);
        var entityPlan = ReprojectAsEntity(plan, rootSource);
        foreach (var row in ExecuteEntityRows(dataSource, entityPlan))
            yield return ConvertProjectionResult<TElement>(
                ProjectionExpressionEvaluator.Evaluate(selector.Body, selector.Parameters[0], row));
    }

    private static IEnumerable<TElement> ExecuteJoinedProjection<TElement>(
        DataSourceAccess dataSource,
        QueryPlanInvocation plan,
        LambdaExpression selector)
    {
        var joinedSources = plan.Template.Sources
            .Where(static source => source.Kind is QueryPlanSourceKind.RootTable or QueryPlanSourceKind.ExplicitJoin)
            .OrderBy(static source => source.Id, StringComparer.Ordinal)
            .ToArray();

        if (selector.Parameters.Count != joinedSources.Length)
            throw new QueryTranslationException($"Join projection selector '{selector}' does not match the query plan source count.");

        var planSqlBuilder = new QueryPlanSqlBuilder(plan, dataSource);
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
            var parameterValues = new Dictionary<ParameterExpression, object?>(selector.Parameters.Count);
            for (var sourceIndex = 0; sourceIndex < joinedSources.Length; sourceIndex++)
            {
                var source = joinedSources[sourceIndex];
                parameterValues[selector.Parameters[sourceIndex]] = GetJoinedRow(dataSource, source, primaryKeysBySource[sourceIndex])
                    ?? throw new InvalidOperationException($"Joined row for table '{source.Table.DbName}' could not be materialized from its provider primary key.");
            }

            yield return ConvertProjectionResult<TElement>(
                ProjectionExpressionEvaluator.Evaluate(selector.Body, parameterValues));
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

        foreach (var reader in select.ReadReader())
        {
            var ordinal = reader.GetOrdinal(QueryPlanSqlBuilder.ScalarProjectionAlias);
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

    private static LambdaExpression GetProjectionLambda(Expression expression)
    {
        if (TryGetProjectionLambda(expression, out var selector))
            return selector;

        throw new QueryTranslationException(
            $"Projection expression '{expression}' is not supported by the DataLinq expression parser execution route.");
    }

    private static bool TryGetProjectionLambda(Expression expression, out LambdaExpression selector)
    {
        expression = UnwrapConvert(expression);
        if (expression is MethodCallExpression methodCall && IsQueryableMethod(methodCall))
        {
            if (methodCall.Method.Name == nameof(Queryable.Select) && methodCall.Arguments.Count == 2)
            {
                selector = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
                return true;
            }

            if (methodCall.Method.Name == nameof(Queryable.Join) && methodCall.Arguments.Count == 5)
            {
                selector = UnwrapLambda(methodCall.Arguments[4], methodCall.ToString());
                return true;
            }

            if (IsTerminalOperator(methodCall.Method.Name) && methodCall.Arguments.Count > 0)
                return TryGetProjectionLambda(methodCall.Arguments[0], out selector);

            if (IsProjectionPassthroughOperator(methodCall.Method.Name) && methodCall.Arguments.Count > 0)
                return TryGetProjectionLambda(methodCall.Arguments[0], out selector);
        }

        selector = null!;
        return false;
    }

    private static bool IsQueryableMethod(MethodCallExpression methodCall)
        => methodCall.Method.DeclaringType == typeof(Queryable);

    private static bool IsTerminalOperator(string methodName)
        => methodName is nameof(Queryable.Single) or
            nameof(Queryable.SingleOrDefault) or
            nameof(Queryable.First) or
            nameof(Queryable.FirstOrDefault) or
            nameof(Queryable.Last) or
            nameof(Queryable.LastOrDefault);

    private static bool IsProjectionPassthroughOperator(string methodName)
        => methodName is nameof(Queryable.Where) or
            nameof(Queryable.OrderBy) or
            nameof(Queryable.OrderByDescending) or
            nameof(Queryable.ThenBy) or
            nameof(Queryable.ThenByDescending) or
            nameof(Queryable.Skip) or
            nameof(Queryable.Take);

    private static LambdaExpression UnwrapLambda(Expression expression, string context)
    {
        expression = UnwrapConvert(expression);
        return expression switch
        {
            LambdaExpression lambda => lambda,
            UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lambda } => lambda,
            _ => throw new QueryTranslationException($"Lambda expression '{expression}' is not supported in {context}.")
        };
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert ||
                unary.NodeType == ExpressionType.ConvertChecked ||
                unary.NodeType == ExpressionType.Quote))
        {
            expression = unary.Operand;
        }

        return expression;
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
