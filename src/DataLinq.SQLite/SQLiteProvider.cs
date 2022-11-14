using DataLinq.Extensions;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using System;
using System.Data;
using Microsoft.Data.Sqlite;
using DataLinq.Metadata;
using System.IO;

namespace DataLinq.SQLite
{
    public class SQLiteProvider : IDatabaseProviderRegister
    {
        public static bool HasBeenRegistered { get; private set; }

        public static void RegisterProvider()
        {
            if (HasBeenRegistered)
                return;

            PluginHook.SqlGenerators[DatabaseType.SQLite] = new SqlFromMetadataFactory();
            PluginHook.DatabaseCreators[DatabaseType.SQLite] = new SqlFromMetadataFactory();

            HasBeenRegistered = true;
        }
    }

    public class SQLiteProvider<T> : DatabaseProvider<T>
        where T : class, IDatabaseModel
    {
        static SQLiteProvider()
        {
            SQLiteProvider.RegisterProvider();
        }

        public SQLiteProvider(string connectionString) : base(connectionString)
        {
        }

        public SQLiteProvider(string connectionString, string databaseName) : base(connectionString, databaseName)
        {
        }

        public override void CreateDatabase(string databaseName = null)
        {
            if (databaseName == null && DatabaseName == null)
                throw new ArgumentNullException("DatabaseName not defined");

            using var transaction = GetNewDatabaseTransaction(TransactionType.ReadAndWrite);

            var query = $"CREATE DATABASE IF NOT EXISTS {databaseName ?? DatabaseName};\n" +
                $"USE {databaseName ?? DatabaseName};\n" +
                GetCreateSql();

            transaction.ExecuteNonQuery(query);
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

        public override Sql GetCreateSql() => new SqlFromMetadataFactory().GetCreateTables(Metadata, true);

        public override IDbCommand ToDbCommand(IQuery query)
        {
            var sql = query.ToSql("");
            var command = new SqliteCommand(sql.Text);
            command.Parameters.AddRange(sql.Parameters.ToArray());

            return command;
        }

        public override string GetExists(string databaseName = null)
        {
            throw new NotImplementedException();
        }
    }
}