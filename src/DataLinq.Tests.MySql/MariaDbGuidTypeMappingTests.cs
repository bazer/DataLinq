using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Testing;
using MySqlConnector;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class MariaDbGuidTypeMappingTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.MariaDbProviders))]
    public async Task ParseDatabase_NativeUuidColumn_MapsToGuid(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_NativeUuidColumn_MapsToGuid),
            "CREATE TABLE uuid_test (id UUID PRIMARY KEY);");

        var database = schema.ParseDatabase(
            "TestDb",
            "TestDb",
            "TestNamespace",
            new MetadataFromDatabaseFactoryOptions { Include = ["uuid_test"] });

        var column = database.TableModels.Single().Table.Columns.Single();

        await Assert.That(column.DbName).IsEqualTo("id");
        await Assert.That(column.ValueProperty.CsType.Name).IsEqualTo("Guid");
        await Assert.That(column.GetDbTypeFor(DatabaseType.MariaDB)!.Name).IsEqualTo("uuid");
    }

    [Test]
    public async Task SqlGeneration_NativeUuidColumn_UsesMariaDbUuidType()
    {
        var database = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(UuidTestDatabase)).ValueOrException();
        var sqlResult = new SqlFromMariaDBFactory().GetCreateTables(database, foreignKeyRestrict: true).ValueOrException().Text;

        await Assert.That(sqlResult.Contains("CREATE TABLE IF NOT EXISTS `uuid_test`", StringComparison.Ordinal)).IsTrue();
        await Assert.That(sqlResult.Contains("`id` UUID NOT NULL", StringComparison.Ordinal)).IsTrue();
        await Assert.That(sqlResult.Contains("`id` BINARY(16) NOT NULL", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.MariaDbProviders))]
    public async Task Runtime_NativeUuidColumn_RoundTripsGuidValues(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(Runtime_NativeUuidColumn_RoundTripsGuidValues));
        using var database = CreateMariaDbDatabase<UuidTestDatabase>(schema.Connection.ConnectionString, schema.Connection.DataSourceName);

        database.Provider.DatabaseAccess.ExecuteNonQuery(database.Provider.GetCreateSql().Text);

        var testId = Guid.NewGuid();
        var inserted = database.Insert(new MutableUuidModel { Id = testId });
        database.Provider.State.ClearCache();
        var retrieved = database.Query().UuidModels.SingleOrDefault(x => x.Id == testId);

        await Assert.That(inserted).IsNotNull();
        await Assert.That(retrieved).IsNotNull();
        await Assert.That(inserted.Id).IsEqualTo(testId);
        await Assert.That(retrieved!.Id).IsEqualTo(testId);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.MariaDbProviders))]
    public async Task Runtime_ContainsQuery_WorksForNativeUuidColumnWithoutGuidFormat(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(Runtime_ContainsQuery_WorksForNativeUuidColumnWithoutGuidFormat));
        var connectionString = RemoveGuidFormat(schema.Connection.ConnectionString);
        using var database = CreateMariaDbDatabase<UuidTestDatabase>(connectionString, schema.Connection.DataSourceName);

        database.Provider.DatabaseAccess.ExecuteNonQuery(database.Provider.GetCreateSql().Text);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        database.Insert(new MutableUuidModel { Id = id1 });
        database.Insert(new MutableUuidModel { Id = id2 });
        database.Insert(new MutableUuidModel { Id = id3 });
        database.Provider.State.ClearCache();

        var idsToFind = new List<Guid> { id1, id2 };
        var results = database.Query().UuidModels.Where(x => idsToFind.Contains(x.Id)).ToList();

        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results.Any(x => x.Id == id1)).IsTrue();
        await Assert.That(results.Any(x => x.Id == id2)).IsTrue();
        await Assert.That(results.Any(x => x.Id == id3)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.MariaDbProviders))]
    public async Task Runtime_Binary16ContainsQuery_RequiresGuidFormat(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(Runtime_Binary16ContainsQuery_RequiresGuidFormat));

        var configuredBuilder = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString)
        {
            GuidFormat = MySqlGuidFormat.LittleEndianBinary16
        };

        using var configuredDatabase = CreateMariaDbDatabase<BinaryGuidTestDatabase>(configuredBuilder.ConnectionString, schema.Connection.DataSourceName);
        configuredDatabase.Provider.DatabaseAccess.ExecuteNonQuery(configuredDatabase.Provider.GetCreateSql().Text);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        configuredDatabase.Insert(new MutableBinaryGuidModel { Id = id1 });
        configuredDatabase.Insert(new MutableBinaryGuidModel { Id = id2 });
        configuredDatabase.Insert(new MutableBinaryGuidModel { Id = id3 });
        configuredDatabase.Provider.State.ClearCache();

        var idsToFind = new List<Guid> { id1, id2 };
        var configuredResults = configuredDatabase.Query().BinaryGuids.Where(x => idsToFind.Contains(x.Id)).ToList();

        await Assert.That(configuredResults.Count).IsEqualTo(2);
        await Assert.That(configuredResults.Any(x => x.Id == id1)).IsTrue();
        await Assert.That(configuredResults.Any(x => x.Id == id2)).IsTrue();

        var unconfiguredConnectionString = RemoveGuidFormat(schema.Connection.ConnectionString);
        using var unconfiguredDatabase = CreateMariaDbDatabase<BinaryGuidTestDatabase>(unconfiguredConnectionString, schema.Connection.DataSourceName);
        var unconfiguredResults = unconfiguredDatabase.Query().BinaryGuids.Where(x => idsToFind.Contains(x.Id)).ToList();

        await Assert.That(unconfiguredResults).IsEmpty();
    }

    private static MariaDBDatabase<TDatabase> CreateMariaDbDatabase<TDatabase>(string connectionString, string databaseName)
        where TDatabase : class, IDatabaseModel
        => new(connectionString, databaseName, loggerFactory: null);

    private static string RemoveGuidFormat(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        builder.Remove("GuidFormat");
        return builder.ConnectionString;
    }
}

public partial class UuidTestDatabase(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<UuidModel> UuidModels { get; } = new(dataSource);
}

[Table("uuid_test")]
public abstract partial class UuidModel(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<UuidModel, UuidTestDatabase>(rowData, dataSource), ITableModel<UuidTestDatabase>
{
    [PrimaryKey, Column("id")]
    [Type(DatabaseType.MariaDB, "uuid")]
    [Type(DatabaseType.MySQL, "binary", 16)]
    public abstract Guid Id { get; }
}

public partial class BinaryGuidTestDatabase(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<BinaryGuidModel> BinaryGuids { get; } = new(dataSource);
}

[Table("binary_guid_test")]
public abstract partial class BinaryGuidModel(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<BinaryGuidModel, BinaryGuidTestDatabase>(rowData, dataSource), ITableModel<BinaryGuidTestDatabase>
{
    [PrimaryKey, Column("id")]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    public abstract Guid Id { get; }
}
