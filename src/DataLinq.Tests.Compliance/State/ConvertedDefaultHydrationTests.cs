using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class ConvertedDefaultHydrationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Insert_ConvertedAutoIncrementAndServerDefaultHydrateAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<ConvertedDefaultHydrationDb>.Create(
            provider,
            nameof(Insert_ConvertedAutoIncrementAndServerDefaultHydrateAcrossProviders));

        var inserted = databaseScope.Database.Insert(new MutableConvertedDefaultHydrationRow());
        var insertedId = inserted.Id;

        await Assert.That(insertedId).IsNotNull();
        await Assert.That(inserted.ServerValue).IsEqualTo(new ConvertedServerValue(42));

        databaseScope.Database.Provider.State.ClearCache();
        var reloaded = databaseScope.Database.Query().Rows.Single(row => row.Id == insertedId);

        await Assert.That(reloaded.Id).IsEqualTo(insertedId);
        await Assert.That(reloaded.ServerValue).IsEqualTo(new ConvertedServerValue(42));
    }
}

public readonly record struct ConvertedAutoIncrementId(int Value);

public sealed class ConvertedAutoIncrementIdConverter
    : DataLinqScalarConverter<ConvertedAutoIncrementId, int>
{
    public override int ToProvider(
        ConvertedAutoIncrementId modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override ConvertedAutoIncrementId FromProvider(
        int providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}

public readonly record struct ConvertedServerValue(int Value);

public sealed class ConvertedServerValueConverter
    : DataLinqScalarConverter<ConvertedServerValue, int>
{
    public override int ToProvider(
        ConvertedServerValue modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override ConvertedServerValue FromProvider(
        int providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}

[Database("converteddefaulthydration")]
public sealed partial class ConvertedDefaultHydrationDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<ConvertedDefaultHydrationRow> Rows { get; } = new(dataSource);
}

[Table("converted_default_hydration_rows")]
public abstract partial class ConvertedDefaultHydrationRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<ConvertedDefaultHydrationRow, ConvertedDefaultHydrationDb>(rowData, dataSource),
      ITableModel<ConvertedDefaultHydrationDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(ConvertedAutoIncrementIdConverter))]
    [Column("id")]
    public abstract ConvertedAutoIncrementId? Id { get; }

    [Nullable]
    [DefaultSql(DatabaseType.Default, "42")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(ConvertedServerValueConverter))]
    [Column("server_value")]
    public abstract ConvertedServerValue? ServerValue { get; }
}
