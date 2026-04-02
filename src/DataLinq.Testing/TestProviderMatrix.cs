using System.Collections.ObjectModel;
using System.Collections.Generic;
using DataLinq;

namespace DataLinq.Testing;

public static class TestProviderMatrix
{
    public static TestProviderDescriptor SQLiteFile { get; } = new(
        Name: "sqlite-file",
        Kind: TestProviderKind.SQLiteFile,
        DatabaseType: DatabaseType.SQLite,
        RequiresExternalServer: false,
        UsesPodman: false);

    public static TestProviderDescriptor SQLiteInMemory { get; } = new(
        Name: "sqlite-memory",
        Kind: TestProviderKind.SQLiteInMemory,
        DatabaseType: DatabaseType.SQLite,
        RequiresExternalServer: false,
        UsesPodman: false);

    public static TestProviderDescriptor MySql { get; } = new(
        Name: "mysql",
        Kind: TestProviderKind.MySql,
        DatabaseType: DatabaseType.MySQL,
        RequiresExternalServer: true,
        UsesPodman: true);

    public static TestProviderDescriptor MariaDb { get; } = new(
        Name: "mariadb",
        Kind: TestProviderKind.MariaDb,
        DatabaseType: DatabaseType.MariaDB,
        RequiresExternalServer: true,
        UsesPodman: true);

    public static IReadOnlyList<TestProviderDescriptor> All { get; } = new ReadOnlyCollection<TestProviderDescriptor>(
    [
        SQLiteFile,
        SQLiteInMemory,
        MySql,
        MariaDb
    ]);

    public static IReadOnlyList<TestProviderDescriptor> Compliance { get; } = All;

    public static IReadOnlyList<TestProviderDescriptor> ServerBacked { get; } = new ReadOnlyCollection<TestProviderDescriptor>(
    [
        MySql,
        MariaDb
    ]);

    public static IReadOnlyList<TestProviderDescriptor> SQLiteOnly { get; } = new ReadOnlyCollection<TestProviderDescriptor>(
    [
        SQLiteFile,
        SQLiteInMemory
    ]);
}
