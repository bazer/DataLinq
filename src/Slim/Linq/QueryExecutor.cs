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

            return query.SelectQuery();
        }

        public IEnumerable<T> ExecuteCollection<T>(QueryModel queryModel)
        {
            var select = ParseQueryModel(queryModel);

            return select.Execute<T>();

            //if (Table.PrimaryKeyColumns.Count != 0)
            //{
            //    select.What(Table.PrimaryKeyColumns);

            //    var keys = ParseQueryModel(queryModel)
            //        .ReadKeys()
            //        .ToArray();

            //    foreach (var row in Table.Cache.GetRows(keys, Transaction))
            //        yield return (T)row;
            //}
            //else
            //{
            //    var rows = ParseQueryModel(queryModel)
            //        .ReadInstances()
            //        .Select(InstanceFactory.NewImmutableRow);

            //    foreach (var row in rows)
            //        yield return (T)row;
            //}
        }

        public T ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var sequence = ExecuteCollection<T>(queryModel);

            if (queryModel.ResultOperators.Any())
            {
                var op = queryModel.ResultOperators[0].ToString();

                return op switch
                {
                    "Single()" => sequence.Single(),
                    "SingleOrDefault()" => sequence.SingleOrDefault(),
                    "First()" => sequence.First(),
                    "FirstOrDefault()" => sequence.FirstOrDefault(),
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