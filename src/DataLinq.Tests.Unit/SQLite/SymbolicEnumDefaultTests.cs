using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public class SymbolicEnumDefaultTests
{
    [Test]
    public async Task GeneratedEnumDefaults_HaveNumericMetadataAndExecuteAsSqliteDefaults()
    {
        using var database = new SQLiteDatabase<EnumDefaultsDb>("Data Source=:memory:");
        var table = database.Provider.Metadata.TableModels.Single().Table;
        var attribute = table.Columns.Single(x => x.DbName == "status").ValueProperty.GetDefaultAttribute()!;
        await Assert.That(attribute.Value).IsTypeOf<int>();
        await Assert.That(attribute.Value).IsEqualTo(1);
        await Assert.That(attribute.CodeExpression).IsEqualTo("DefaultRowStatus.Inactive");

        var mutable = new MutableEnumDefaultsRow { Id = 1 };
        await Assert.That(mutable.Status).IsEqualTo(DefaultRowStatus.Inactive);
        await Assert.That((int)mutable.Combined).IsEqualTo(3);
        await Assert.That((int)mutable.Unnamed).IsEqualTo(7);
        await Assert.That((int)mutable.Numeric).IsEqualTo(0);
        await Assert.That((int)mutable.Negative).IsEqualTo(-7);

        var createSql = new SqlFromSQLiteFactory().GetCreateTables(database.Provider.Metadata, true).ValueOrException().Text;
        await Assert.That(createSql).Contains("DEFAULT 1");
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = createSql;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO enum_default_rows (id) VALUES (1);";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT status, combined, unnamed, numeric_default, negative FROM enum_default_rows;";
        using var reader = command.ExecuteReader();
        await Assert.That(reader.Read()).IsTrue();
        foreach (var (index, expected) in new[] { (0, 1), (1, 3), (2, 7), (3, 0), (4, -7) })
            await Assert.That(reader.GetInt32(index)).IsEqualTo(expected);
        await Assert.That(reader.Read()).IsFalse();
    }
}

[Flags]
public enum DefaultRowStatus { Inactive = 1, Active = 2, Negative = -7 }

[Database("enum_defaults")]
public partial class EnumDefaultsDb(DataSourceAccess source) : IDatabaseModel<EnumDefaultsDb>
{
    public DbRead<EnumDefaultsRow> Rows { get; } = new(source);
}

[Table("enum_default_rows")]
public abstract partial class EnumDefaultsRow(IRowData data, IDataSourceAccess source)
    : Immutable<EnumDefaultsRow, EnumDefaultsDb>(data, source), ITableModel<EnumDefaultsDb>
{
    [PrimaryKey, Column("id"), Type(DatabaseType.MariaDB, "int", 11)]
    public abstract int Id { get; }
    [Column("status"), Type(DatabaseType.MariaDB, "int", 11), Default(DefaultRowStatus.Inactive)]
    public abstract DefaultRowStatus Status { get; }
    [Column("combined"), Type(DatabaseType.MariaDB, "int", 11), Default(DefaultRowStatus.Inactive | DefaultRowStatus.Active)]
    public abstract DefaultRowStatus Combined { get; }
    [Column("unnamed"), Type(DatabaseType.MariaDB, "int", 11), Default((DefaultRowStatus)7)]
    public abstract DefaultRowStatus Unnamed { get; }
    [Column("numeric_default"), Type(DatabaseType.MariaDB, "int", 11), Default(0)]
    public abstract DefaultRowStatus Numeric { get; }
    [Column("negative"), Type(DatabaseType.MariaDB, "int", 11), Default(DefaultRowStatus.Negative)]
    public abstract DefaultRowStatus Negative { get; }
}
