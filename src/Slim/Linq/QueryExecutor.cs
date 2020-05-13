using Remotion.Linq;
using Remotion.Linq.Clauses;
using Slim.Instances;
using Slim.Metadata;
using Slim.Mutation;
using Slim.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Slim.Linq
{
    internal class QueryExecutor : IQueryExecutor
    {
        internal QueryExecutor(Transaction transaction, Table table)
        {
            this.Transaction = transaction;
            this.Table = table;
        }

        private Transaction Transaction { get; }

        private Table Table { get; }

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

        public IEnumerable<T> ExecuteCollection<T>(QueryModel queryModel)
        {
            return ParseQueryModel(queryModel)
                .Execute<T>();
        }

        public T ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var sequence = ParseQueryModel(queryModel)
                .Execute<T>();

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