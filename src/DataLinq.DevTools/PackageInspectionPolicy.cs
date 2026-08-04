using System;
using System.Collections.Generic;

namespace DataLinq.DevTools;

public static class PackageInspectionPolicy
{
    public const string CorePackageId = "DataLinq";
    public const string MemoryPackageId = "DataLinq.Memory";
    public const string MemoryDescription = "Experimental read-only in-memory backend for generated DataLinq models.";
    public const string RepositoryUrl = "https://github.com/bazer/DataLinq";
    public const string LicenseFile = "LICENSE.md";
    public const string ReadmeFile = "README.md";

    public static IReadOnlyList<string> PublicPackageIds { get; } = Array.AsReadOnly(
    [
        CorePackageId,
        "DataLinq.SQLite",
        "DataLinq.MySql",
        MemoryPackageId,
        "DataLinq.CLI",
        "DataLinq.Tools"
    ]);

    public static IReadOnlyList<string> RuntimePackageIds { get; } = Array.AsReadOnly(
    [
        CorePackageId,
        "DataLinq.SQLite",
        "DataLinq.MySql",
        MemoryPackageId
    ]);

    public static IReadOnlyList<string> MemoryTargetFrameworks { get; } = Array.AsReadOnly(
    [
        "net8.0",
        "net9.0",
        "net10.0"
    ]);

    internal static IReadOnlyList<string> MemoryBannedPayloadTokens { get; } = Array.AsReadOnly(
    [
        "DataLinq.SQLite",
        "DataLinq.MySql",
        "Microsoft.Data.Sqlite",
        "MySqlConnector",
        "SQLitePCLRaw",
        "e_sqlite3",
        "Microsoft.CodeAnalysis",
        "Remotion.Linq",
        "DataLinq.Generators"
    ]);
}
