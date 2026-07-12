using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class TypedIdPredicateTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task TypedIdPredicates_ExecuteEqualityContainsAndLocalAnyAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<TypedIdPredicateDb>.Create(
            provider,
            nameof(TypedIdPredicates_ExecuteEqualityContainsAndLocalAnyAcrossProviders));

        var inserted = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO typedidqueryrows (id, parent_id, name) VALUES " +
            "(101, NULL, 'alpha'), (102, 101, 'beta'), (103, 101, 'gamma')");
        var database = databaseScope.Database;
        var probe = new QueryTypedId(102);
        QueryTypedId? parentProbe = new QueryTypedId(101);
        var selectedIds = new[] { new QueryTypedId(101), new QueryTypedId(103) };

        var directEquality = database.Query().Rows.Count(row => row.Id == probe);
        var reversedEquality = database.Query().Rows.Count(row => probe == row.Id);
        var directInequality = database.Query().Rows.Count(row => row.Id != probe);
        var nullableEquality = database.Query().Rows.Count(row => row.ParentId == parentProbe);
        var nullableNullEquality = database.Query().Rows.Count(row => row.ParentId == null);
        var contains = database.Query().Rows.Count(row => selectedIds.Contains(row.Id));
        var localAny = database.Query().Rows.Count(row => selectedIds.Any(id => id == row.Id));

        await Assert.That(inserted).IsEqualTo(3);
        await Assert.That(directEquality).IsEqualTo(1);
        await Assert.That(reversedEquality).IsEqualTo(1);
        await Assert.That(directInequality).IsEqualTo(2);
        await Assert.That(nullableEquality).IsEqualTo(2);
        await Assert.That(nullableNullEquality).IsEqualTo(1);
        await Assert.That(contains).IsEqualTo(2);
        await Assert.That(localAny).IsEqualTo(2);
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task TypedIdMaterialization_BatchedColdAndOrderedMixedCachePreserveIdentityAndTelemetry(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<TypedIdPredicateDb>.Create(
            provider,
            nameof(TypedIdMaterialization_BatchedColdAndOrderedMixedCachePreserveIdentityAndTelemetry));

        var inserted = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO typedidqueryrows (id, parent_id, name) VALUES " +
            "(101, NULL, 'alpha'), (102, 101, 'beta'), (103, 101, 'gamma')");
        var database = databaseScope.Database;
        var selectedIds = new[] { new QueryTypedId(101), new QueryTypedId(103) };

        database.Provider.State.ClearCache();

        DataLinqMetrics.Reset();
        var coldRows = database.Query().Rows
            .Where(row => selectedIds.Contains(row.Id))
            .ToList();
        var coldSnapshot = DataLinqMetrics.Snapshot();

        await Assert.That(inserted).IsEqualTo(3);
        await Assert.That(coldRows.Select(static row => row.Id).ToArray())
            .IsEquivalentTo(new[] { new QueryTypedId(101), new QueryTypedId(103) });
        await Assert.That(coldSnapshot.Queries.EntityExecutions).IsEqualTo(1);
        await Assert.That(coldSnapshot.Commands.ReaderExecutions).IsEqualTo(2);
        await Assert.That(coldSnapshot.RowCache.Hits).IsEqualTo(0);
        await Assert.That(coldSnapshot.RowCache.Misses).IsEqualTo(2);
        await Assert.That(coldSnapshot.RowCache.Stores).IsEqualTo(2);
        await Assert.That(coldSnapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(2);
        await Assert.That(coldSnapshot.RowCache.Materializations).IsEqualTo(2);

        database.Provider.State.ClearCache();
        var warmId = new QueryTypedId(103);
        var warmRow = database.Query().Rows
            .Single(row => row.Id == warmId);

        DataLinqMetrics.Reset();
        var orderedRows = database.Query().Rows
            .Where(row => selectedIds.Contains(row.Id))
            .OrderByDescending(row => row.Name)
            .ToList();
        var mixedSnapshot = DataLinqMetrics.Snapshot();

        await Assert.That(orderedRows.Count).IsEqualTo(2);
        await Assert.That(orderedRows[0].Name).IsEqualTo("gamma");
        await Assert.That(orderedRows[1].Name).IsEqualTo("alpha");
        await Assert.That(orderedRows.Single(row => row.Id == new QueryTypedId(103)))
            .IsSameReferenceAs(warmRow);
        await Assert.That(mixedSnapshot.Queries.EntityExecutions).IsEqualTo(1);
        await Assert.That(mixedSnapshot.Commands.ReaderExecutions).IsEqualTo(2);
        await Assert.That(mixedSnapshot.RowCache.Hits).IsEqualTo(1);
        await Assert.That(mixedSnapshot.RowCache.Misses).IsEqualTo(1);
        await Assert.That(mixedSnapshot.RowCache.Stores).IsEqualTo(1);
        await Assert.That(mixedSnapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(1);
        await Assert.That(mixedSnapshot.RowCache.Materializations).IsEqualTo(1);
    }

    [Test]
    public async Task TypedIdPredicates_BindCanonicalProviderValuesAcrossTemplateAndMembershipPaths()
    {
        using var databaseScope = TemporaryModelTestDatabase<TypedIdPredicateDb>.Create(
            TestProviderMatrix.SQLiteInMemory,
            nameof(TypedIdPredicates_BindCanonicalProviderValuesAcrossTemplateAndMembershipPaths));

        var firstId = new QueryTypedId(201);
        var secondId = new QueryTypedId(202);
        var selectedIds = new[] { new QueryTypedId(203), new QueryTypedId(204) };
        var firstQuery = databaseScope.Database.Query().Rows.Where(row => row.Id == firstId);
        var firstSelect = CurrentQueryTranslationInspection.BuildSelect(
            databaseScope.Database,
            firstQuery);
        var firstEquality = firstSelect.ToSql();
        var secondEquality = CurrentQueryTranslationInspection.BuildSql(
            databaseScope.Database,
            databaseScope.Database.Query().Rows.Where(row => row.Id == secondId));
        var contains = CurrentQueryTranslationInspection.BuildSql(
            databaseScope.Database,
            databaseScope.Database.Query().Rows.Where(row => selectedIds.Contains(row.Id)));
        var localAny = CurrentQueryTranslationInspection.BuildSql(
            databaseScope.Database,
            databaseScope.Database.Query().Rows.Where(row => selectedIds.Any(id => id == row.Id)));
        var canonicalKey = firstSelect.Query.TryGetSimplePrimaryKey();
        var canonicalKeyValue = canonicalKey
            ?? throw new InvalidOperationException("Expected the direct equality predicate to expose a canonical primary key.");

        await Assert.That(firstEquality.Text).IsEqualTo(secondEquality.Text);
        await Assert.That(firstEquality.Parameters.Select(static parameter => (int)parameter.Value!).ToArray()).IsEquivalentTo([201]);
        await Assert.That(secondEquality.Parameters.Select(static parameter => (int)parameter.Value!).ToArray()).IsEquivalentTo([202]);
        await Assert.That(contains.Parameters.Select(static parameter => (int)parameter.Value!).ToArray()).IsEquivalentTo([203, 204]);
        await Assert.That(localAny.Parameters.Select(static parameter => (int)parameter.Value!).ToArray()).IsEquivalentTo([203, 204]);
        await Assert.That(contains.Parameters.All(static parameter => parameter.Value?.GetType() == typeof(int))).IsTrue();
        await Assert.That(localAny.Parameters.All(static parameter => parameter.Value?.GetType() == typeof(int))).IsTrue();
        await Assert.That(canonicalKey).IsNotNull();
        await Assert.That(canonicalKeyValue.GetValue(0)).IsEqualTo(201);
        await Assert.That(canonicalKeyValue.GetValue(0)?.GetType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task TypedIdOrdering_IsRejectedBeforeSqlExecution()
    {
        using var databaseScope = TemporaryModelTestDatabase<TypedIdPredicateDb>.Create(
            TestProviderMatrix.SQLiteInMemory,
            nameof(TypedIdOrdering_IsRejectedBeforeSqlExecution));
        var probe = new QueryTypedId(201);

        var exception = Capture<QueryTranslationException>(() =>
            CurrentQueryTranslationInspection.BuildSql(
                databaseScope.Database,
                databaseScope.Database.Query().Rows.Where(row => row.Id > probe)));

        await Assert.That(exception.Message).Contains("do not declare whether they preserve ordering");
        await Assert.That(exception.Message).Contains("typedidqueryrows.id");
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }
}

public readonly record struct QueryTypedId(int Value)
{
    public static bool operator >(QueryTypedId left, QueryTypedId right) => left.Value > right.Value;
    public static bool operator <(QueryTypedId left, QueryTypedId right) => left.Value < right.Value;
    public static bool operator >=(QueryTypedId left, QueryTypedId right) => left.Value >= right.Value;
    public static bool operator <=(QueryTypedId left, QueryTypedId right) => left.Value <= right.Value;
}

public sealed class QueryTypedIdConverter : DataLinqScalarConverter<QueryTypedId, int>
{
    public override int ToProvider(QueryTypedId modelValue, in ScalarConversionContext context) =>
        modelValue.Value;

    public override QueryTypedId FromProvider(int providerValue, in ScalarConversionContext context) =>
        new(providerValue);
}

[UseCache]
[Database("typedidpredicates")]
public sealed partial class TypedIdPredicateDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<TypedIdQueryRow> Rows { get; } = new(dataSource);
}

[Table("typedidqueryrows")]
public abstract partial class TypedIdQueryRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<TypedIdQueryRow, TypedIdPredicateDb>(rowData, dataSource), ITableModel<TypedIdPredicateDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(QueryTypedIdConverter))]
    [Column("id")]
    public abstract QueryTypedId Id { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(QueryTypedIdConverter))]
    [Column("parent_id")]
    public abstract QueryTypedId? ParentId { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }
}
