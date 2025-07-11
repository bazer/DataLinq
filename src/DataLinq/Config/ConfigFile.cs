﻿using System.Collections.Generic;
using System.Text;

namespace DataLinq.Config;

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
    public List<string>? Include { get; set; }
    public bool? UseRecord { get; set; }
    public bool? UseFileScopedNamespaces { get; set; }
    public bool? UseNullableReferenceTypes { get; set; }
    public bool? CapitalizeNames { get; set; }
    public bool? RemoveInterfacePrefix { get; set; }
    public bool? SeparateTablesAndViews { get; set; }
    public List<ConfigFileDatabaseConnection> Connections { get; set; } = new();
    public string FileEncoding { get; set; }
    public Encoding ParseFileEncoding() => ConfigReader.ParseFileEncoding(FileEncoding);
}

public record ConfigFileDatabaseConnection
{
    public DatabaseType ParsedType => ConfigReader.ParseDatabaseType(Type);
    public string? Type { get; set; }
    public string? DatabaseName { get; set; }
    public string? DataSourceName { get; set; }
    public string? ConnectionString { get; set; }
    public DataLinqConnectionString? ParsedConnectionString => new DataLinqConnectionString(ConnectionString);
}
