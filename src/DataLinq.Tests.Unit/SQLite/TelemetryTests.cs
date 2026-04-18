using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.SQLite;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
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
    public async Task SnapshotAndMeter_CaptureMutationMetricsForSQLite()
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

            var inserted = db.Insert(new MutableSQLiteTelemetryRow
            {
                Name = "mutation-insert"
            });

            var mutable = inserted.Mutate();
            mutable.Name = "mutation-update";
            var updated = db.Update(mutable);
            db.Delete(updated);

            var snapshot = DataLinqMetrics.Snapshot();
            var provider = snapshot.Providers.Single();
            var table = provider.Tables.Single(x => x.TableName == "telemetryrows");

            await Assert.That(snapshot.Mutations.Inserts).IsEqualTo(1);
            await Assert.That(snapshot.Mutations.Updates).IsEqualTo(1);
            await Assert.That(snapshot.Mutations.Deletes).IsEqualTo(1);
            await Assert.That(snapshot.Mutations.Failures).IsEqualTo(0);
            await Assert.That(snapshot.Mutations.AffectedRows).IsGreaterThanOrEqualTo(3L);
            await Assert.That(snapshot.Mutations.TotalDurationMicroseconds).IsGreaterThan(0L);
            await Assert.That(provider.Mutations).IsEqualTo(snapshot.Mutations);
            await Assert.That(table.Mutations.Inserts).IsEqualTo(1);
            await Assert.That(table.Mutations.Updates).IsEqualTo(1);
            await Assert.That(table.Mutations.Deletes).IsEqualTo(1);

            var insertMeasurement = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.db.mutations" &&
                HasTag(x.Tags, "datalinq.table", "telemetryrows") &&
                HasTag(x.Tags, "datalinq.mutation.type", "insert"));
            await Assert.That(insertMeasurement.Value).IsEqualTo(1L);

            var updateMeasurement = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.db.mutations" &&
                HasTag(x.Tags, "datalinq.table", "telemetryrows") &&
                HasTag(x.Tags, "datalinq.mutation.type", "update"));
            await Assert.That(updateMeasurement.Value).IsEqualTo(1L);

            var deleteMeasurement = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.db.mutations" &&
                HasTag(x.Tags, "datalinq.table", "telemetryrows") &&
                HasTag(x.Tags, "datalinq.mutation.type", "delete"));
            await Assert.That(deleteMeasurement.Value).IsEqualTo(1L);

            var affectedRows = longMeasurements
                .Where(x =>
                    x.InstrumentName == "datalinq.db.mutation.affected_rows" &&
                    HasTag(x.Tags, "datalinq.table", "telemetryrows"))
                .Sum(x => x.Value);
            await Assert.That(affectedRows).IsGreaterThanOrEqualTo(3L);
        });
    }

    [Test]
    [NotInParallel]
    public async Task Activities_ExposeQueryMetadataForSQLite()
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

            await Assert.That(db.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('theta')")).IsEqualTo(1);

            var row = db.Query().Rows.Single(x => x.Name == "theta");
            await Assert.That(row.Name).IsEqualTo("theta");

            var queryActivity = stoppedActivities.Single(x => x.OperationName == "datalinq.query");
            await Assert.That(queryActivity.Kind).IsEqualTo(ActivityKind.Internal);
            await Assert.That(queryActivity.GetTagItem("db.system")).IsEqualTo("sqlite");
            await Assert.That(queryActivity.GetTagItem("datalinq.table")).IsEqualTo("telemetryrows");
            await Assert.That(queryActivity.GetTagItem("datalinq.query.kind")).IsEqualTo("entity");
            await Assert.That(queryActivity.GetTagItem("datalinq.outcome")).IsEqualTo("success");
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

    [Test]
    [NotInParallel]
    public async Task Meter_ExposesRowCacheRelationAndNotificationMetricsForSQLiteEmployees()
    {
        DataLinqMetrics.Reset();
        using var databaseScope = EmployeesTestDatabase.CreateIsolatedBogus(
            TestProviderMatrix.SQLiteInMemory,
            "telemetry_meter",
            employeeCount: 50);

        try
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

            var employeeNumber = databaseScope.Database.Query().Employees
                .OrderBy(x => x.emp_no)
                .Select(x => x.emp_no!.Value)
                .First();

            var firstEmployee = databaseScope.Database.Query().Employees.Single(x => x.emp_no == employeeNumber);
            var secondEmployee = databaseScope.Database.Query().Employees.Single(x => x.emp_no == employeeNumber);

            await Assert.That(firstEmployee.emp_no).IsNotNull();
            await Assert.That(secondEmployee.emp_no).IsNotNull();

            _ = firstEmployee.dept_emp.First().departments.Name;
            _ = firstEmployee.dept_emp.First().departments.Name;

            var employeesTable = databaseScope.Database.Provider.Metadata.TableModels
                .Single(x => x.Table.DbName == "employees")
                .Table;
            var subscriber = new TestCacheNotification();
            databaseScope.Database.Provider.GetTableCache(employeesTable).SubscribeToChanges(subscriber);

            listener.RecordObservableInstruments();

            await Assert.That(longMeasurements.Any(x =>
                x.InstrumentName == "datalinq.cache.rows.access" &&
                HasTag(x.Tags, "datalinq.table", "employees") &&
                HasTag(x.Tags, "datalinq.cache.result", "miss") &&
                x.Value > 0)).IsTrue();

            await Assert.That(longMeasurements.Any(x =>
                x.InstrumentName == "datalinq.cache.rows.access" &&
                HasTag(x.Tags, "datalinq.table", "employees") &&
                HasTag(x.Tags, "datalinq.cache.result", "hit") &&
                x.Value > 0)).IsTrue();

            await Assert.That(longMeasurements.Any(x =>
                x.InstrumentName == "datalinq.cache.rows.access" &&
                HasTag(x.Tags, "datalinq.table", "employees") &&
                HasTag(x.Tags, "datalinq.cache.result", "store") &&
                x.Value > 0)).IsTrue();

            await Assert.That(longMeasurements.Any(x =>
                x.InstrumentName == "datalinq.cache.relations" &&
                HasTag(x.Tags, "datalinq.cache.result", "load") &&
                x.Value > 0)).IsTrue();

            await Assert.That(longMeasurements.Any(x =>
                x.InstrumentName == "datalinq.cache.relations" &&
                HasTag(x.Tags, "datalinq.cache.result", "hit") &&
                x.Value > 0)).IsTrue();

            var queueDepthGauge = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.cache.notifications.queue_depth" &&
                HasTag(x.Tags, "datalinq.table", "employees"));
            await Assert.That(queueDepthGauge.Value).IsEqualTo(1L);

            var peakQueueDepthGauge = longMeasurements.Single(x =>
                x.InstrumentName == "datalinq.cache.notifications.peak_queue_depth" &&
                HasTag(x.Tags, "datalinq.table", "employees"));
            await Assert.That(peakQueueDepthGauge.Value).IsEqualTo(1L);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
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

internal sealed class TestCacheNotification : ICacheNotification
{
    public void Clear()
    {
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
