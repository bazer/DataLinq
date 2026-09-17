using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Query;
using DataLinq.SQLite;
using DataLinq.MySql;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public class SqlIdentifierEscapingTests
{
    [Test]
    [Arguments("\"")]
    [Arguments("`")]
    public async Task QuotesEachIdentifierComponent(string delimiter)
    {
        var name = "some" + delimiter + "name";
        var expected = delimiter + "some" + delimiter + delimiter + "name" + delimiter;
        var sql = new Sql();
        SqlIdentifier.Append(sql, name, delimiter);
        await Assert.That(SqlIdentifier.Quote(name, delimiter)).IsEqualTo(expected);
        await Assert.That(sql.Text).IsEqualTo(expected);
        await Assert.That(Operand.Column(name, "select").FormatName(delimiter)).IsEqualTo(delimiter + "select" + delimiter + "." + expected);
    }

    [Test]
    public async Task PredicateNameCannotExpandSelectedRows()
    {
        using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:");
        provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE departments (dept_no TEXT PRIMARY KEY, dept_name TEXT NOT NULL); INSERT INTO departments VALUES ('d001','one'),('d002','two')");
        var query = new SqlQuery<Department>(provider.ReadOnlyAccess);
        query.Where("dept_no\" = 'd001' OR \"dept_no").EqualTo("d002");
        // The payload is now an unknown column name, not an OR expression.
        await Assert.That(() => query.SelectQuery().ReadReader().Count()).Throws<SqliteException>();
    }

    [Test]
    public async Task QuotedSchemaNamesAndAliasesWorkThroughSqlAndLinq()
    {
        using var provider = new SQLiteProvider<IdentifierEscapingDb>("Data Source=:memory:");
        var schema = new SqlFromSQLiteFactory().GetCreateTables(provider.Metadata, foreignKeyRestrict: true);
        await Assert.That(schema.HasFailed).IsFalse();
        provider.DatabaseAccess.ExecuteNonQuery(schema.Value.Text);
        provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO \"odd\"\"`table\" VALUES (1,'one'),(2,'two')");
        const string alias = "select\" joined --";
        var query = new SqlQuery<IdentifierEscapingRow>(provider.ReadOnlyAccess, alias);
        query.Where("id\"`value", alias).EqualTo(2);
        query.OrderBy("id\"`value", alias);
        await Assert.That(query.SelectQuery().ReadRows().Single().GetValue(query.Table.GetColumnByDbName("text\"`value"))).IsEqualTo("two");
        await Assert.That(new DbRead<IdentifierEscapingRow>(provider.ReadOnlyAccess).Where(row => row.Id == 1).Single().Text).IsEqualTo("one");
        await Assert.That(new DbRead<IdentifierEscapingRow>(provider.ReadOnlyAccess).OrderBy(row => row.Id).Select(row => row.Text).ToArray()).IsEquivalentTo(new[] { "one", "two" });
    }

    [Test]
    public async Task MySqlTableAndDatabaseNamesAreQuotedWithoutConnecting()
    {
        using var provider = new MySqlProvider<EmployeesDb>("Server=127.0.0.1;Database=odd`db");
        var sql = provider.GetTableName(new Sql(), "odd`table", "select`alias");
        await Assert.That(sql.Text).IsEqualTo("`odd``db`.`odd``table` `select``alias`");
    }
}

[Database("identifier_escaping")]
public partial class IdentifierEscapingDb(IDataLinqReadSource source) : IDatabaseModel
{
    public DbRead<IdentifierEscapingRow> Rows { get; } = new(source);
}

[Table("odd\"`table")]
public abstract partial class IdentifierEscapingRow(IRowData data, IDataSourceAccess source) : Immutable<IdentifierEscapingRow, IdentifierEscapingDb>(data, source), ITableModel<IdentifierEscapingDb>
{
    [PrimaryKey, Column("id\"`value"), Type(DatabaseType.SQLite, "integer")]
    public abstract int Id { get; }
    [Column("text\"`value"), Type(DatabaseType.SQLite, "text")]
    public abstract string Text { get; }
}
