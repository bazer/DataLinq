using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
        // Extract the subquery model from the main clause if necessary
        var subQueryModel = ExtractQueryModel(queryModel.MainFromClause.FromExpression);
        if (subQueryModel != null)
        {
            // Recursively parse the subquery model
            return ParseQueryModel<T>(subQueryModel);
        }

        var query = new SqlQuery<T>(Table, Transaction);

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

            var opString = op.ToString();

            if (opString == "Single()")
                query.Limit(2);
            else if (opString == "SingleOrDefault()")
                query.Limit(2);
            else if (opString == "First()")
                query.Limit(1);
            else if (opString == "FirstOrDefault()")
                query.Limit(1);
            else if (opString == "Any()")
                query.Limit(1);
        }

        return query.SelectQuery();
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
                throw new NotImplementedException($"'{newExpression}' constructor without arguments is not implemented");

            if (newExpression.Arguments[0] is not MemberExpression argumentExpression || argumentExpression.Expression == null)
                throw new NotImplementedException($"'{newExpression.Arguments[0]}' argument is not implemented");

            var memberType = argumentExpression.Expression.Type;
            var param = Expression.Parameter(typeof(object));
            var convert = Expression.Convert(param, memberType);

            var arguments = newExpression.Arguments.Select(x =>
            {
                if (x is not MemberExpression memberExp)
                    throw new NotImplementedException($"'{x}' argument is not implemented");

                return Expression.MakeMemberAccess(convert, memberExp.Member);
            });

            if (newExpression.Constructor == null)
                throw new NotImplementedException($"'{newExpression}' constructor is not implemented");

            var expression = Expression.New(newExpression.Constructor, arguments, newExpression.Members);
            var lambda = Expression.Lambda<Func<object?, T?>>(expression, param);
            return lambda.Compile();
        }

        throw new NotImplementedException($"Unsupported selector expression '{selectClause.Selector}' of type '{selectClause.Selector.NodeType}'");

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
        var sequence = ParseQueryModel<T>(queryModel)
            .Execute()
            .Select(GetSelectFunc<T>(queryModel.SelectClause));

        if (queryModel.ResultOperators.Any())
        {
            var op = queryModel.ResultOperators[0].ToString();

            return op switch
            {
                "Single()" => sequence.Single(),
                "SingleOrDefault()" => sequence.SingleOrDefault(),
                "First()" => sequence.First(),
                "FirstOrDefault()" => sequence.FirstOrDefault(),
                "Last()" => sequence.Last(),
                "LastOrDefault()" => sequence.LastOrDefault(),
                _ => throw new NotImplementedException($"Unknown operator '{op}'")
            };
        }

        throw new NotImplementedException();
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
            var op = queryModel.ResultOperators[0].ToString();

            // Modify the SQL query for Count() or Any()
            if (op == "Count()" || op == "Any()")
                select.What("COUNT(*)");
            else
                throw new NotImplementedException($"Query results operator '{op}' is not implemented");

            // Execute the scalar query
            var result = select.ExecuteScalar();

            // Handle the result for different types
            if (result is int intResult)
            {
                if (typeof(T) == typeof(int))
                    return (T)(object)intResult;

                if (typeof(T) == typeof(long))
                    return (T)(object)(long)intResult;

                if (typeof(T) == typeof(bool) && op == "Any()")
                    return (T)(object)(intResult > 0);
            }
            else if (result is long longResult)
            {
                if (typeof(T) == typeof(long))
                    return (T)(object)longResult;

                if (typeof(T) == typeof(int))
                    return (T)(object)(int)longResult;

                if (typeof(T) == typeof(bool) && op == "Any()")
                    return (T)(object)(longResult > 0);
            }

            throw new InvalidOperationException($"Unexpected result type '{result?.GetType()}' for operator '{op}'");
        }

        throw new NotImplementedException($"The query model lacks a results operator: '{queryModel}'");
    }
}