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

public class EnumDefaultSchemaTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task GeneratedEnumDefaults_RespectNumericAndNativeEnumStorage(TestProviderDescriptor provider)
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<ServerEnumDefaultsDb>().ValueOrException();
        var columns = metadata.TableModels.Single().Table.Columns;
        var numeric = columns.Single(x => x.DbName == "numeric_status").ValueProperty.GetDefaultAttribute()!;
        await Assert.That(numeric.Value).IsTypeOf<int>();
        await Assert.That(numeric.Value).IsEqualTo(1);
        await Assert.That(numeric.CodeExpression).IsEqualTo("ServerRowStatus.Inactive");
        var factory = SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider.DatabaseType);
        await Assert.That(factory.GetDefaultValue(columns.Single(x => x.DbName == "numeric_status"))).IsEqualTo("1");
        await Assert.That(factory.GetDefaultValue(columns.Single(x => x.DbName == "native_status"))).IsEqualTo("'inactive'");

        using var schema = ServerSchemaDatabase.Create(provider, nameof(GeneratedEnumDefaults_RespectNumericAndNativeEnumStorage));
        schema.ExecuteNonQuery(factory.GetCreateTables(metadata, true).ValueOrException().Text);
        schema.ExecuteNonQuery("INSERT INTO server_enum_defaults (id) VALUES (1)");
        using var connection = new MySqlConnection(schema.Connection.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT numeric_status, combined, unnamed, negative, native_status FROM server_enum_defaults";
        using (var reader = command.ExecuteReader())
        {
            await Assert.That(reader.Read()).IsTrue();
            await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
            await Assert.That(reader.GetInt32(1)).IsEqualTo(3);
            await Assert.That(reader.GetInt32(2)).IsEqualTo(7);
            await Assert.That(reader.GetInt32(3)).IsEqualTo(-7);
            await Assert.That(reader.GetString(4)).IsEqualTo("inactive");
            await Assert.That(reader.Read()).IsFalse();
        }
        command.CommandText = "SELECT DATA_TYPE, COLUMN_DEFAULT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'server_enum_defaults' AND COLUMN_NAME = 'numeric_status'";
        using var schemaReader = command.ExecuteReader();
        await Assert.That(schemaReader.Read()).IsTrue();
        await Assert.That(schemaReader.GetString(0)).IsEqualTo("int");
        await Assert.That(schemaReader.GetString(1)).IsEqualTo("1");
    }
}

[Flags]
public enum ServerRowStatus { Inactive = 1, Active = 2, Negative = -7 }

[Database("server_enum_defaults")]
public partial class ServerEnumDefaultsDb(DataSourceAccess source) : IDatabaseModel<ServerEnumDefaultsDb>
{
    public DbRead<ServerEnumDefaultsRow> Rows { get; } = new(source);
}

[Table("server_enum_defaults")]
public abstract partial class ServerEnumDefaultsRow(IRowData data, IDataSourceAccess source)
    : Immutable<ServerEnumDefaultsRow, ServerEnumDefaultsDb>(data, source), ITableModel<ServerEnumDefaultsDb>
{
    [PrimaryKey, Column("id"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int")]
    public abstract int Id { get; }
    [Column("numeric_status"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int"), Default(ServerRowStatus.Inactive)]
    public abstract ServerRowStatus NumericStatus { get; }
    [Column("combined"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int"), Default(ServerRowStatus.Inactive | ServerRowStatus.Active)]
    public abstract ServerRowStatus Combined { get; }
    [Column("unnamed"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int"), Default((ServerRowStatus)7)]
    public abstract ServerRowStatus Unnamed { get; }
    [Column("negative"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int"), Default(ServerRowStatus.Negative)]
    public abstract ServerRowStatus Negative { get; }
    [Column("native_status"), Type(DatabaseType.MySQL, "enum"), Type(DatabaseType.MariaDB, "enum"), Enum("inactive", "active"), Default(ServerRowStatus.Inactive)]
    public abstract ServerRowStatus NativeStatus { get; }
}
