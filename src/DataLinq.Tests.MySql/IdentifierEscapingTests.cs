using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Testing;

namespace DataLinq.Tests.MySql;

public class IdentifierEscapingTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task EmbeddedDelimitersWorkAcrossSchemaMutationAndQueries(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<EscapedIdentifiersDatabase>.Create(
            provider, nameof(EmbeddedDelimitersWorkAcrossSchemaMutationAndQueries));

        var inserted = scope.Database.Insert(new MutableEscapedIdentifiersRow { Id = 1, Text = "before" });
        using (var transaction = scope.Database.Transaction(TransactionType.ReadAndWrite))
        {
            var mutable = inserted.Mutate();
            mutable.Text = "after";
            transaction.Save(mutable);
            transaction.Commit();
        }

        scope.Database.Provider.State.ClearCache();
        await Assert.That(scope.Database.Query().Rows.Single(row => row.Id == 1).Text).IsEqualTo("after");
        await Assert.That(scope.Database.Query().Rows.OrderBy(row => row.Id).Select(row => row.Text).Single()).IsEqualTo("after");

        const string alias = "select` joined --";
        var query = new SqlQuery<EscapedIdentifiersRow>(scope.Database.Provider.ReadOnlyAccess, alias);
        query.Where("id`value", alias).EqualTo(1);
        query.OrderBy("id`value", alias);
        await Assert.That(query.SelectQuery().ReadRows().Single().GetValue(query.Table.GetColumnByDbName("text`value"))).IsEqualTo("after");
    }
}

public partial class EscapedIdentifiersDatabase(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<EscapedIdentifiersRow> Rows { get; } = new(source);
}

[Table("odd`table")]
public abstract partial class EscapedIdentifiersRow(IRowData data, IDataSourceAccess source)
    : Immutable<EscapedIdentifiersRow, EscapedIdentifiersDatabase>(data, source), ITableModel<EscapedIdentifiersDatabase>
{
    [PrimaryKey, Column("id`value"), Type(DatabaseType.MySQL, "int"), Type(DatabaseType.MariaDB, "int")]
    public abstract int Id { get; }

    [Column("text`value"), Type(DatabaseType.MySQL, "varchar", 100), Type(DatabaseType.MariaDB, "varchar", 100)]
    public abstract string Text { get; }
}
