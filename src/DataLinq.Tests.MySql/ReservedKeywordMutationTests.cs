using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.MySql;

public class ReservedKeywordMutationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task Update_ColumnNamedReferences_QuotesSetColumn(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<ReservedKeywordTestDatabase>.Create(
            provider,
            nameof(Update_ColumnNamedReferences_QuotesSetColumn));

        var inserted = scope.Database.Insert(new MutableReservedKeywordRow
        {
            Id = 1,
            References = "before"
        });

        using var transaction = scope.Database.Transaction(TransactionType.ReadAndWrite);
        var mutable = inserted.Mutate();
        mutable.References = "after";

        transaction.Save(mutable);
        transaction.Commit();
        scope.Database.Provider.State.ClearCache();

        var updated = scope.Database.Query().Rows.Single(x => x.Id == 1);

        await Assert.That(updated.References).IsEqualTo("after");
    }
}

public partial class ReservedKeywordTestDatabase(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<ReservedKeywordRow> Rows { get; } = new(dataSource);
}

[Table("reserved_keyword_rows")]
public abstract partial class ReservedKeywordRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<ReservedKeywordRow, ReservedKeywordTestDatabase>(rowData, dataSource), ITableModel<ReservedKeywordTestDatabase>
{
    [PrimaryKey]
    [Column("Id")]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.MariaDB, "int")]
    public abstract int Id { get; }

    [Column("References")]
    [Type(DatabaseType.MySQL, "varchar", 100)]
    [Type(DatabaseType.MariaDB, "varchar", 100)]
    public abstract string References { get; }
}
