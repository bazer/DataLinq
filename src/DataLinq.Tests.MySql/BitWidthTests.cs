using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.Testing;
using MySqlConnector;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class BitWidthTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task BitWidthsAndDefaultsSurviveSchemaRegeneration(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(BitWidthsAndDefaultsSurviveSchemaRegeneration),
            "CREATE TABLE bit_defaults (id INT PRIMARY KEY, one_bit BIT(1) NOT NULL DEFAULT b'1', eight_bits BIT(8) NOT NULL DEFAULT b'10101010', sixtyfour_bits BIT(64) NOT NULL DEFAULT b'1111111111111111111111111111111111111111111111111111111111111111', optional_bits BIT(8) NULL)");
        var metadata = schema.ParseDatabase("BitsDb", "BitsDb", "DataLinq.Tests.GeneratedBits");
        var table = metadata.TableModels.Single().Table;
        var one = table.GetColumnByDbName("one_bit");
        var eight = table.GetColumnByDbName("eight_bits");
        var sixtyFour = table.GetColumnByDbName("sixtyfour_bits");
        var optional = table.GetColumnByDbName("optional_bits");

        await Assert.That(one.ValueProperty.CsType.Type).IsEqualTo(typeof(bool));
        await Assert.That(eight.ValueProperty.CsType.Type).IsEqualTo(typeof(ulong));
        await Assert.That(sixtyFour.ValueProperty.CsType.Type).IsEqualTo(typeof(ulong));
        await Assert.That(optional.ValueProperty.CsNullable).IsTrue();
        await Assert.That(eight.GetDbTypeFor(provider.DatabaseType)!.Length).IsEqualTo((ulong?)8);
        await Assert.That(sixtyFour.GetDbTypeFor(provider.DatabaseType)!.Length).IsEqualTo((ulong?)64);
        await Assert.That(eight.ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo((object)170UL);
        await Assert.That(sixtyFour.ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo((object)ulong.MaxValue);
        await Assert.That(sixtyFour.ValueProperty.GetDefaultValueCode()).IsEqualTo("18446744073709551615UL");

        var sql = SqlFromMetadataFactory.GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(metadata, foreignKeyRestrict: false).ValueOrException();
        schema.ExecuteNonQuery("DROP TABLE bit_defaults");
        schema.ExecuteNonQuery(sql.Text);
        schema.ExecuteNonQuery("INSERT INTO bit_defaults (id) VALUES (1)");
        using var connection = new MySqlConnection(schema.Connection.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT one_bit, eight_bits, sixtyfour_bits, optional_bits FROM bit_defaults";
        using var reader = new SqlDataLinqDataReader(command.ExecuteReader());
        await Assert.That(reader.ReadNextRow()).IsTrue();
        await Assert.That(reader.GetValue<bool>(one)).IsTrue();
        await Assert.That(reader.GetValue<ulong>(eight)).IsEqualTo(170UL);
        await Assert.That(reader.GetValue<ulong>(sixtyFour)).IsEqualTo(ulong.MaxValue);
        await Assert.That(reader.GetValue<ulong?>(optional)).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task WideBitPropertiesPreserveAllBitsThroughMutationsAndReads(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<BitWidthDatabase>.Create(provider, nameof(WideBitPropertiesPreserveAllBitsThroughMutationsAndReads));
        var bytes = new ulong[] { 0, 1, 3, 255 };
        var words = new ulong[] { 0, 1, 1UL << 63, ulong.MaxValue };
        for (var index = 0; index < bytes.Length; index++)
            scope.Database.Insert(new MutableBitWidthRow { Id = index, Bits8 = bytes[index], Bits64 = words[index] });

        scope.Database.Provider.State.ClearCache();
        var rows = scope.Database.Query().Rows.OrderBy(row => row.Id).ToArray();
        await Assert.That(rows.Select(row => row.Bits8).ToArray()).IsEquivalentTo(bytes);
        await Assert.That(rows.Select(row => row.Bits64).ToArray()).IsEquivalentTo(words);
    }
}

public partial class BitWidthDatabase(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<BitWidthRow> Rows { get; } = new(source);
}

[Table("bit_rows")]
public abstract partial class BitWidthRow(IRowData data, IDataSourceAccess source)
    : Immutable<BitWidthRow, BitWidthDatabase>(data, source), ITableModel<BitWidthDatabase>
{
    [PrimaryKey, Column("id"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int")]
    public abstract int Id { get; }
    [Column("bits8"), Type(DatabaseType.MySQL, "bit", 8), Type(DatabaseType.MariaDB, "bit", 8)]
    public abstract ulong Bits8 { get; }
    [Column("bits64"), Type(DatabaseType.MySQL, "bit", 64), Type(DatabaseType.MariaDB, "bit", 64)]
    public abstract ulong Bits64 { get; }
}
