using System;
using System.Data;
using System.Linq;
using System.Text;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using DataLinq.Logging;
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

    public SqlProvider(string connectionString, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration) : this(connectionString, databaseType, loggingConfiguration, null)
    {
    }

    public SqlProvider(string connectionString, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration, string? databaseName) : base(connectionString, databaseType, loggingConfiguration, databaseName)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(databaseName) && !string.IsNullOrWhiteSpace(builder.Database))
            DatabaseName = builder.Database;

        //// If the user has not specified a GuidFormat, default to the one that works best with .NET Guids.
        //if (!builder.ContainsKey("GuidFormat"))
        //{
        //    builder.GuidFormat = MySqlGuidFormat.LittleEndianBinary16;
        //}

        //if (string.IsNullOrWhiteSpace(databaseName))
        //{
        //    var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);

        //    if (!string.IsNullOrWhiteSpace(connectionStringBuilder.Database))
        //        DatabaseName = connectionStringBuilder.Database;
        //}

        dataSource = new MySqlDataSourceBuilder(builder.ConnectionString)
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

    public override bool TableExists(string tableName, string? databaseName = null)
    {
        if (string.IsNullOrEmpty(tableName))
            throw new ArgumentNullException(nameof(tableName));

        if (databaseName == null && DatabaseName == null)
            throw new ArgumentNullException(nameof(databaseName));

        return DatabaseAccess
            .ReadReader($"SHOW TABLES IN `{databaseName ?? DatabaseName}` LIKE '{tableName}'")
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

    public override string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments)
    {
        if (SqlFunctionType.StringSubstring == functionType && (arguments == null || arguments.Length != 2))
            throw new ArgumentException("StringSubstring requires two arguments: start index and length.");

        // If columnName already looks like an expression (contains '(' ) then don't quote it again.
        var quotedColumnName = columnName.Contains('(')
            ? columnName
            : $"{Constants.EscapeCharacter}{columnName}{Constants.EscapeCharacter}";

        return functionType switch
        {
            // Date Parts
            SqlFunctionType.DatePartYear => $"YEAR({quotedColumnName})",
            SqlFunctionType.DatePartMonth => $"MONTH({quotedColumnName})",
            SqlFunctionType.DatePartDay => $"DAY({quotedColumnName})",
            SqlFunctionType.DatePartDayOfYear => $"DAYOFYEAR({quotedColumnName})",
            // MySQL's DAYOFWEEK() returns 1=Sun, 2=Mon... C#'s DayOfWeek is 0=Sun, 1=Mon...
            // So we subtract 1 to align them.
            SqlFunctionType.DatePartDayOfWeek => $"(DAYOFWEEK({quotedColumnName}) - 1)",

            // Time Parts
            SqlFunctionType.TimePartHour => $"HOUR({quotedColumnName})",
            SqlFunctionType.TimePartMinute => $"MINUTE({quotedColumnName})",
            SqlFunctionType.TimePartSecond => $"SECOND({quotedColumnName})",
            // MySQL's MICROSECOND() returns microseconds, so we divide by 1000.
            SqlFunctionType.TimePartMillisecond => $"FLOOR(MICROSECOND({quotedColumnName}) / 1000)",

            // String Parts
            SqlFunctionType.StringLength => $"CHAR_LENGTH({quotedColumnName})",
            SqlFunctionType.StringToUpper => $"UPPER({quotedColumnName})",
            SqlFunctionType.StringToLower => $"LOWER({quotedColumnName})",
            SqlFunctionType.StringTrim => $"TRIM({quotedColumnName})",
            SqlFunctionType.StringSubstring => $"SUBSTRING({quotedColumnName}, {arguments![0]}, {arguments[1]})",

            _ => throw new NotImplementedException($"SQL function '{functionType}' not implemented for MySQL/MariaDB."),
        };
    }

    public override string GetOperatorSql(Operator @operator) => @operator switch
    {
        Operator.Equal => "=",
        Operator.EqualNull => "IS",
        Operator.NotEqual => "<>",
        Operator.NotEqualNull => "IS NOT",
        Operator.Like => "LIKE",
        Operator.NotLike => "NOT LIKE",
        Operator.GreaterThan => ">",
        Operator.GreaterThanOrEqual => ">=",
        Operator.LessThan => "<",
        Operator.LessThanOrEqual => "<=",
        Operator.In => "IN",
        Operator.NotIn => "NOT IN",
        _ => throw new NotSupportedException($"Operator '{@operator}' is not supported in MySQL/MariaDB.")
    };

    public override Sql GetParameterValue(Sql sql, string key)
    {
        return sql.AddFormat("?{0}", key);
    }

    public override Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] key)
    {
        return sql.AddFormat("{0} {1} {2}",
            field,
            GetOperatorSql(@operator),
            GetParameterName(@operator, key));
    }

    public override string GetParameterName(Operator relation, string[] key)
    {
        var builder = new StringBuilder();
        if (key.Length > 1 || relation == Operator.In || relation == Operator.NotIn)
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

        if (key.Length > 1 || relation == Operator.In || relation == Operator.NotIn)
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

    public override IDbConnection GetDbConnection()
    {
        return new MySqlConnection(dataSource.ConnectionString);
    }
}