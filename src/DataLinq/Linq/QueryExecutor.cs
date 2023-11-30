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
using Remotion.Linq.Clauses.ResultOperators;

namespace DataLinq.Linq;

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
    internal QueryExecutor(Transaction transaction, TableMetadata table)
    {
        this.Transaction = transaction;
        this.Table = table;
    }

    /// <summary>Gets the transaction associated with the query executor.</summary>
    private Transaction Transaction { get; }

    /// <summary>Gets the metadata for the table that queries will be executed against.</summary>
    private TableMetadata Table { get; }

    /// <summary>
    /// Parses the provided QueryModel into a SQL query.
    /// </summary>
    /// <remarks>
    /// The method parses through body clauses and result operators to construct a SQL query.
    /// It uses the Where and OrderBy clauses to form the SQL conditions and ordering.
    /// 
    /// TODO: Enhance the parsing logic to support additional query expressions and body clauses
    /// for a richer query building experience.
    /// </remarks>
    private Select<object> ParseQueryModel(QueryModel queryModel)
    {
        var query = new SqlQuery(Table, Transaction);

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
        }

        return query.SelectQuery();
    }

    private Func<object, T> GetSelectFunc<T>(SelectClause selectClause)
    {
        if (selectClause != null && selectClause.Selector.NodeType == ExpressionType.MemberAccess)
        {
            var memberExpression = selectClause.Selector as MemberExpression;
            var prop = memberExpression.Member as PropertyInfo;

            return x => (T)prop.GetValue(x);
        }
        else if (selectClause?.Selector.NodeType == ExpressionType.New)
        {
            var memberExpression = selectClause.Selector as NewExpression;
            var memberType = (memberExpression.Arguments[0] as MemberExpression).Expression.Type;
            var param = Expression.Parameter(typeof(object));
            var convert = Expression.Convert(param, memberType);

            var arguments = memberExpression.Arguments.Select(x =>
            {
                var memberExp = x as MemberExpression;

                return Expression.MakeMemberAccess(convert, memberExp.Member);
            });

            var newExpression = Expression.New(memberExpression.Constructor, arguments, memberExpression.Members);
            var lambda = Expression.Lambda<Func<object, T>>(newExpression, param);

            return lambda.Compile();
        }

        return x => (T)x;
    }

    /// <summary>
    /// Executes the query represented by the QueryModel as a collection of objects of type T.
    /// </summary>
    /// <remarks>
    /// This method performs the actual execution of the SQL query and maps the result set
    /// to a collection of objects of the specified type using a projection function.
    /// 
    /// TODO: Consider implementing caching strategies for query results to minimize database hits
    /// for frequently executed queries.
    /// </remarks>
    public IEnumerable<T> ExecuteCollection<T>(QueryModel queryModel)
    {
        return ParseQueryModel(queryModel)
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
        var sequence = ParseQueryModel(queryModel)
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
        var keys = ParseQueryModel(queryModel)
            .ReadKeys();

        if (queryModel.ResultOperators.Any())
        {
            if (queryModel.ResultOperators[0].ToString() == "Count()")
                return (T)(object)keys.Count();
            else if (queryModel.ResultOperators[0].ToString() == "Any()")
                return (T)(object)keys.Any();
        }

        throw new NotImplementedException();
    }
}