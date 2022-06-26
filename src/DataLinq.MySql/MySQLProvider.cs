using MySqlConnector;
using DataLinq.Extensions;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using System;
using System.Data;

namespace DataLinq.MySql
{
    public class MySQLProvider<T> : DatabaseProvider<T>
        where T : class, IDatabaseModel
    {
        public MySQLProvider(string connectionString) : base(connectionString)
        {
        }

        public MySQLProvider(string connectionString, string databaseName) : base(connectionString, databaseName)
        {
        }

        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type)
        {
            if (type == TransactionType.NoTransaction)
                return new MySqlDbAccess(ConnectionString, type);
            else
                return new MySqlDatabaseTransaction(ConnectionString, type);
        }


        public override string GetExists(string databaseName = null)
        {
            if (databaseName == null && DatabaseName == null)
                throw new ArgumentNullException("DatabaseName not defined");

           return $"SHOW DATABASES LIKE '{databaseName ?? DatabaseName}'";
        }

        public override string GetLastIdQuery()
        {
            return "SELECT last_insert_id()";
        }

        public override Sql GetParameterValue(Sql sql, string key)
        {
            return sql.AddFormat("?{0}", key);
        }

        public override Sql GetParameterComparison(Sql sql, string field, Relation relation, string key)
        {
            return sql.AddFormat("{0} {1} ?{2}", field, relation.ToSql(), key);
        }

        public override Sql GetParameter(Sql sql, string key, object value)
        {
            return sql.AddParameters(new MySqlParameter("?" + key, value ?? DBNull.Value));
        }

        public override IDbCommand ToDbCommand(IQuery query)
        {
            var sql = query.ToSql("");

            var sqlText = sql.Text;
            if (DatabaseName != null)
                sqlText = $"USE {DatabaseName};\r\n" + sqlText;

            var command = new MySqlCommand(sqlText);
            command.Parameters.AddRange(sql.Parameters.ToArray());

            return command;
        }

        public override Sql GetCreateSql() => new SqlFromMetadataFactory().GenerateSql(Metadata, true);
    }
}