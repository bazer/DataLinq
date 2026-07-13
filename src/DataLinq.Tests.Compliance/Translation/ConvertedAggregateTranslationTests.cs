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

public sealed class ConvertedAggregateTranslationTests
{
    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarAggregates_RejectConverterBackedSelectorsBeforeSqlExecution(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<ConvertedAggregateDb>.Create(
            provider,
            nameof(ScalarAggregates_RejectConverterBackedSelectorsBeforeSqlExecution));
        Seed(databaseScope.Database);

        try
        {
            DataLinqMetrics.Reset();

            var sum = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows.Sum(row => row.Amount));
            var min = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows.Min(row => row.Amount));
            var max = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows.Max(row => row.Amount));
            var average = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows.Average(row => row.Amount));
            var rejectionSnapshot = DataLinqMetrics.Snapshot();

            await AssertAggregateDiagnostic(sum, nameof(Queryable.Sum));
            await AssertAggregateDiagnostic(min, nameof(Queryable.Min));
            await AssertAggregateDiagnostic(max, nameof(Queryable.Max));
            await AssertAggregateDiagnostic(average, nameof(Queryable.Average));
            await Assert.That(rejectionSnapshot.Commands.TotalExecutions).IsEqualTo(0);
            await Assert.That(rejectionSnapshot.Queries.ScalarExecutions).IsEqualTo(0);

            await Assert.That(databaseScope.Database.Query().Rows.Count()).IsEqualTo(2);
            await Assert.That(databaseScope.Database.Query().Rows.Any()).IsTrue();
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedAggregates_RejectConverterBackedSelectorsBeforeSqlExecution(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<ConvertedAggregateDb>.Create(
            provider,
            nameof(GroupedAggregates_RejectConverterBackedSelectorsBeforeSqlExecution));
        Seed(databaseScope.Database);

        try
        {
            DataLinqMetrics.Reset();

            var sum = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows
                    .GroupBy(row => row.Bucket)
                    .Select(group => new { group.Key, Value = group.Sum(row => row.Amount) })
                    .ToList());
            var min = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows
                    .GroupBy(row => row.Bucket)
                    .Select(group => new { group.Key, Value = group.Min(row => row.Amount) })
                    .ToList());
            var max = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows
                    .GroupBy(row => row.Bucket)
                    .Select(group => new { group.Key, Value = group.Max(row => row.Amount) })
                    .ToList());
            var average = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows
                    .GroupBy(row => row.Bucket)
                    .Select(group => new { group.Key, Value = group.Average(row => row.Amount) })
                    .ToList());
            var having = Capture<QueryTranslationException>(() =>
                databaseScope.Database.Query().Rows
                    .GroupBy(row => row.Bucket)
                    .Where(group => group.Sum(row => row.Amount) > 0)
                    .Select(group => new { group.Key, Count = group.Count() })
                    .ToList());
            var rejectionSnapshot = DataLinqMetrics.Snapshot();

            await AssertAggregateDiagnostic(sum, nameof(Enumerable.Sum));
            await AssertAggregateDiagnostic(min, nameof(Enumerable.Min));
            await AssertAggregateDiagnostic(max, nameof(Enumerable.Max));
            await AssertAggregateDiagnostic(average, nameof(Enumerable.Average));
            await AssertAggregateDiagnostic(having, nameof(Enumerable.Sum));
            await Assert.That(rejectionSnapshot.Commands.TotalExecutions).IsEqualTo(0);

            var counts = databaseScope.Database.Query().Rows
                .GroupBy(row => row.Bucket)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToList();

            await Assert.That(counts.Count).IsEqualTo(1);
            await Assert.That(counts[0].Key).IsEqualTo("alpha");
            await Assert.That(counts[0].Count).IsEqualTo(2);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    private static void Seed(Database<ConvertedAggregateDb> database)
    {
        database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO converted_aggregate_rows (id, bucket, amount) VALUES " +
            "(1, 'alpha', -2), (2, 'alpha', -3)");
    }

    private static async Task AssertAggregateDiagnostic(
        QueryTranslationException exception,
        string operatorName)
    {
        await Assert.That(exception.Message).Contains($"Aggregate operator '{operatorName}'");
        await Assert.That(exception.Message).Contains("'converted_aggregate_rows.amount'");
        await Assert.That(exception.Message).Contains("Scalar converters declare value conversion only");
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

public sealed class ComplementIntConverter : DataLinqScalarConverter<int, int>
{
    public override int ToProvider(int modelValue, in ScalarConversionContext context) =>
        ~modelValue;

    public override int FromProvider(int providerValue, in ScalarConversionContext context) =>
        ~providerValue;
}

[Database("convertedaggregates")]
public sealed partial class ConvertedAggregateDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<ConvertedAggregateRow> Rows { get; } = new(dataSource);
}

[Table("converted_aggregate_rows")]
public abstract partial class ConvertedAggregateRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<ConvertedAggregateRow, ConvertedAggregateDb>(rowData, dataSource),
      ITableModel<ConvertedAggregateDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 20)]
    [Type(DatabaseType.MariaDB, "varchar", 20)]
    [Column("bucket")]
    public abstract string Bucket { get; }

    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(ComplementIntConverter))]
    [Column("amount")]
    public abstract int Amount { get; }
}
