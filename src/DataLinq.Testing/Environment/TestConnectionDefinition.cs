using DataLinq;
using DataLinq.Config;

namespace DataLinq.Testing;

public sealed record TestConnectionDefinition(
    string LogicalDatabaseName,
    string DataSourceName,
    DatabaseType DatabaseType,
    string ConnectionString)
{
    public ConfigFileDatabaseConnection ToConfigFileConnection()
    {
        var connection = new ConfigFileDatabaseConnection
        {
            Type = DatabaseType.ToString(),
            ConnectionString = ConnectionString
        };

        if (DatabaseType == DatabaseType.SQLite)
            connection.DatabaseName = DataSourceName;
        else
            connection.DataSourceName = DataSourceName;

        return connection;
    }
}
