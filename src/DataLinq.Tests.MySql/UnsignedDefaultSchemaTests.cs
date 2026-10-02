using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.Testing;
using MySqlConnector;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class UnsignedDefaultSchemaTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task UnsignedColumnsWithDefaults_CreateAndStoreDefaults(TestProviderDescriptor provider)
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<UnsignedDefaultsDb>().ValueOrException();
        var factory = SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider.DatabaseType);
        using var schema = ServerSchemaDatabase.Create(provider, nameof(UnsignedColumnsWithDefaults_CreateAndStoreDefaults));
        schema.ExecuteNonQuery(factory.GetCreateTables(metadata, true).ValueOrException().Text);
        schema.ExecuteNonQuery("INSERT INTO unsigned_defaults () VALUES ()");
        using var connection = new MySqlConnection(schema.Connection.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, tiny, small, regular, big, optional, signed_value, no_default FROM unsigned_defaults";
        using (var reader = command.ExecuteReader())
        {
            await Assert.That(reader.Read()).IsTrue();
            await Assert.That(reader.GetUInt32(0)).IsEqualTo(1U);
            for (var i = 1; i <= 5; i++)
                await Assert.That(reader.GetUInt64(i)).IsEqualTo(3UL);
            await Assert.That(reader.GetInt32(6)).IsEqualTo(-3);
            await Assert.That(reader.IsDBNull(7)).IsTrue();
        }
        command.CommandText = "SELECT COLUMN_NAME, COLUMN_TYPE, COLUMN_DEFAULT, IS_NULLABLE, EXTRA FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'unsigned_defaults'";
        using var columns = command.ExecuteReader();
        var count = 0;
        while (columns.Read())
        {
            count++;
            var name = columns.GetString(0);
            await Assert.That(columns.GetString(1).Contains("unsigned", StringComparison.OrdinalIgnoreCase)).IsEqualTo(name != "signed_value");
            await Assert.That(columns.GetString(3)).IsEqualTo(name is "optional" or "no_default" ? "YES" : "NO");
            if (name == "id")
                await Assert.That(columns.GetString(4).Contains("auto_increment")).IsTrue();
            else if (name != "no_default")
                await Assert.That(columns.GetString(2)).IsEqualTo(name == "signed_value" ? "-3" : "3");
        }
        await Assert.That(count).IsEqualTo(8);
    }
}

[Database("unsigned_defaults")]
public partial class UnsignedDefaultsDb(DataSourceAccess source) : IDatabaseModel<UnsignedDefaultsDb>
{
    public DbRead<UnsignedDefaultsRow> Rows { get; } = new(source);
}

[Table("unsigned_defaults")]
public abstract partial class UnsignedDefaultsRow(IRowData data, IDataSourceAccess source)
    : Immutable<UnsignedDefaultsRow, UnsignedDefaultsDb>(data, source), ITableModel<UnsignedDefaultsDb>
{
    [PrimaryKey, AutoIncrement, Column("id"), Type(DatabaseType.MySQL, "int", 10, false), Type(DatabaseType.MariaDB, "int", 10, false)]
    public abstract uint? Id { get; }
    [Column("tiny"), Type(DatabaseType.MySQL, "tinyint", 3, false), Type(DatabaseType.MariaDB, "tinyint", 3, false), Default(3)]
    public abstract byte Tiny { get; }
    [Column("small"), Type(DatabaseType.MySQL, "smallint", 5, false), Type(DatabaseType.MariaDB, "smallint", 5, false), Default(3)]
    public abstract ushort Small { get; }
    [Column("regular"), Type(DatabaseType.MySQL, "int", 10, false), Type(DatabaseType.MariaDB, "int", 10, false), Default(3)]
    public abstract uint Regular { get; }
    [Column("big"), Type(DatabaseType.MySQL, "bigint", 20, false), Type(DatabaseType.MariaDB, "bigint", 20, false), Default(3)]
    public abstract ulong Big { get; }
    [Nullable, Column("optional"), Type(DatabaseType.MySQL, "int", 10, false), Type(DatabaseType.MariaDB, "int", 10, false), Default(3)]
    public abstract uint? Optional { get; }
    [Column("signed_value"), Type(DatabaseType.MySQL, "int", 10, true), Type(DatabaseType.MariaDB, "int", 10, true), Default(-3)]
    public abstract int SignedValue { get; }
    [Nullable, Column("no_default"), Type(DatabaseType.MySQL, "int", 10, false), Type(DatabaseType.MariaDB, "int", 10, false)]
    public abstract uint? NoDefault { get; }
}
