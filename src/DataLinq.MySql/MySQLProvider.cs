using DataLinq.Extensions;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using MySqlConnector;
using System;
using System.Data;
using System.Runtime.CompilerServices;

namespace DataLinq.MySql
{
    public class MySQLProvider : IDatabaseProviderRegister
    {
        public static bool HasBeenRegistered { get; private set; }

        [ModuleInitializer]
        public static void RegisterProvider()
        {
            if (HasBeenRegistered)
                return;

            PluginHook.DatabaseProviders[DatabaseType.MySQL] = new MySqlDatabaseCreator();
            PluginHook.SqlFromMetadataFactories[DatabaseType.MySQL] = new SqlFromMetadataFactory();
            PluginHook.MetadataFromSqlFactories[DatabaseType.MySQL] = new MetadataFromMySqlFactoryCreator();

            HasBeenRegistered = true;
        }
    }

    public class MySQLProviderConstants : IDatabaseProviderConstants
    {
        public string ParameterSign { get; } = "?";
        public string LastInsertCommand { get; } = "last_insert_id()";
    }

    public class MySQLProvider<T> : DatabaseProvider<T>
        where T : class, IDatabaseModel
    {
        private MySqlConnectionStringBuilder connectionStringBuilder;
        private MySqlDataLinqDataWriter dataWriter = new MySqlDataLinqDataWriter();

        public override IDatabaseProviderConstants Constants { get; } = new MySQLProviderConstants();

        static MySQLProvider()
        {
            MySQLProvider.RegisterProvider();
        }

        public MySQLProvider(string connectionString) : base(connectionString, DatabaseType.MySQL)
        {
            connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);
            
            if (!string.IsNullOrWhiteSpace(connectionStringBuilder.Database))
                DatabaseName = connectionStringBuilder.Database;
        }

        public MySQLProvider(string connectionString, string databaseName) : base(connectionString, DatabaseType.MySQL, databaseName)
        {
            connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);
        }

        public override void CreateDatabase(string? databaseName = null)
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
            DatabaseTransaction transaction = type == TransactionType.ReadOnly
                ? new MySqlDbAccess(ConnectionString, type)
                : new MySqlDatabaseTransaction(ConnectionString, type);

            if (DatabaseName != null)
                transaction.ExecuteNonQuery($"USE {DatabaseName};");
            
            return transaction;
        }

        public override DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type)
        {
            return new MySqlDatabaseTransaction(dbTransaction, type);
        }

        public override string GetExists(string? databaseName = null)
        {
            if (databaseName == null && DatabaseName == null)
                throw new ArgumentNullException("DatabaseName not defined");

            return $"SHOW DATABASES LIKE '{databaseName ?? DatabaseName}'";
        }

        public override bool FileOrServerExists()
        {
            try
            {
                using var transaction = GetNewDatabaseTransaction(TransactionType.ReadOnly);

                return transaction.ExecuteScalar<int>("SELECT 1") == 1;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override string GetLastIdQuery()
        {
            return "SELECT last_insert_id()";
        }

        public override Sql GetParameterValue(Sql sql, string key)
        {
            return sql.AddFormat("?{0}", key);
        }

        public override Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string key)
        {
            return sql.AddFormat("{0} {1} ?{2}", field, relation.ToSql(), key);
        }

        public override Sql GetParameter(Sql sql, string key, object value)
        {
            return sql.AddParameters(new MySqlParameter("?" + key, value ?? DBNull.Value));
        }

        public override Sql GetLimitOffset(Sql sql, int? limit, int? offset)
        {
            if (!limit.HasValue && !offset.HasValue)
                return sql;

            if (limit.HasValue && !offset.HasValue)
                sql.AddText($"\nLIMIT {limit}");
            else if (!limit.HasValue && offset.HasValue)
                sql.AddText($"\nLIMIT 18446744073709551615 OFFSET {offset}");
            else
                sql.AddText($"\nLIMIT {limit} OFFSET {offset}");

            return sql;
        }

        public override IDbCommand ToDbCommand(IQuery query)
        {
            var sql = query.ToSql("");

            var sqlText = sql.Text;
            if (DatabaseName != null)
                sqlText = $"USE {DatabaseName};\n" + sqlText;

            var command = new MySqlCommand(sqlText);
            command.Parameters.AddRange(sql.Parameters.ToArray());

            return command;
        }

        public override Sql GetCreateSql() => new SqlFromMetadataFactory().GetCreateTables(Metadata, true);

        public override IDataLinqDataWriter GetWriter()
        {
            return dataWriter;
        }
    }
}