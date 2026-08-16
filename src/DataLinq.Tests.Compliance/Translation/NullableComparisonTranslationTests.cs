using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class NullableComparisonTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableComparisons_PreserveCSharpTruthTablesAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<NullableComparisonDb>.Create(
            provider,
            nameof(NullableComparisons_PreserveCSharpTruthTablesAcrossProviders));
        var database = databaseScope.Database;
        var inserted = database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO nullable_comparison_rows " +
            "(id, left_value, right_value, typed_left, typed_right) VALUES " +
            "(1, NULL, NULL, NULL, NULL), " +
            "(2, NULL, 10, NULL, 10), " +
            "(3, 10, NULL, 10, NULL), " +
            "(4, 10, 10, 10, 10), " +
            "(5, 10, 20, 10, 20), " +
            "(6, 20, 10, 20, 10)");

        int? probe = 10;
        int? nullProbe = null;
        NullableComparisonId? typedProbe = new(10);
        NullableComparisonId? typedNullProbe = null;

        await Assert.That(inserted).IsEqualTo(6);

        await AssertIds(database, row => !(row.LeftValue == probe), [1, 2, 6]);
        await AssertIds(database, row => row.LeftValue != probe, [1, 2, 6]);
        await AssertIds(database, row => probe != row.LeftValue, [1, 2, 6]);
        await AssertIds(database, row => !(row.LeftValue != probe), [3, 4, 5]);
        await AssertIds(database, row => !(row.LeftValue > probe), [1, 2, 3, 4, 5]);
        await AssertIds(database, row => !(probe < row.LeftValue), [1, 2, 3, 4, 5]);
        await AssertIds(database, row => !(row.LeftValue >= probe), [1, 2]);
        await AssertIds(database, row => !(row.LeftValue < probe), [1, 2, 3, 4, 5, 6]);
        await AssertIds(database, row => !(row.LeftValue <= probe), [1, 2, 6]);

        await AssertIds(database, row => row.LeftValue == row.RightValue, [1, 4]);
        await AssertIds(database, row => row.LeftValue != row.RightValue, [2, 3, 5, 6]);
        await AssertIds(database, row => !(row.LeftValue == row.RightValue), [2, 3, 5, 6]);
        await AssertIds(database, row => !(row.LeftValue != row.RightValue), [1, 4]);
        await AssertIds(database, row => !(row.LeftValue > row.RightValue), [1, 2, 3, 4, 5]);
        await AssertIds(database, row => !(row.LeftValue == row.RightValue || row.Id == -1), [2, 3, 5, 6]);

        await AssertIds(database, row => row.LeftValue == nullProbe, [1, 2]);
        await AssertIds(database, row => row.LeftValue != nullProbe, [3, 4, 5, 6]);
        await AssertIds(database, row => row.LeftValue > nullProbe, []);
        await AssertIds(database, row => !(row.LeftValue > nullProbe), [1, 2, 3, 4, 5, 6]);
        await AssertIds(database, row => !(nullProbe > row.LeftValue), [1, 2, 3, 4, 5, 6]);

        await AssertIds(database, row => !(row.TypedLeft == typedProbe), [1, 2, 6]);
        await AssertIds(database, row => row.TypedLeft == row.TypedRight, [1, 4]);
        await AssertIds(database, row => row.TypedLeft != row.TypedRight, [2, 3, 5, 6]);
        await AssertIds(database, row => row.TypedLeft == typedNullProbe, [1, 2]);
        await AssertIds(database, row => row.TypedLeft != typedNullProbe, [3, 4, 5, 6]);
    }

    private static async Task AssertIds(
        Database<NullableComparisonDb> database,
        Expression<Func<NullableComparisonRow, bool>> predicate,
        int[] expected)
    {
        var actual = database.Query().Rows
            .Where(predicate)
            .Select(static row => row.Id)
            .OrderBy(static id => id)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }
}

public readonly record struct NullableComparisonId(int Value);

public sealed class NullableComparisonIdConverter : DataLinqScalarConverter<NullableComparisonId, int>
{
    public override int ToProvider(NullableComparisonId modelValue, in ScalarConversionContext context) =>
        modelValue.Value;

    public override NullableComparisonId FromProvider(int providerValue, in ScalarConversionContext context) =>
        new(providerValue);
}

[Database("nullablecomparisons")]
public sealed partial class NullableComparisonDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<NullableComparisonRow> Rows { get; } = new(dataSource);
}

[Table("nullable_comparison_rows")]
public abstract partial class NullableComparisonRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<NullableComparisonRow, NullableComparisonDb>(rowData, dataSource),
      ITableModel<NullableComparisonDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int Id { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("left_value")]
    public abstract int? LeftValue { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("right_value")]
    public abstract int? RightValue { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(NullableComparisonIdConverter))]
    [Column("typed_left")]
    public abstract NullableComparisonId? TypedLeft { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(NullableComparisonIdConverter))]
    [Column("typed_right")]
    public abstract NullableComparisonId? TypedRight { get; }
}
