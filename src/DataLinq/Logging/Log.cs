using System.Data;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DataLinq.Logging;

public static partial class Log
{
    [LoggerMessage(EventIds.SqlCommand, LogLevel.Debug, "{sql}")]
    public static partial void Sql(ILogger logger, string sql);

    public static void SqlCommand(ILogger logger, IDbCommand command)
    {
        var formattedCommand = command.FormatCommand();
        Sql(logger, formattedCommand);
    }
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
                sb.AppendLine($"{param.ParameterName} = {param.Value} (Type: {param.DbType})");
            }
        }

        if (command.Transaction != null)
        {
            sb.AppendLine("Transaction:");
            sb.AppendLine($"Isolation Level: {command.Transaction.IsolationLevel}");
        }

        return sb.ToString();
    }
}
