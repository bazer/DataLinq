using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLinq.Tools.Config
{
    public enum DatabaseType
    {
        MySQL,
        SQLite
    }

    public record ConfigFile
    {
        public List<DatabaseConfig> Databases { get; set; } = new();
    }

    public record DatabaseConfig
    {
        public string? Name { get; set; }
        public string? CsType { get; set; }
        public string? Namespace { get; set; }
        public string? SourceDirectory { get; set; }
        public string? DestinationDirectory { get; set; }
        public List<string>? Tables { get; set; }
        public List<string>? Views { get; set; }
        public bool? UseCache { get; set; }
        public bool? UseRecord { get; set; }
        public List<DatabaseConnectionConfig> Connections { get; set; } = new();
    }

    public record DatabaseConnectionConfig
    {
        public DatabaseType? ParsedType
        {
            get
            {
                if (Type?.ToLower() == "mysql" || Type?.ToLower() == "mariadb")
                    return DatabaseType.MySQL;
                else if (Type?.ToLower() == "sqlite")
                    return DatabaseType.SQLite;
                else
                    return null;
            }
        }
        public string? Type { get; set; }
        public string? DatabaseName { get; set; }
        public string? ConnectionString { get; set; }
        public ConnectionString? ParsedConnectionString => new ConnectionString(ConnectionString);
    }

    public record ConnectionString
    {
        public string Original { get; }
        DbConnectionStringBuilder builder;

        public bool HasPassword =>
            builder.ContainsKey("Password") ||
            builder.ContainsKey("password") ||
            builder.ContainsKey("pwd");

        public ConnectionString(string original)
        {
            Original = original;
            builder = new DbConnectionStringBuilder();
            builder.ConnectionString = Original;
        }
    }
}
