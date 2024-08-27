using System;
using System.Data;
using System.Text;
using DataLinq.Metadata;
using Microsoft.Extensions.Logging;

namespace DataLinq.Logging;

public static partial class Log
{
    [LoggerMessage(EventIds.SqlCommand, LogLevel.Debug, "{sql}")]
    public static partial void Sql(ILogger logger, string sql);

    public static void SqlCommand(ILogger logger, IDbCommand command)
    {
        if(logger.IsEnabled(LogLevel.Debug))
            Sql(logger, command.FormatCommand());
    }

    [LoggerMessage(EventIds.IndexCachePreload, LogLevel.Debug, "Preloaded {rowsLoaded} keys to index cache: {index}")]
    public static partial void IndexCachePreload(ILogger logger, ColumnIndex index, int rowsLoaded);

    [LoggerMessage(EventIds.RowCachePreload, LogLevel.Debug, "Preloaded {rowsLoaded} rows to table cache: {table}")]
    public static partial void RowCachePreload(ILogger logger, TableDefinition table, int rowsLoaded);

    [LoggerMessage(EventIds.LoadRowsFromCache, LogLevel.Debug, "Fetched {rowsLoaded} rows from table cache: {table}")]
    public static partial void LoadRowsFromCache(ILogger logger, TableDefinition table, int rowsLoaded);

    [LoggerMessage(EventIds.LoadRowsFromDatabase, LogLevel.Debug, "Fetched {rowsLoaded} rows from database and added to table cache: {table}")]
    public static partial void LoadRowsFromDatabase(ILogger logger, TableDefinition table, int rowsLoaded);
}

public static class DbCommandExtensions
{
    public static string FormatCommand(this IDbCommand command)
    {
        var sb = new StringBuilder();
        sb.AppendLine(command.CommandText);

        if (command.Parameters.Count > 0)
        {
            sb.AppendLine("Parameters:");
            foreach (IDbDataParameter param in command.Parameters)
            {
                sb.AppendLine($"{param.ParameterName} = {ConvertParamValue(param)} (Type: {param.DbType})");
            }
        }

        if (command.Transaction != null)
        {
            sb.AppendLine("Transaction:");
            sb.AppendLine($"Isolation Level: {command.Transaction.IsolationLevel}");
        }

        return sb.ToString();

        string ConvertParamValue(IDbDataParameter param)
        {
            return param.Value switch
            {
                null => "NULL",
                DBNull _ => "NULL",
                byte[] byteArray => TryParseGuid(byteArray, out var guid) ? guid.ToString() : BitConverter.ToString(byteArray).Replace("-", " "),
                DateTime dateTime => dateTime.ToString("o"), // ISO 8601 format
                string str => Guid.TryParse(str, out var guid) ? guid.ToString() : $"\"{str}\"",
                _ => param.Value?.ToString() ?? "NULL",
            };
        }

        bool TryParseGuid(byte[] byteArray, out Guid guid)
        {
            // Assuming the byte array represents a GUID in a common format
            if (byteArray.Length == 16)
            {
                guid = new Guid(byteArray);
                return true;
            }

            guid = default;
            return false;
        }
    }
}
