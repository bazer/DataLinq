using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class TelemetryTests
{
    [Test]
    [NotInParallel]
    public async Task Snapshot_CapturesCommandMetricsForSQLite()
    {
        await WithTelemetryDatabase(async db =>
        {
            await Assert.That(db.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('alpha')")).IsEqualTo(1);
            await Assert.That(Convert.ToInt64(db.Provider.DatabaseAccess.ExecuteScalar("SELECT COUNT(*) FROM telemetryrows"))).IsEqualTo(1L);

            using (var reader = db.Provider.DatabaseAccess.ExecuteReader("SELECT id, name FROM telemetryrows ORDER BY id"))
            {
                await Assert.That(reader.ReadNextRow()).IsTrue();
                await Assert.That(reader.GetString(1)).IsEqualTo("alpha");
                await Assert.That(reader.ReadNextRow()).IsFalse();
            }

            var snapshot = DataLinqMetrics.Snapshot();
            var provider = snapshot.Providers.Single();

            await Assert.That(provider.DatabaseType).IsEqualTo(DatabaseType.SQLite);
            await Assert.That(snapshot.Commands.NonQueryExecutions).IsGreaterThanOrEqualTo(1);
            await Assert.That(snapshot.Commands.ScalarExecutions).IsEqualTo(1);
            await Assert.That(snapshot.Commands.ReaderExecutions).IsEqualTo(1);
            await Assert.That(snapshot.Commands.TotalExecutions).IsGreaterThanOrEqualTo(3);
            await Assert.That(snapshot.Commands.Failures).IsEqualTo(0);
            await Assert.That(snapshot.Commands.TotalDurationMicroseconds).IsGreaterThan(0L);
            await Assert.That(provider.Commands).IsEqualTo(snapshot.Commands);
        });
    }

    [Test]
    [NotInParallel]
    public async Task Snapshot_CapturesTransactionMetricsForSQLite()
    {
        await WithTelemetryDatabase(async db =>
        {
            using (var transaction = db.Transaction())
            {
                await Assert.That(transaction.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('beta')")).IsEqualTo(1);
                transaction.Commit();
            }

            var snapshot = DataLinqMetrics.Snapshot();
            var provider = snapshot.Providers.Single();

            await Assert.That(snapshot.Transactions.Starts).IsEqualTo(1);
            await Assert.That(snapshot.Transactions.Commits).IsEqualTo(1);
            await Assert.That(snapshot.Transactions.Rollbacks).IsEqualTo(0);
            await Assert.That(snapshot.Transactions.Failures).IsEqualTo(0);
            await Assert.That(snapshot.Transactions.TotalDurationMicroseconds).IsGreaterThan(0L);
            await Assert.That(provider.Transactions).IsEqualTo(snapshot.Transactions);
            await Assert.That(snapshot.Commands.NonQueryExecutions).IsGreaterThanOrEqualTo(1);
        });
    }

    [Test]
    [NotInParallel]
    public async Task Activities_ExposeCommandAndTransactionMetadataForSQLite()
    {
        await WithTelemetryDatabase(async db =>
        {
            var stoppedActivities = new List<Activity>();

            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "DataLinq",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => stoppedActivities.Add(activity)
            };

            ActivitySource.AddActivityListener(listener);

            using (var transaction = db.Transaction())
            {
                await Assert.That(transaction.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('gamma')")).IsEqualTo(1);
                transaction.Commit();
            }

            var commandActivities = stoppedActivities
                .Where(x => x.OperationName == "datalinq.db.command")
                .ToArray();

            await Assert.That(commandActivities.Length).IsEqualTo(2);

            var setupCommandActivity = commandActivities.Single(x => Equals(x.GetTagItem("datalinq.transactional"), false));
            await Assert.That(setupCommandActivity.Kind).IsEqualTo(ActivityKind.Client);
            await Assert.That(setupCommandActivity.GetTagItem("db.system")).IsEqualTo("sqlite");
            await Assert.That(setupCommandActivity.GetTagItem("datalinq.command.kind")).IsEqualTo("non_query");

            var transactionalCommandActivity = commandActivities.Single(x => Equals(x.GetTagItem("datalinq.transactional"), true));
            await Assert.That(transactionalCommandActivity.Kind).IsEqualTo(ActivityKind.Client);
            await Assert.That(transactionalCommandActivity.GetTagItem("db.system")).IsEqualTo("sqlite");
            await Assert.That(transactionalCommandActivity.GetTagItem("datalinq.command.kind")).IsEqualTo("non_query");
            await Assert.That(transactionalCommandActivity.GetTagItem("datalinq.transaction.type")).IsEqualTo("read_write");

            var transactionActivity = stoppedActivities.Single(x => x.OperationName == "datalinq.db.transaction");
            await Assert.That(transactionActivity.Kind).IsEqualTo(ActivityKind.Client);
            await Assert.That(transactionActivity.GetTagItem("db.system")).IsEqualTo("sqlite");
            await Assert.That(transactionActivity.GetTagItem("datalinq.transaction.type")).IsEqualTo("read_write");
            await Assert.That(transactionActivity.GetTagItem("datalinq.outcome")).IsEqualTo("commit");
        });
    }

    [Test]
    [NotInParallel]
    public async Task Snapshot_CapturesCacheOccupancyAndCleanupForSQLite()
    {
        await WithTelemetryDatabase(async db =>
        {
            await Assert.That(db.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('delta')")).IsEqualTo(1);
            var cachedRow = db.Query().Rows.Single(x => x.Name == "delta");
            await Assert.That(cachedRow.Name).IsEqualTo("delta");

            var occupiedSnapshot = DataLinqMetrics.Snapshot();
            var occupiedProvider = occupiedSnapshot.Providers.Single();
            var occupiedTable = occupiedProvider.Tables.Single(x => x.TableName == "telemetryrows");

            await Assert.That(occupiedSnapshot.Occupancy.Rows).IsEqualTo(1L);
            await Assert.That(occupiedProvider.Occupancy.Rows).IsEqualTo(1L);
            await Assert.That(occupiedTable.Occupancy.Rows).IsEqualTo(1L);
            await Assert.That(occupiedTable.Occupancy.TransactionRows).IsEqualTo(0L);
            await Assert.That(occupiedTable.Occupancy.Bytes).IsGreaterThan(0L);

            DataLinqMetrics.Reset();
            db.Provider.State.Cache.ClearCache();

            var cleanupSnapshot = DataLinqMetrics.Snapshot();
            var cleanupProvider = cleanupSnapshot.Providers.Single();
            var cleanupTable = cleanupProvider.Tables.Single(x => x.TableName == "telemetryrows");

            await Assert.That(cleanupSnapshot.Occupancy.Rows).IsEqualTo(0L);
            await Assert.That(cleanupTable.Occupancy.Rows).IsEqualTo(0L);
            await Assert.That(cleanupSnapshot.Cleanup.Operations).IsGreaterThanOrEqualTo(1L);
            await Assert.That(cleanupSnapshot.Cleanup.RowsRemoved).IsGreaterThanOrEqualTo(1L);
            await Assert.That(cleanupTable.Cleanup.Operations).IsGreaterThanOrEqualTo(1L);
            await Assert.That(cleanupTable.Cleanup.RowsRemoved).IsGreaterThanOrEqualTo(1L);
        });
    }

    [Test]
    [NotInParallel]
    public async Task Meter_ExposesCacheOccupancyAndCleanupMetricsForSQLite()
    {
        await WithTelemetryDatabase(async db =>
        {
            var longMeasurements = new List<(string InstrumentName, long Value, Dictionary<string, object?> Tags)>();

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "DataLinq")
                    meterListener.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                longMeasurements.Add((instrument.Name, measurement, ToTagDictionary(tags)));
            });
            listener.Start();

            await Assert.That(db.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('epsilon')")).IsEqualTo(1);
            _ = db.Query().Rows.Single(x => x.Name == "epsilon");

            listener.RecordObservableInstruments();

            var rowGauge = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.cache.rows" &&
                HasTag(x.Tags, "datalinq.table", "telemetryrows"));
            await Assert.That(rowGauge.Value).IsEqualTo(1L);

            var bytesGauge = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.cache.bytes" &&
                HasTag(x.Tags, "datalinq.table", "telemetryrows"));
            await Assert.That(bytesGauge.Value).IsGreaterThan(0L);

            var transactionRowsGauge = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.cache.transaction.rows" &&
                HasTag(x.Tags, "datalinq.table", "telemetryrows"));
            await Assert.That(transactionRowsGauge.Value).IsEqualTo(0L);

            longMeasurements.Clear();
            db.Provider.State.Cache.ClearCache();

            var maintenanceOperation = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.cache.maintenance.operations" &&
                HasTag(x.Tags, "datalinq.table", "telemetryrows") &&
                HasTag(x.Tags, "datalinq.cache.operation", "clear"));
            await Assert.That(maintenanceOperation.Value).IsEqualTo(1L);

            var rowsRemoved = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.cache.rows.removed" &&
                HasTag(x.Tags, "datalinq.table", "telemetryrows") &&
                HasTag(x.Tags, "datalinq.cache.operation", "clear"));
            await Assert.That(rowsRemoved.Value).IsGreaterThanOrEqualTo(1L);
        });
    }

    private static async Task WithTelemetryDatabase(Func<SQLiteDatabase<SQLiteTelemetryDb>, Task> testAction)
    {
        DataLinqMetrics.Reset();
        using var db = CreateTelemetryDatabase();

        try
        {
            await testAction(db);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    private static SQLiteDatabase<SQLiteTelemetryDb> CreateTelemetryDatabase()
    {
        var databaseName = $"sqlite_telemetry_{Guid.NewGuid():N}";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseName,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ConnectionString;

        var db = new SQLiteDatabase<SQLiteTelemetryDb>(connectionString, databaseName);

        var createResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            db.Provider.Metadata,
            databaseName,
            connectionString,
            true);

        if (createResult.HasFailed)
        {
            db.Dispose();
            throw new InvalidOperationException(createResult.Failure.ToString());
        }

        return db;
    }

    private static bool HasTag(Dictionary<string, object?> tags, string key, object? value)
        => tags.TryGetValue(key, out var actual) && Equals(actual, value);

    private static Dictionary<string, object?> ToTagDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
            dictionary[tag.Key] = tag.Value;

        return dictionary;
    }
}

[Database("sqlitetelemetry")]
[UseCache]
public sealed partial class SQLiteTelemetryDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<SQLiteTelemetryRow> Rows { get; } = new(dataSource);
}

[Table("telemetryrows")]
public abstract partial class SQLiteTelemetryRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<SQLiteTelemetryRow, SQLiteTelemetryDb>(rowData, dataSource), ITableModel<SQLiteTelemetryDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("id")]
    public abstract int? Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Column("name")]
    public abstract string Name { get; }
}
