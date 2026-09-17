using System;
using System.Data;
using System.Globalization;
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

    public static void SqlCommand(DataLinqLoggingConfiguration configuration, IDbCommand command)
    {
        if (configuration.SqlCommandLogger.IsEnabled(LogLevel.Debug))
            Sql(configuration.SqlCommandLogger, command.FormatCommand(configuration.SqlParameters));
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
    public static string FormatCommand(this IDbCommand command) =>
        FormatCommand(command, SqlParameterLoggingOptions.Default);

    public static string FormatCommand(this IDbCommand command, SqlParameterLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);
        var sb = new StringBuilder();
        sb.AppendLine(command.CommandText);

        if (command.Parameters.Count > 0)
        {
            sb.AppendLine("Parameters:");
            foreach (IDbDataParameter param in command.Parameters)
            {
                var value = param.Value;
                var formatted = value is null or DBNull ? "NULL"
                    : !options.IncludeSensitiveValues || options.RedactParameter?.Invoke(param) == true
                        ? "<redacted>"
                        : FormatValue(value, options.MaximumValueLength);
                sb.Append(param.ParameterName).Append(" = ").Append(formatted)
                    .Append(" (Type: ").Append(param.DbType);
                if (value is string text)
                    sb.Append(", Length: ").Append(text.Length);
                else if (value is byte[] bytes)
                    sb.Append(", Length: ").Append(bytes.Length);
                sb.AppendLine(")");
            }
        }

        if (command.Transaction != null)
        {
            sb.AppendLine("Transaction:");
            sb.AppendLine($"Isolation Level: {command.Transaction.IsolationLevel}");
        }

        return sb.ToString();
    }

    private static string FormatValue(object value, int maximumLength)
    {
        if (value is string text)
            return QuoteBounded(text, maximumLength);
        if (value is char character)
            return QuoteBounded(character.ToString(), maximumLength);
        if (value is byte[] bytes)
        {
            var count = Math.Min(bytes.Length, maximumLength / 2);
            return "0x" + Convert.ToHexString(bytes.AsSpan(0, count)) + (count < bytes.Length ? "…" : "");
        }

        // Never call an arbitrary object's ToString: it may expose data or allocate without a bound.
        var formatted = value switch
        {
            DateTime dateTime => dateTime.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("o", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("o", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("o", CultureInfo.InvariantCulture),
            TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString(),
            bool boolean => boolean ? "true" : "false",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
            _ => "<unsupported>"
        };
        return formatted.Length <= maximumLength ? formatted : formatted[..maximumLength] + "…";
    }

    private static string QuoteBounded(string value, int maximumLength)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        var remaining = maximumLength;
        var position = 0;
        for (; position < value.Length; position++)
        {
            var character = value[position];
            var escaped = character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ when char.IsControl(character) || char.IsSurrogate(character) || character is '\u2028' or '\u2029'
                    => "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                _ => null
            };
            var length = escaped?.Length ?? 1;
            if (length > remaining)
                break;
            if (escaped is not null)
                sb.Append(escaped);
            else
                sb.Append(character);
            remaining -= length;
        }
        if (position < value.Length)
            sb.Append('…');
        return sb.Append('"').ToString();
    }
}
