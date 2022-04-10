using Remotion.Linq;
using Remotion.Linq.Clauses;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DataLinq.Linq
{
    internal class QueryExecutor : IQueryExecutor
    {
        internal QueryExecutor(Transaction transaction, TableMetadata table)
        {
            this.Transaction = transaction;
            this.Table = table;
        }

        private Transaction Transaction { get; }

        private TableMetadata Table { get; }

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

        public IEnumerable<T> ExecuteCollection<T>(QueryModel queryModel)
        {
            return ParseQueryModel(queryModel)
                .Execute()
                .Select(GetSelectFunc<T>(queryModel.SelectClause));
        }

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
}