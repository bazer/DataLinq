using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.MySql;
using DataLinq.Mutation;
using DataLinq.Testing;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class ReservedKeywordMutationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task MySql97LibraryKeyword_IsQuotedAcrossSchemaMetadataQueryAndMutation(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<ReservedKeywordTestDatabase>.Create(
            provider,
            nameof(MySql97LibraryKeyword_IsQuotedAcrossSchemaMetadataQueryAndMutation));

        var factory = MetadataFromSqlFactory.GetSqlFactory(
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true },
            provider.DatabaseType);
        var metadata = factory.ParseDatabase(
                "ReservedKeywordDb",
                "ReservedKeywordDb",
                "DataLinq.Tests.ReservedKeywords",
                scope.Connection.DataSourceName,
                scope.Connection.ConnectionString)
            .ValueOrException();
        var table = metadata.TableModels.Single(static model => model.Table.DbName == "reserved_keyword_rows");
        var generatedModel = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(metadata)
            .Single(static file => file.contents.Contains("[Table(\"reserved_keyword_rows\")]", StringComparison.Ordinal));
        var generatedSql = SqlFromMetadataFactory
            .GetFactoryFromDatabaseType(provider.DatabaseType)
            .GetCreateTables(metadata, foreignKeyRestrict: false)
            .ValueOrException();

        await Assert.That(table.Table.Columns.Any(static column => column.DbName == "Library")).IsTrue();
        await Assert.That(generatedModel.contents).Contains("[Column(\"Library\")]");
        await Assert.That(generatedSql.Text).Contains("`Library`");

        var inserted = scope.Database.Insert(new MutableReservedKeywordRow
        {
            Id = 1,
            Library = "before"
        });

        using var transaction = scope.Database.Transaction(TransactionType.ReadAndWrite);
        var mutable = inserted.Mutate();
        mutable.Library = "after";

        transaction.Save(mutable);
        transaction.Commit();
        scope.Database.Provider.State.ClearCache();

        var updated = scope.Database.Query().Rows.Single(x => x.Id == 1);

        await Assert.That(updated.Library).IsEqualTo("after");
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

    [Column("Library")]
    [Type(DatabaseType.MySQL, "varchar", 100)]
    [Type(DatabaseType.MariaDB, "varchar", 100)]
    public abstract string Library { get; }
}
