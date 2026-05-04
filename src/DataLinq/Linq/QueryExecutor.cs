using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace DataLinq.Linq;

internal static class SelectFuncCache<T>
{
    // Cache for the "Identity" function (Select(x => x))
    public static readonly Func<object?, T?> Identity = x => (T?)x;

    // Cache for complex projections
    // Note: Expression equality is complex. For now, we can optimize the Identity case 
    // which covers 90% of DataLinq usage (fetching entities).
}

/// <summary>
/// The QueryExecutor class is responsible for converting a QueryModel into SQL queries and executing them
/// against a database using a provided Transaction context and TableMetadata.
/// </summary>
/// <remarks>
/// This class serves as a critical component in the LINQ to SQL translation and execution process.
/// It handles the parsing and execution of different query types such as collections, single entities,
/// and scalar values. It also applies result operators to the queries.
/// 
/// TODO: Consider implementing async versions of the Execute methods to improve performance
/// on I/O bound database operations and to allow for scalability in applications requiring asynchronous processing.
/// </remarks>
internal class QueryExecutor : IQueryExecutor
{
    /// <summary>
    /// Initializes a new instance of the QueryExecutor class with the specified transaction and table metadata.
    /// </summary>
    internal QueryExecutor(DataSourceAccess transaction, TableDefinition table)
    {
        this.Transaction = transaction;
        this.Table = table;
    }

    /// <summary>Gets the transaction associated with the query executor.</summary>
    private DataSourceAccess Transaction { get; }

    /// <summary>Gets the metadata for the table that queries will be executed against.</summary>
    private TableDefinition Table { get; }

    private static QueryModel? ExtractQueryModel(Expression? expression)
    {
        switch (expression)
        {
            case SubQueryExpression subQueryExpression:
                return subQueryExpression.QueryModel;

            case MemberExpression memberExpression:
                return QueryExecutor.ExtractQueryModel(memberExpression.Expression);

            case MethodCallExpression methodCallExpression:
                foreach (var argument in methodCallExpression.Arguments)
                {
                    var subQuery = QueryExecutor.ExtractQueryModel(argument);
                    if (subQuery != null)
                        return subQuery;
                }
                break;

            case UnaryExpression unaryExpression:
                return QueryExecutor.ExtractQueryModel(unaryExpression.Operand);

            case ConstantExpression constantExpression when constantExpression.Value is IQueryable queryable:
                return null;
        }

        return null;
    }

    /// <summary>
    /// Parses the provided QueryModel into a SQL query.
    /// </summary>
    /// <remarks>
    /// The method parses through body clauses and result operators to construct a SQL query.
    /// It uses the Where and OrderBy clauses to form the SQL conditions and ordering.
    /// </remarks>
    private Select<T> ParseQueryModel<T>(QueryModel queryModel)
    {
        return BuildSqlQuery<T>(queryModel).SelectQuery();
    }

    private SqlQuery<T> BuildSqlQuery<T>(QueryModel queryModel)
    {
        // Extract the subquery model from the main clause if necessary
        var subQueryModel = ExtractQueryModel(queryModel.MainFromClause.FromExpression);
        var query = subQueryModel != null
            ? BuildSqlQuery<T>(subQueryModel)
            : new SqlQuery<T>(Table, Transaction);

        foreach (var body in queryModel.BodyClauses)
        {
            if (body is WhereClause where)
            {
                query.Where(where);
            }
            else if (body is OrderByClause orderBy)
            {
                query.OrderBy(orderBy);
            }
            else if (body is JoinClause or GroupJoinClause)
            {
                throw new QueryTranslationException($"Join body clause '{body.GetType().Name}' is only supported for collection projection queries. Query model: {queryModel}");
            }
        }

        foreach (var op in queryModel.ResultOperators)
        {
            if (op is TakeResultOperator takeOperator && takeOperator.Count is ConstantExpression takeExpression)
            {
                query.Limit((int)takeExpression.Value!);
            }
            else if (op is SkipResultOperator skipOperator && skipOperator.Count is ConstantExpression skipExpression)
            {
                query.Offset((int)skipExpression.Value!);
            }

            if (op is SingleResultOperator)
                query.Limit(2);
            else if (op is FirstResultOperator)
                query.Limit(1);
            else if (op is AnyResultOperator)
                query.Limit(1);
        }

        return query;
    }

    private static Func<object?, T?> GetSelectFunc<T>(SelectClause selectClause, TableDefinition table)
    {
        if (selectClause == null)
            throw new ArgumentNullException(nameof(selectClause));

        // Optimization: If selecting the entity itself (QuerySourceReferenceExpression), return cached Identity
        // This removes JIT compilation for standard queries
        if (selectClause.Selector is QuerySourceReferenceExpression)
        {
            // If T is the Model type, just cast.
            return SelectFuncCache<T>.Identity;
        }

        // Normalize selector: unwrap Convert/Quote nodes commonly introduced by nullable/value conversions
        Expression selector = selectClause.Selector;
        while (selector is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked || unary.NodeType == ExpressionType.Quote))
        {
            selector = unary.Operand;
        }

        // NEW: Handle selecting the full entity (QuerySourceReferenceExpression) e.g. source.SingleOrDefault(x => ...)
        // Optimization: If selecting the entity itself (QuerySourceReferenceExpression), return cached Identity
        // This removes JIT compilation for standard queries
        if (selector is QuerySourceReferenceExpression)
        {
            // If T is the Model type, just cast.
            return SelectFuncCache<T>.Identity;

        }

        ValidateProjectionExpression(selector, table, selectClause);
        var querySources = GetReferencedQuerySources(selector);

        return value =>
        {
            var querySourceValues = querySources.ToDictionary(source => source, _ => value);
            var projected = ProjectionExpressionEvaluator.Evaluate(selector, querySourceValues);
            return ConvertProjectionResult<T>(projected);
        };
    }

    private static void ValidateProjectionExpression(Expression selector, TableDefinition table, SelectClause selectClause)
        => ValidateProjectionExpression(selector, new Dictionary<Type, TableDefinition>
        {
            [table.Model.CsType.Type!] = table
        }, selectClause);

    private static void ValidateProjectionExpression(Expression selector, IReadOnlyDictionary<Type, TableDefinition> tablesByType, SelectClause selectClause)
    {
        try
        {
            new ProjectionValidationVisitor(tablesByType, selectClause).Visit(selector);
        }
        catch (QueryTranslationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new QueryTranslationException($"Selector expression '{selectClause.Selector}' is not supported for LINQ Select projection.", exception);
        }
    }

    private sealed class JoinQuerySource(IQuerySource querySource, TableDefinition table, string alias)
    {
        public IQuerySource QuerySource { get; } = querySource;
        public TableDefinition Table { get; } = table;
        public string Alias { get; } = alias;
    }

    private sealed class ProjectionValidationVisitor(
        IReadOnlyDictionary<Type, TableDefinition> tablesByType,
        SelectClause selectClause) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is QuerySourceReferenceExpression)
                return node;

            if (node is SubQueryExpression)
                throw new QueryTranslationException($"Subquery projection '{node}' is not supported in LINQ Select projection. Expression: {selectClause.Selector}");

            return base.VisitExtension(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (IsRelationPropertyAccess(node))
            {
                throw new QueryTranslationException(
                    $"Relation property '{node.Member.Name}' is not supported in LINQ Select projection. " +
                    "Projection expressions are evaluated after materializing the selected rows; load relation data explicitly after ToList(). " +
                    $"Expression: {selectClause.Selector}");
            }

            return base.VisitMember(node);
        }

        private bool IsRelationPropertyAccess(MemberExpression node)
        {
            if (!TryGetQuerySourceType(node.Expression, out var sourceType))
                return false;

            return tablesByType.TryGetValue(sourceType, out var table) &&
                   table.Model.RelationProperties.ContainsKey(node.Member.Name);
        }

        private static bool TryGetQuerySourceType(Expression? expression, out Type sourceType)
        {
            switch (expression)
            {
                case QuerySourceReferenceExpression querySource:
                    sourceType = querySource.Type;
                    return true;

                case MemberExpression memberExpression:
                    return TryGetQuerySourceType(memberExpression.Expression, out sourceType);

                case MethodCallExpression methodCallExpression:
                    if (TryGetQuerySourceType(methodCallExpression.Object, out sourceType))
                        return true;

                    foreach (var argument in methodCallExpression.Arguments)
                    {
                        if (TryGetQuerySourceType(argument, out sourceType))
                            return true;
                    }

                    break;

                case UnaryExpression unaryExpression:
                    return TryGetQuerySourceType(unaryExpression.Operand, out sourceType);
            }

            sourceType = null!;
            return false;
        }
    }

    /// <summary>
    /// Executes the query represented by the QueryModel as a collection of objects of type T.
    /// </summary>
    /// <remarks>
    /// This method performs the actual execution of the SQL query and maps the result set
    /// to a collection of objects of the specified type using a projection function.
    /// </remarks>
    public IEnumerable<T?> ExecuteCollection<T>(QueryModel queryModel)
    {
        if (queryModel.BodyClauses.Any(static body => body is JoinClause or GroupJoinClause))
            return ExecuteJoinedCollection<T>(queryModel);

        return ParseQueryModel<T>(queryModel)
            .Execute()
            .Select(GetSelectFunc<T>(queryModel.SelectClause, Table));
    }

    private IEnumerable<T?> ExecuteJoinedCollection<T>(QueryModel queryModel)
    {
        if (queryModel.BodyClauses.Any(static body => body is GroupJoinClause))
            throw new QueryTranslationException($"GroupJoin is not supported. Use a simple inner Join with direct member keys. Query model: {queryModel}");

        var joinClauses = queryModel.BodyClauses.OfType<JoinClause>().ToArray();
        if (joinClauses.Length != 1)
            throw new QueryTranslationException($"Only a single explicit inner Join is supported. Query model: {queryModel}");

        if (queryModel.BodyClauses.Any(static body => body is not JoinClause))
            throw new QueryTranslationException($"Join queries currently support only the Join body clause. Filtering, ordering, and additional from clauses over joins are not supported yet. Query model: {queryModel}");

        if (queryModel.ResultOperators.Any())
            throw new QueryTranslationException($"Result operators over explicit Join queries are not supported yet. Query model: {queryModel}");

        var joinClause = joinClauses[0];
        var rootSource = new JoinQuerySource(queryModel.MainFromClause, Table, "t0");
        var innerSource = new JoinQuerySource(joinClause, GetTableForModelType(joinClause.ItemType), "t1");
        var sources = new[] { rootSource, innerSource };

        ValidateJoinInnerSequence(joinClause);

        var outerColumn = GetJoinKeyColumn(rootSource, joinClause.OuterKeySelector, "outer");
        var innerColumn = GetJoinKeyColumn(innerSource, joinClause.InnerKeySelector, "inner");

        var query = new SqlQuery<T>(Table, Transaction, rootSource.Alias);
        query.Join(innerSource.Table.DbName, innerSource.Alias)
            .On(on => on.Where(outerColumn.DbName, rootSource.Alias).EqualToColumn(innerColumn.DbName, innerSource.Alias));

        var select = query.SelectQuery();
        select.What(GetJoinedPrimaryKeySelectors(query, sources).ToArray());

        var projector = GetJoinedSelectFunc<T>(queryModel.SelectClause, sources);
        foreach (var reader in select.ReadReader())
        {
            var rows = new object?[sources.Length];
            for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                var key = ReadJoinedPrimaryKey(reader, source, sourceIndex);
                rows[sourceIndex] = Transaction.Provider.GetTableCache(source.Table).GetRow(key, Transaction)
                    ?? throw new InvalidOperationException($"Joined row for table '{source.Table.DbName}' and key '{key}' could not be materialized.");
            }

            yield return projector(rows);
        }
    }

    private TableDefinition GetTableForModelType(Type modelType)
    {
        return Transaction.Provider.Metadata.TableModels
            .SingleOrDefault(x => x.Model.CsType.Type == modelType)
            ?.Table
            ?? throw new QueryTranslationException($"Join inner sequence type '{modelType}' is not a mapped DataLinq table model.");
    }

    private static void ValidateJoinInnerSequence(JoinClause joinClause)
    {
        if (joinClause.InnerSequence is not ConstantExpression { Value: IQueryable })
            throw new QueryTranslationException($"Join inner sequence '{joinClause.InnerSequence}' is not supported. Only direct DataLinq query sources are supported.");
    }

    private static ColumnDefinition GetJoinKeyColumn(JoinQuerySource source, Expression keySelector, string side)
    {
        var selector = UnwrapConvert(keySelector);
        if (selector is MemberExpression { Member.Name: "Value", Expression: MemberExpression nullableMember } &&
            Nullable.GetUnderlyingType(nullableMember.Type) is not null)
        {
            selector = nullableMember;
        }

        if (selector is not MemberExpression memberExpression ||
            memberExpression.Expression is not QuerySourceReferenceExpression querySource ||
            !ReferenceEquals(querySource.ReferencedQuerySource, source.QuerySource))
        {
            throw new QueryTranslationException($"The {side} Join key selector '{keySelector}' is not supported. Only direct member keys and nullable Value member keys are supported.");
        }

        return source.Table.Columns.SingleOrDefault(x => x.ValueProperty.PropertyName == memberExpression.Member.Name)
            ?? throw new QueryTranslationException($"The {side} Join key member '{memberExpression.Member.Name}' is not mapped on table '{source.Table.DbName}'.");
    }

    private static IEnumerable<string> GetJoinedPrimaryKeySelectors<T>(SqlQuery<T> query, IReadOnlyList<JoinQuerySource> sources)
    {
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            for (var columnIndex = 0; columnIndex < source.Table.PrimaryKeyColumns.Length; columnIndex++)
            {
                var column = source.Table.PrimaryKeyColumns[columnIndex];
                yield return $"{source.Alias}.{query.EscapeCharacter}{column.DbName}{query.EscapeCharacter} AS {query.EscapeCharacter}{GetJoinedPrimaryKeyAlias(sourceIndex, columnIndex)}{query.EscapeCharacter}";
            }
        }
    }

    private static IKey ReadJoinedPrimaryKey(IDataLinqDataReader reader, JoinQuerySource source, int sourceIndex)
    {
        var values = source.Table.PrimaryKeyColumns
            .Select((column, columnIndex) => reader.GetValue<object>(column, reader.GetOrdinal(GetJoinedPrimaryKeyAlias(sourceIndex, columnIndex))))
            .ToArray();

        return KeyFactory.CreateKeyFromValues(values);
    }

    private static string GetJoinedPrimaryKeyAlias(int sourceIndex, int columnIndex)
        => $"dl_{sourceIndex}_pk_{columnIndex}";

    private static Func<object?[], T?> GetJoinedSelectFunc<T>(SelectClause selectClause, IReadOnlyList<JoinQuerySource> sources)
    {
        var selector = UnwrapConvert(selectClause.Selector);
        var sourceIndexes = sources
            .Select((source, index) => (source.QuerySource, index))
            .ToDictionary(x => x.QuerySource, x => x.index);
        var tablesByType = sources.ToDictionary(x => x.Table.Model.CsType.Type!, x => x.Table);

        ValidateProjectionExpression(selector, tablesByType, selectClause);

        return rows =>
        {
            var querySourceValues = sourceIndexes.ToDictionary(x => x.Key, x => rows[x.Value]);
            var projected = ProjectionExpressionEvaluator.Evaluate(selector, querySourceValues);
            return ConvertProjectionResult<T>(projected);
        };
    }

    private static IReadOnlyList<IQuerySource> GetReferencedQuerySources(Expression expression)
    {
        var visitor = new QuerySourceCollector();
        visitor.Visit(expression);
        return visitor.QuerySources;
    }

    private static T? ConvertProjectionResult<T>(object? value)
    {
        if (value is null)
            return default;

        if (value is T typed)
            return typed;

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private sealed class QuerySourceCollector : ExpressionVisitor
    {
        private readonly List<IQuerySource> querySources = [];

        public IReadOnlyList<IQuerySource> QuerySources => querySources;

        protected override Expression VisitExtension(Expression node)
        {
            if (node is QuerySourceReferenceExpression querySourceReference)
            {
                if (!querySources.Contains(querySourceReference.ReferencedQuerySource))
                    querySources.Add(querySourceReference.ReferencedQuerySource);

                return node;
            }

            return node.CanReduce ? Visit(node.Reduce()) : node;
        }
    }

    /// <summary>
    /// Executes the query represented by the QueryModel and returns a single object of type T.
    /// </summary>
    /// <remarks>
    /// This method caters to executing queries that are expected to return a single result.
    /// It applies the correct result operator based on the QueryModel's specifications.
    /// 
    /// TODO: Introduce error handling to provide more informative exceptions when queries
    /// do not behave as expected, e.g., when a Single() query returns multiple results.
    /// </remarks>
    public T? ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
    {
        if (queryModel.ResultOperators.FirstOrDefault() is SumResultOperator or MinResultOperator or MaxResultOperator or AverageResultOperator)
            return ExecuteScalar<T>(queryModel);

        var sequence = ParseQueryModel<T>(queryModel)
            .Execute()
            .Select(GetSelectFunc<T>(queryModel.SelectClause, Table));

        if (queryModel.ResultOperators.Any())
        {
            return queryModel.ResultOperators[0] switch
            {
                SingleResultOperator => returnDefaultWhenEmpty ? sequence.SingleOrDefault() : sequence.Single(),
                FirstResultOperator => returnDefaultWhenEmpty ? sequence.FirstOrDefault() : sequence.First(),
                LastResultOperator => returnDefaultWhenEmpty ? sequence.LastOrDefault() : sequence.Last(),
                var op => throw new QueryTranslationException($"Single-result operator '{op.GetType().Name}' is not supported. Query model: {queryModel}")
            };
        }

        throw new QueryTranslationException($"Single-result execution requires a result operator. Query model: {queryModel}");
    }

    /// <summary>
    /// Executes the query represented by the QueryModel and returns a scalar value of type T.
    /// </summary>
    /// <remarks>
    /// This method is used for queries that are expected to return a single scalar value, such as counts or existence checks.
    /// 
    /// TODO: Investigate the possibility of extending support for more complex aggregate functions and
    /// grouping operations to enhance the capability of scalar queries.
    /// </remarks>
    public T ExecuteScalar<T>(QueryModel queryModel)
    {
        if (queryModel.ResultOperators.Any())
        {
            var select = ParseQueryModel<T>(queryModel);
            var op = queryModel.ResultOperators[0];

            if (op is CountResultOperator || op is AnyResultOperator)
            {
                select.What("COUNT(*)");
            }
            else if (op is SumResultOperator || op is MinResultOperator || op is MaxResultOperator || op is AverageResultOperator)
            {
                select.What(GetAggregateSelectorSql(select.Query, queryModel.SelectClause, op));
            }
            else
            {
                throw new QueryTranslationException($"Scalar result operator '{op.GetType().Name}' is not supported. Query model: {queryModel}");
            }

            var result = select.ExecuteScalar();
            return ConvertScalarResult<T>(result, op, queryModel);
        }

        throw new QueryTranslationException($"Scalar execution requires a result operator. Query model: {queryModel}");
    }

    private static T ConvertScalarResult<T>(object? result, ResultOperatorBase op, QueryModel queryModel)
    {
        if (result is DBNull)
            result = null;

        if (op is AnyResultOperator)
            return (T)(object)(Convert.ToInt64(result ?? 0, CultureInfo.InvariantCulture) > 0);

        if (result is null)
        {
            if (op is SumResultOperator || Nullable.GetUnderlyingType(typeof(T)) is not null)
                return default!;

            throw new InvalidOperationException($"Scalar result operator '{op.GetType().Name}' returned no value. Query model: {queryModel}");
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType.IsInstanceOfType(result))
            return (T)result;

        return (T)Convert.ChangeType(result, targetType, CultureInfo.InvariantCulture);
    }

    private static string GetAggregateSelectorSql<T>(SqlQuery<T> query, SelectClause selectClause, ResultOperatorBase op)
    {
        var selector = UnwrapConvert(selectClause.Selector);
        var columnExpression = GetAggregateColumnExpression(query, selector, op);

        return op switch
        {
            SumResultOperator => $"COALESCE(SUM({columnExpression}), 0)",
            MinResultOperator => $"MIN({columnExpression})",
            MaxResultOperator => $"MAX({columnExpression})",
            AverageResultOperator => $"AVG({columnExpression})",
            _ => throw new QueryTranslationException($"Scalar result operator '{op.GetType().Name}' is not supported. Query model selector: {selectClause.Selector}")
        };
    }

    private static string GetAggregateColumnExpression<T>(SqlQuery<T> query, Expression selector, ResultOperatorBase op)
    {
        if (selector is not MemberExpression memberExpression)
            throw new QueryTranslationException($"Aggregate selector '{selector}' is not supported for '{op.GetType().Name}'. Only direct numeric members and nullable Value members are supported.");

        memberExpression = UnwrapNullableValueMember(memberExpression);

        if (memberExpression.Expression is not QuerySourceReferenceExpression)
            throw new QueryTranslationException($"Aggregate selector '{selector}' is not supported for '{op.GetType().Name}'. Only direct numeric members and nullable Value members are supported.");

        if (!IsNumericType(selector.Type))
            throw new QueryTranslationException($"Aggregate selector '{selector}' for '{op.GetType().Name}' must be numeric. Selector type: {selector.Type}");

        var column = query.Table.Columns.SingleOrDefault(x => x.ValueProperty.PropertyName == memberExpression.Member.Name)
            ?? throw new QueryTranslationException($"Aggregate selector member '{memberExpression.Member.Name}' was not found on table '{query.Table.DbName}'. Selector: {selector}");

        return $"{query.EscapeCharacter}{column.DbName}{query.EscapeCharacter}";
    }

    private static MemberExpression UnwrapNullableValueMember(MemberExpression memberExpression)
    {
        if (memberExpression.Member.Name == nameof(Nullable<int>.Value) &&
            memberExpression.Expression is MemberExpression innerMember &&
            Nullable.GetUnderlyingType(innerMember.Type) is not null)
        {
            return innerMember;
        }

        return memberExpression;
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked || unary.NodeType == ExpressionType.Quote))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
            return false;

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte or
            TypeCode.SByte or
            TypeCode.Int16 or
            TypeCode.UInt16 or
            TypeCode.Int32 or
            TypeCode.UInt32 or
            TypeCode.Int64 or
            TypeCode.UInt64 or
            TypeCode.Single or
            TypeCode.Double or
            TypeCode.Decimal => true,
            _ => false
        };
    }
}
