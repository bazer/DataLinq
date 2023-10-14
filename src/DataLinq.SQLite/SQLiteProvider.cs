using DataLinq.Extensions;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;

namespace DataLinq.SQLite
{
    public class SQLiteProvider : IDatabaseProviderRegister
    {
        public static bool HasBeenRegistered { get; private set; }

        [ModuleInitializer]
        public static void RegisterProvider()
        {
            if (HasBeenRegistered)
                return;

            PluginHook.DatabaseProviders[DatabaseType.SQLite] = new SQLiteDatabaseCreator();
            PluginHook.SqlFromMetadataFactories[DatabaseType.SQLite] = new SqlFromMetadataFactory();
            PluginHook.MetadataFromSqlFactories[DatabaseType.SQLite] = new MetadataFromSQLiteFactoryCreator();

            HasBeenRegistered = true;
        }
    }

    public class SQLiteProviderConstants : IDatabaseProviderConstants
    {
        public string ParameterSign { get; } = "@";

        public string LastInsertCommand { get; } = "last_insert_rowid()";
    }

    public class SQLiteProvider<T> : DatabaseProvider<T>
        where T : class, IDatabaseModel
    {
        private SqliteConnectionStringBuilder connectionStringBuilder;
        private SQLiteDataLinqDataWriter dataWriter = new SQLiteDataLinqDataWriter();
        public override IDatabaseProviderConstants Constants { get; } = new SQLiteProviderConstants();
        static SQLiteProvider()
        {
            SQLiteProvider.RegisterProvider();
        }

        public SQLiteProvider(string connectionString) : base(connectionString, DatabaseType.SQLite)
        {
            connectionStringBuilder = new SqliteConnectionStringBuilder(connectionString);
        }

        public SQLiteProvider(string connectionString, string databaseName) : base(connectionString, DatabaseType.SQLite, databaseName)
        {
            connectionStringBuilder = new SqliteConnectionStringBuilder(connectionString);
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
            //if (type == TransactionType.ReadOnly)
            //    return new SQLiteDbAccess(ConnectionString, type);
            //else
                return new SQLiteDatabaseTransaction(ConnectionString, type);
        }

        public override DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type)
        {
            return new SQLiteDatabaseTransaction(dbTransaction, type);
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
            if (databaseName == null && DatabaseName == null)
                throw new ArgumentNullException("DatabaseName not defined");

            return $"SELECT name FROM pragma_database_list WHERE name = '{databaseName ?? DatabaseName}'";
        }

        public override bool FileOrServerExists()
        {
            var source = connectionStringBuilder.DataSource;

            if (source == "memory")
                return true;

            return File.Exists(source);
        }

        public override IDataLinqDataWriter GetWriter()
        {
            return dataWriter;
        }
    }
}