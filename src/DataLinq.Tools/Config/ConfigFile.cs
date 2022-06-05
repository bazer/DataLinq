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
        MariaDB,
        SQLite
    }

    public record ConfigFile
    {
        public List<DatabaseConfig> Databases { get; set; } = new();
    }

    public record DatabaseConfig
    {
        public DatabaseType? Type { get; set; }
        public string? Name { get; set; }
        public string? Namespace { get; set; }
        public ConnectionString? ConnectionString { get; set; }
        public string? SourceDirectory { get; set; }
        public string? DestinationDirectory { get; set; }
        public List<string>? Tables { get; set; }
        public List<string>? Views { get; set; }
        public bool? UseCache { get; set; }
        public bool? UseRecord { get; set; }
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
