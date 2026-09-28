using DataLinq;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.PackageConsumer;
using DataLinq.SQLite;

namespace Legacy092Consumer;

// This assembly is compiled once against the locked 0.9.2 packages. The candidate
// consumer loads those same bytes, without rebuilding or copying baseline dependencies.
public static class LegacyExports
{
    public static IDatabaseProvider Provider() => new LegacyProvider();
    public static IDatabaseAccess Access() => new LegacyDatabaseAccess();
    public static DatabaseTransaction Transaction() => new LegacyTransaction();
    public static DataSourceAccess Source() => new LegacyDataSource(Provider());
    public static IMetadataFromSqlFactory Metadata() => new LegacyMetadataFactory();
    public static ISqlFromMetadataFactory Provisioning() => new LegacyProvisioningFactory();
    public static IImmutable<IModel> StaticInterfaceImplementer() => new LegacyImmutable();
    public static DatabaseProvider DerivedProvider() => ExternalConstructorBindings.Constructors.CreateSql();
    public static string ConstructorBinding() => ExternalConstructorBindings.Constructors.UntypedNullBinding();
    public static IImmutableForeignKey<IImmutableInstance> CovariantReference() => new LegacyReference();

    public static void RemovedKeyedEnumeration()
    {
        var relation = new ImmutableRelationMock<PackageConsumerRow>([]);
        _ = relation.AsEnumerable();
    }

    public static int SynchronousGeneratedConsumer()
    {
        SQLiteProvider.RegisterProvider();
        var name = "legacy_" + Guid.NewGuid().ToString("N");
        using var database = new SQLiteDatabase<PackageConsumerDatabase>(
            connectionString: $"Data Source={name};Mode=Memory;Cache=Shared", databaseName: name,
            loggerFactory: null);
        var created = PluginHook.CreateDatabaseFromMetadata(DatabaseType.SQLite,
            database.Provider.Metadata, name, database.Provider.ConnectionString, true);
        if (created.HasFailed) throw new InvalidOperationException(created.Failure.ToString());
        database.Insert(new MutablePackageConsumerRow
        {
            Id = 17, GroupId = 7, Name = "old generated model", ExternalGuid = Guid.NewGuid()
        });
        using var transaction = database.Transaction();
        var row = transaction.Query().Rows.Single(row => row.Id == 17);
        transaction.Commit();
        return row.Id;
    }

    private sealed class LegacyImmutable : IImmutable<IModel> { }
    private sealed class LegacyReference : IImmutableForeignKey<PackageConsumerRow>
    {
        public PackageConsumerRow? Value => throw new Exception("Synchronous reference was read.");
        public void Clear() => throw new Exception("Synchronous reference was cleared.");
    }
}
