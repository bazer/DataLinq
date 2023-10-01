using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Linq;
using System;
using static System.Formats.Asn1.AsnWriter;

namespace DataLinq.Config
{
    public record ConfigFile
    {
        public List<ConfigFileDatabase> Databases { get; set; } = new();
    }

    public record ConfigFileDatabase
    {
        public string? Name { get; set; }
        public string? CsType { get; set; }
        public string? Namespace { get; set; }
        public List<string>? SourceDirectories { get; set; }
        public string? DestinationDirectory { get; set; }
        public List<string>? Tables { get; set; }
        public List<string>? Views { get; set; }
        public bool? UseCache { get; set; }
        public bool? UseRecord { get; set; }
        public bool? UseFileScopedNamespaces { get; set; }
        public bool? CapitalizeNames { get; set; }
        public bool? RemoveInterfacePrefix { get; set; }
        public bool? SeparateTablesAndViews { get; set; }
        public List<ConfigFileDatabaseConnection> Connections { get; set; } = new();
        public string FileEncoding { get; set; }
        public Encoding ParseFileEncoding() => ConfigReader.ParseFileEncoding(FileEncoding);
    }

    public record ConfigFileDatabaseConnection
    {
        public DatabaseType? ParsedType
        {
            get
            {
                var type = ConfigReader.ParseDatabaseType(Type);

                return type.HasValue
                    ? type
                    : null;
            }
        }
        public string? Type { get; set; }
        public string? DatabaseName { get; set; }
        public string? ConnectionString { get; set; }
        public DataLinqConnectionString? ParsedConnectionString => new DataLinqConnectionString(ConnectionString);
    }
}
