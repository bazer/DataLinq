using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Config;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Testing;
using Remotion.Linq.Parsing.Structure;

namespace DataLinq.Tests.Compliance;

public class CharPredicateTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CharPredicate_SqlQueryMatchesRawTextCharAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<CharPredicateDb>.Create(
            provider,
            nameof(CharPredicate_SqlQueryMatchesRawTextCharAcrossProviders));

        var inserted = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO charpredicaterows (status) VALUES ('N')");

        var keys = new SqlQuery<CharPredicateRow>(databaseScope.Database.Provider.ReadOnlyAccess)
            .Where("status")
            .EqualTo('N')
            .SelectQuery()
            .ReadKeys()
            .ToArray();

        await Assert.That(inserted).IsEqualTo(1);
        await Assert.That(keys.Length).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CharPredicate_LinqMatchesRawTextCharAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<CharPredicateDb>.Create(
            provider,
            nameof(CharPredicate_LinqMatchesRawTextCharAcrossProviders));

        var inserted = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO charpredicaterows (status) VALUES ('N')");

        var row = databaseScope.Database.Query().Rows.SingleOrDefault(x => x.Status == 'N');

        await Assert.That(inserted).IsEqualTo(1);
        await Assert.That(row).IsNotNull();
        await Assert.That(row!.Status).IsEqualTo('N');
    }

    [Test]
    public async Task CharPredicate_LinqTranslationMatchesDirectSqlInSQLiteInMemory()
    {
        using var databaseScope = TemporaryModelTestDatabase<CharPredicateDb>.Create(
            TestProviderMatrix.SQLiteInMemory,
            nameof(CharPredicate_LinqTranslationMatchesDirectSqlInSQLiteInMemory));

        var directSelect = new SqlQuery<CharPredicateRow>(databaseScope.Database.Provider.ReadOnlyAccess)
            .Where("status")
            .EqualTo('N')
            .SelectQuery();

        var linqQuery = databaseScope.Database.Query().Rows.Where(x => x.Status == 'N');
        var linqSelect = BuildLinqSelect(databaseScope.Database, linqQuery);

        var directSql = directSelect.ToSql();
        var linqSql = linqSelect.ToSql();

        await Assert.That(linqSql.Text).IsEqualTo(directSql.Text);
        await Assert.That(linqSql.Parameters.Count).IsEqualTo(1);
        await Assert.That(directSql.Parameters.Count).IsEqualTo(1);
        await Assert.That(linqSql.Parameters.Single().Value).IsEqualTo(directSql.Parameters.Single().Value);
        await Assert.That(linqSql.Parameters.Single().Value?.GetType()).IsEqualTo(directSql.Parameters.Single().Value?.GetType());
    }

    private static Select<CharPredicateRow> BuildLinqSelect(Database<CharPredicateDb> database, IQueryable<CharPredicateRow> query)
    {
        var queryParser = QueryParser.CreateDefault();
        var queryModel = queryParser.GetParsedQuery(query.Expression);
        var table = database.Provider.Metadata.TableModels
            .Single(x => x.Model.CsType.Type == typeof(CharPredicateRow))
            .Table;
        var queryExecutorType = typeof(Database<CharPredicateDb>).Assembly.GetType("DataLinq.Linq.QueryExecutor", throwOnError: true)!;
        var executor = Activator.CreateInstance(
            queryExecutorType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [database.Provider.ReadOnlyAccess, table],
            culture: null)!;
        var parseMethod = queryExecutorType
            .GetMethod("ParseQueryModel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(CharPredicateRow));

        return (Select<CharPredicateRow>)parseMethod.Invoke(executor, [queryModel])!;
    }
}

[Database("charpredicate")]
public sealed partial class CharPredicateDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<CharPredicateRow> Rows { get; } = new(dataSource);
}

[Table("charpredicaterows")]
public abstract partial class CharPredicateRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<CharPredicateRow, CharPredicateDb>(rowData, dataSource), ITableModel<CharPredicateDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int? Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "char", 1)]
    [Type(DatabaseType.MariaDB, "char", 1)]
    [Column("status")]
    public abstract char Status { get; }
}
