using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class ServerDefaultInsertTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Insert_DefaultOnlyAndExplicitAssignmentsRoundTrip(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<ServerDefaultInsertDb>.Create(
            provider,
            nameof(Insert_DefaultOnlyAndExplicitAssignmentsRoundTrip));

        var unset = databaseScope.Database.Insert(new MutableServerDefaultInsertRow());
        var assignedNull = databaseScope.Database.Insert(new MutableServerDefaultInsertRow
        {
            ServerValue = null
        });
        var assignedValue = databaseScope.Database.Insert(new MutableServerDefaultInsertRow
        {
            ServerValue = "client-value"
        });
        var assignedNullIdentity = databaseScope.Database.Insert(new MutableServerDefaultInsertRow
        {
            Id = null
        });
        var assignedIdentity = databaseScope.Database.Insert(new MutableServerDefaultInsertRow
        {
            Id = 7001
        });

        databaseScope.Database.Provider.State.ClearCache();
        var reloadedUnset = databaseScope.Database.Query().Rows.Single(row => row.Id == unset.Id);
        var reloadedAssignedNull = databaseScope.Database.Query().Rows.Single(row => row.Id == assignedNull.Id);
        var reloadedAssignedValue = databaseScope.Database.Query().Rows.Single(row => row.Id == assignedValue.Id);
        var reloadedAssignedNullIdentity = databaseScope.Database.Query().Rows.Single(row => row.Id == assignedNullIdentity.Id);
        var reloadedAssignedIdentity = databaseScope.Database.Query().Rows.Single(row => row.Id == assignedIdentity.Id);

        await Assert.That(unset.Id).IsNotNull();
        await Assert.That(assignedNull.Id).IsNotNull();
        await Assert.That(assignedValue.Id).IsNotNull();
        await Assert.That(assignedNullIdentity.Id).IsNotNull();
        await Assert.That(assignedIdentity.Id).IsEqualTo(7001);
        await Assert.That(unset.ServerValue).IsEqualTo("server-generated");
        await Assert.That(assignedNull.ServerValue).IsNull();
        await Assert.That(assignedValue.ServerValue).IsEqualTo("client-value");
        await Assert.That(assignedNullIdentity.ServerValue).IsEqualTo("server-generated");
        await Assert.That(assignedIdentity.ServerValue).IsEqualTo("server-generated");
        await Assert.That(reloadedUnset.ServerValue).IsEqualTo("server-generated");
        await Assert.That(reloadedAssignedNull.ServerValue).IsNull();
        await Assert.That(reloadedAssignedValue.ServerValue).IsEqualTo("client-value");
        await Assert.That(reloadedAssignedNullIdentity.ServerValue).IsEqualTo("server-generated");
        await Assert.That(reloadedAssignedIdentity.ServerValue).IsEqualTo("server-generated");
    }
}

[Database("serverdefaultinsert")]
public sealed partial class ServerDefaultInsertDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<ServerDefaultInsertRow> Rows { get; } = new(dataSource);
}

[Table("server_default_insert_rows")]
public abstract partial class ServerDefaultInsertRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<ServerDefaultInsertRow, ServerDefaultInsertDb>(rowData, dataSource), ITableModel<ServerDefaultInsertDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int? Id { get; }

    [Nullable]
    [DefaultSql(DatabaseType.Default, "'server-generated'")]
    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("server_value")]
    public abstract string? ServerValue { get; }
}
