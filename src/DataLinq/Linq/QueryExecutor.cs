using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Exceptions;
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

            if (body is OrderByClause orderBy)
            {
                query.OrderBy(orderBy);
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

    private static Func<object?, T?> GetSelectFunc<T>(SelectClause selectClause)
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

            //var param = Expression.Parameter(typeof(object), "x");
            //Expression body = typeof(T) == typeof(object)
            //    ? param
            //    : Expression.Convert(param, typeof(T));
            //return Expression.Lambda<Func<object?, T?>>(body, param).Compile();
        }

        // Handle member access (single or chained) by rebuilding lambda with correct root parameter cast
        if (selector is MemberExpression memberExpression)
        {
            // Find ultimate root expression (should be the query source parameter)
            Expression root = memberExpression;
            while (root is MemberExpression m && m.Expression != null)
            {
                root = m.Expression;
            }

            var entityType = root.Type; // Expected type of the row instance
            var param = Expression.Parameter(typeof(object), "x");
            var castParam = Expression.Convert(param, entityType);

            // Rebuild the member access chain replacing original root with castParam
            Expression rebuilt = ReplaceRoot(memberExpression, root, castParam);

            // Ensure resulting type matches T (apply conversion if needed)
            if (rebuilt.Type != typeof(T))
            {
                // Attempt implicit conversion (handles Nullable<T> -> T etc.)
                if (Nullable.GetUnderlyingType(rebuilt.Type) == typeof(T))
                {
                    // Access .Value for Nullable<T>
                    var valueProp = rebuilt.Type.GetProperty("Value");
                    if (valueProp != null)
                        rebuilt = Expression.Property(rebuilt, valueProp);
                }
                else
                {
                    rebuilt = Expression.Convert(rebuilt, typeof(T));
                }
            }

            var lambda = Expression.Lambda<Func<object?, T?>>(rebuilt, param);
            return lambda.Compile();
        }
        else if (selector.NodeType == ExpressionType.New && selector is NewExpression newExpression)
        {
            if (newExpression.Arguments.Count == 0)
                throw new QueryTranslationException($"Projection constructor '{newExpression}' has no arguments. Expression: {selectClause.Selector}");

            if (newExpression.Arguments[0] is not MemberExpression argumentExpression || argumentExpression.Expression == null)
                throw new QueryTranslationException($"Projection argument '{newExpression.Arguments[0]}' is not supported. Expression: {selectClause.Selector}");

            var memberType = argumentExpression.Expression.Type;
            var param = Expression.Parameter(typeof(object));
            var convert = Expression.Convert(param, memberType);

            var arguments = newExpression.Arguments.Select(x =>
            {
                if (x is not MemberExpression memberExp)
                    throw new QueryTranslationException($"Projection argument '{x}' is not supported. Expression: {selectClause.Selector}");

                return Expression.MakeMemberAccess(convert, memberExp.Member);
            });

            if (newExpression.Constructor == null)
                throw new QueryTranslationException($"Projection constructor '{newExpression}' is not supported. Expression: {selectClause.Selector}");

            var expression = Expression.New(newExpression.Constructor, arguments, newExpression.Members);
            var lambda = Expression.Lambda<Func<object?, T?>>(expression, param);
            return lambda.Compile();
        }

        throw new QueryTranslationException($"Selector expression '{selectClause.Selector}' with node type '{selectClause.Selector.NodeType}' is not supported.");

        // Local helper to rebuild chain replacing root
        static Expression ReplaceRoot(MemberExpression memberExpr, Expression originalRoot, Expression newRoot)
        {
            if (memberExpr.Expression == originalRoot)
            {
                return Expression.MakeMemberAccess(newRoot, memberExpr.Member);
            }
            if (memberExpr.Expression is MemberExpression inner)
            {
                var replacedInner = ReplaceRoot(inner, originalRoot, newRoot);
                return Expression.MakeMemberAccess(replacedInner, memberExpr.Member);
            }
            return memberExpr; // Fallback (should not occur for expected patterns)
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
        return ParseQueryModel<T>(queryModel)
            .Execute()
            .Select(GetSelectFunc<T>(queryModel.SelectClause));
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
            .Select(GetSelectFunc<T>(queryModel.SelectClause));

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
