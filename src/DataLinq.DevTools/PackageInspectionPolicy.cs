using System;
using System.Collections.Generic;

namespace DataLinq.DevTools;

public static class PackageInspectionPolicy
{
    // The inspector and every downstream consumer of inspected package assets share these
    // ceilings so validation cannot approve an input that later requires an unbounded read.
    internal const long MaximumPackageArchiveBytes = 512L * 1024 * 1024;
    internal const int MaximumPrimaryManagedAssetBytes = 16 * 1024 * 1024;

    public const string CorePackageId = "DataLinq";
    public const string SQLitePackageId = "DataLinq.SQLite";
    public const string MySqlPackageId = "DataLinq.MySql";
    public const string MemoryPackageId = "DataLinq.Memory";
    public const string CliPackageId = "DataLinq.CLI";
    public const string ToolsPackageId = "DataLinq.Tools";
    public const string MemoryDescription = "Experimental read-only in-memory backend for generated DataLinq models.";
    public const string RepositoryUrl = "https://github.com/bazer/DataLinq";
    public const string LicenseFile = "LICENSE.md";
    public const string ReadmeFile = "README.md";

    public static IReadOnlyList<string> PublicPackageIds { get; } = Array.AsReadOnly(
    [
        CorePackageId,
        SQLitePackageId,
        MySqlPackageId,
        MemoryPackageId,
        CliPackageId,
        ToolsPackageId
    ]);

    public static IReadOnlyList<string> RuntimePackageIds { get; } = Array.AsReadOnly(
    [
        CorePackageId,
        SQLitePackageId,
        MySqlPackageId,
        MemoryPackageId
    ]);

    public static IReadOnlyList<string> PublicTargetFrameworks { get; } = Array.AsReadOnly(
    [
        "net8.0",
        "net9.0",
        "net10.0"
    ]);

    public static IReadOnlyList<string> MemoryTargetFrameworks { get; } = PublicTargetFrameworks;

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
