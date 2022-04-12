using DataLinq.Extensions;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using System;
using System.Data;
using Microsoft.Data.Sqlite;
using DataLinq.Metadata;

namespace DataLinq.SQLite
{
    public class SQLiteProvider<T> : DatabaseProvider<T>
        where T : class, IDatabaseModel
    {
        public SQLiteProvider(string connectionString) : base(connectionString)
        {
        }

        public SQLiteProvider(string connectionString, string databaseName) : base(connectionString, databaseName)
        {
        }

        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type)
        {
            if (type == TransactionType.NoTransaction)
                return new SQLiteDbAccess(ConnectionString, type);
            else
                return new SQLiteDatabaseTransaction(ConnectionString, type);
        }

        public override string GetLastIdQuery() => "SELECT last_insert_rowid()";

        public override Sql GetParameterValue(Sql sql, string key)
        {
            return sql.AddFormat("@{0}", key);
        }

        public override Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string key)
        {
            return sql.AddFormat("{0} {1} @{2}", field, relation.ToSql(), key);
        }

        public override Sql GetParameter(Sql sql, string key, object value)
        {
            return sql.AddParameters(new SqliteParameter("@" + key, value ?? DBNull.Value));
        }

        public override Sql GetCreateSql() => SqlFromMetadataFactory.GenerateSql(Metadata, true);

        public override IDbCommand ToDbCommand(IQuery query)
        {
            var sql = query.ToSql("");
            var command = new SqliteCommand(sql.Text);
            command.Parameters.AddRange(sql.Parameters.ToArray());

            return command;
        }
    }
}