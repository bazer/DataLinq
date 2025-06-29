using System;
using System.Data;
using System.Linq;
using System.Text;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using MySqlConnector;

namespace DataLinq.MySql;

public class SqlProviderConstants : IDatabaseProviderConstants
{
    public string ParameterSign { get; } = "?";
    public string LastInsertCommand { get; } = "last_insert_id()";
    public string EscapeCharacter { get; } = "`";
    public bool SupportsMultipleDatabases { get; } = true;
}

public abstract class SqlProvider<T> : DatabaseProvider<T>, IDisposable
    where T : class, IDatabaseModel
{
    private readonly SqlDataLinqDataWriter dataWriter;
    private readonly MySqlDataSource dataSource;
    private readonly SqlDbAccess dbAccess;
    private readonly SqlFromMetadataFactory sqlFromMetadataFactory;

    public override IDatabaseProviderConstants Constants { get; } = new SqlProviderConstants();
    public override DatabaseAccess DatabaseAccess => dbAccess;

    static SqlProvider()
    {
        MySQLProvider.RegisterProvider();
    }

    public SqlProvider(string connectionString, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration) : this(connectionString, databaseType, loggingConfiguration, null)
    {
    }

    public SqlProvider(string connectionString, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration, string? databaseName) : base(connectionString, databaseType, loggingConfiguration, databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);

            if (!string.IsNullOrWhiteSpace(connectionStringBuilder.Database))
                DatabaseName = connectionStringBuilder.Database;
        }

        dataSource = new MySqlDataSourceBuilder(ConnectionString)
             .UseLoggerFactory(LoggingConfiguration.LoggerFactory)
             .Build();

        dbAccess = new SqlDbAccess(dataSource, LoggingConfiguration);
        sqlFromMetadataFactory = SqlFromMetadataFactory.GetFactoryFromDatabaseType(DatabaseType);
        dataWriter = new SqlDataLinqDataWriter(sqlFromMetadataFactory);
    }

    public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type)
    {

        return new SqlDatabaseTransaction(dataSource, type, DatabaseName, LoggingConfiguration);
    }

    public override DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type)
    {
        return new SqlDatabaseTransaction(dbTransaction, type, DatabaseName, LoggingConfiguration);
    }

    public override bool DatabaseExists(string? databaseName = null)
    {
        if (databaseName == null && DatabaseName == null)
            throw new ArgumentNullException("DatabaseName not defined");

        return DatabaseAccess
            .ReadReader($"SHOW DATABASES LIKE '{databaseName ?? DatabaseName}'")
            .Any();
    }

    public override bool FileOrServerExists()
    {
        try
        {
            return DatabaseAccess.ExecuteScalar<int>("SELECT 1") == 1;
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

    public override Sql GetParameterComparison(Sql sql, string field, Query.Relation relation, string[] key)
    {
        return sql.AddFormat("{0} {1} {2}",
            field,
            relation.ToSql(),
            GetParameterName(relation, key));
    }

    private string GetParameterName(Query.Relation relation, string[] key)
    {
        var builder = new StringBuilder();
        if (key.Length > 1 || relation == Query.Relation.In || relation == Query.Relation.NotIn)
        {
            builder.Append('(');
        }

        for (int i = 0; i < key.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }
            builder.Append('?');
            builder.Append(key[i]);
        }

        if (key.Length > 1 || relation == Query.Relation.In || relation == Query.Relation.NotIn)
        {
            builder.Append(')');
        }

        return builder.ToString();
    }

    public override Sql GetParameter(Sql sql, string key, object? value)
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

    public override Sql GetTableName(Sql sql, string tableName, string? alias = null)
    {
        sql.AddText($"{Constants.EscapeCharacter}{DatabaseName}{Constants.EscapeCharacter}.");

        sql.AddText(string.IsNullOrEmpty(alias)
            ? $"{Constants.EscapeCharacter}{tableName}{Constants.EscapeCharacter}"
            : $"{Constants.EscapeCharacter}{tableName}{Constants.EscapeCharacter} {alias}");

        return sql;
    }

    public override IDbCommand ToDbCommand(IQuery query)
    {
        var sql = query.ToSql();

        var command = new MySqlCommand(sql.Text);
        command.Parameters.AddRange(sql.Parameters.ToArray());

        return command;
    }

    public override Sql GetCreateSql() => sqlFromMetadataFactory.GetCreateTables(Metadata, true);

    public override IDataLinqDataWriter GetWriter()
    {
        return dataWriter;
    }
}