using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class NativeAsyncTelemetryParityTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NativeWorkloadPreservesSyncMetricsAndActivitySemantics(TestProviderDescriptor descriptor)
    {
        var sync = await Observe(descriptor, asynchronous: false);
        var asyncResult = await Observe(descriptor, asynchronous: true);
        await Assert.That(asyncResult.Commands).IsEqualTo(sync.Commands);
        await Assert.That(asyncResult.Queries).IsEqualTo(sync.Queries);
        await Assert.That(asyncResult.Transactions).IsEqualTo(sync.Transactions);
        await Assert.That(asyncResult.Mutations).IsEqualTo(sync.Mutations);
        await Assert.That(asyncResult.RowCache).IsEqualTo(sync.RowCache);
        await Assert.That(asyncResult.Activities).IsEquivalentTo(sync.Activities);
        await Assert.That(asyncResult.Measurements).IsEquivalentTo(sync.Measurements);
    }

    private static async Task<Observation> Observe(TestProviderDescriptor descriptor, bool asynchronous)
    {
        using var scope = new NativeAsyncTestDatabase<CachePublicationDb>(descriptor, $"telemetry_{asynchronous}");
        var database = scope.Database;
        database.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO publication_rows VALUES (1, 'one'), (2, 'two')");
        database.Cache.Clear();
        var before = Snapshot();
        var activities = new ConcurrentQueue<Activity>();
        var measurements = new ConcurrentQueue<string>();
        // Do not inherit a runner trace shared by other tests.
        using var parent = new Activity("native-telemetry-parity").SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded).Start();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DataLinq",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == parent.TraceId ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == parent.TraceId) activities.Enqueue(activity);
            }
        };
        ActivitySource.AddActivityListener(activityListener);
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "DataLinq" && InstrumentNames.Contains(instrument.Name))
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Record(instrument.Name, value.ToString(CultureInfo.InvariantCulture), tags));
        // Durations are expected to differ. Compare histogram emissions and dimensions,
        // not elapsed time. All counter values and semantic tags remain in the comparison.
        meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            Record(instrument.Name, "duration", tags));
        meterListener.Start();

        var rows = await ReadRows();
        await Assert.That(rows.Select(row => row.Name).ToArray()).IsEquivalentTo(new[] { "one", "two" });
        var key = database.PrepareQuery(1, id => database.Query().Rows.Single(row => row.Id == id));
        var keyed = asynchronous ? await key.ExecuteAsyncCore(database, 1) : key.Execute(database, 1);
        await Assert.That(keyed).IsSameReferenceAs(rows[0]);
        var count = database.PrepareQuery(0, _ => database.Query().Rows.Count());
        await Assert.That(asynchronous ? await count.ExecuteAsyncCore(database, 0) : count.Execute(database, 0)).IsEqualTo(2);

        var mutable = keyed.Mutate();
        mutable.Name = "committed";
        CachePublicationRow updated;
        var commit = database.Transaction();
        try
        {
            updated = asynchronous ? await commit.UpdateAsyncCore(mutable) : commit.Update(mutable);
            if (asynchronous) await commit.CommitAsyncCore(); else commit.Commit();
        }
        finally { if (asynchronous) await commit.DisposeAsyncCore(); else commit.Dispose(); }
        var discarded = updated.Mutate();
        discarded.Name = "discarded";
        var rollback = database.Transaction();
        try
        {
            if (asynchronous)
            {
                await rollback.UpdateAsyncCore(discarded);
                await rollback.RollbackAsyncCore();
            }
            else
            {
                rollback.Update(discarded);
                rollback.Rollback();
            }
        }
        finally { if (asynchronous) await rollback.DisposeAsyncCore(); else rollback.Dispose(); }
        var newRow = new MutableCachePublicationRow { Id = 3, Name = "three" };
        var inserted = asynchronous ? await database.InsertAsyncCore(newRow) : database.Insert(newRow);
        if (asynchronous) await database.DeleteAsyncCore(inserted); else database.Delete(inserted);
        var final = await ReadRows();
        await Assert.That(final.Select(row => (row.Id, row.Name)).ToArray())
            .IsEquivalentTo(new[] { (1, "committed"), (2, "two") });

        // An actual native SQL error must appear once in command metrics and activities.
        DbException? failure = null;
        try
        {
            const string invalidSql = "SELECT missing_column FROM publication_rows";
            if (asynchronous) await database.Provider.DatabaseAccess.ExecuteScalarAsyncCore(invalidSql);
            else database.Provider.DatabaseAccess.ExecuteScalar(invalidSql);
        }
        catch (DbException exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(Activity.Current).IsSameReferenceAs(parent);
        var after = Snapshot();
        var result = new Observation(
            new(after.Commands.ReaderExecutions - before.Commands.ReaderExecutions,
                after.Commands.ScalarExecutions - before.Commands.ScalarExecutions,
                after.Commands.NonQueryExecutions - before.Commands.NonQueryExecutions,
                after.Commands.Failures - before.Commands.Failures, 0),
            new(after.Queries.EntityExecutions - before.Queries.EntityExecutions,
                after.Queries.ScalarExecutions - before.Queries.ScalarExecutions),
            new(after.Transactions.Starts - before.Transactions.Starts,
                after.Transactions.Commits - before.Transactions.Commits,
                after.Transactions.Rollbacks - before.Transactions.Rollbacks,
                after.Transactions.Failures - before.Transactions.Failures, 0),
            new(after.Mutations.Inserts - before.Mutations.Inserts,
                after.Mutations.Updates - before.Mutations.Updates,
                after.Mutations.Deletes - before.Mutations.Deletes,
                after.Mutations.Failures - before.Mutations.Failures,
                after.Mutations.AffectedRows - before.Mutations.AffectedRows, 0),
            new(after.RowCache.Hits - before.RowCache.Hits,
                after.RowCache.Misses - before.RowCache.Misses,
                after.RowCache.DatabaseRowsLoaded - before.RowCache.DatabaseRowsLoaded,
                after.RowCache.Materializations - before.RowCache.Materializations,
                after.RowCache.Stores - before.RowCache.Stores),
            activities.Select(activity => $"{activity.OperationName}|{activity.Kind}|{activity.Status}|{Tags(activity.TagObjects)}").ToArray(),
            measurements.ToArray());

        // Nonempty controls prevent two absent/broken instrumentation paths from passing parity.
        await Assert.That(result.Commands.ReaderExecutions).IsGreaterThan(0L);
        await Assert.That(result.Commands.ScalarExecutions).IsGreaterThan(0L);
        await Assert.That(result.Commands.NonQueryExecutions).IsGreaterThan(0L);
        await Assert.That(result.Commands.Failures).IsEqualTo(1L);
        await Assert.That(result.Queries.EntityExecutions).IsGreaterThan(0L);
        await Assert.That(result.Queries.ScalarExecutions).IsGreaterThan(0L);
        await Assert.That(result.Transactions).IsEqualTo(new TransactionMetricsSnapshot(4, 3, 1, 0, 0));
        await Assert.That(result.Mutations).IsEqualTo(new MutationMetricsSnapshot(1, 2, 1, 0, 4, 0));
        await Assert.That(activities.Count(activity => activity.Status == ActivityStatusCode.Error)).IsEqualTo(1);
        foreach (var operation in new[] { "datalinq.db.command", "datalinq.query", "datalinq.db.transaction", "datalinq.db.mutation" })
            await Assert.That(activities.Any(activity => activity.OperationName == operation)).IsTrue();
        foreach (var instrument in InstrumentNames)
            await Assert.That(measurements.Any(value => value.StartsWith(instrument + "|", StringComparison.Ordinal))).IsTrue();
        return result;

        DataLinqProviderMetricsSnapshot Snapshot() => DataLinqMetrics.Snapshot().Providers
            .Single(provider => provider.ProviderInstanceId == database.Provider.TelemetryInstanceId);

        async Task<CachePublicationRow[]> ReadRows()
        {
            var query = database.Query().Rows.Where(row => row.Id > 0).OrderBy(row => row.Id);
            if (!asynchronous) return query.ToArray();
            var output = new List<CachePublicationRow>();
            await foreach (var row in ((ExpressionQueryPlanProvider)query.Provider)
                .ExecuteEnumerableAsyncCore<CachePublicationRow>(query.Expression)) output.Add(row);
            return output.ToArray();
        }

        void Record(string instrument, string value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            // Isolated schema identity excludes other parallel tests without resetting global metrics.
            foreach (var tag in tags)
                if (tag.Key == "db.namespace" && Equals(tag.Value, database.Provider.DatabaseName))
                {
                    measurements.Enqueue($"{instrument}|{value}|{Tags(tags.ToArray())}");
                    return;
                }
        }
    }

    private static string Tags(IEnumerable<KeyValuePair<string, object?>> tags) => string.Join(";", tags
        .Where(tag => tag.Key != "db.namespace") // Each workload has its own isolated database name.
        .OrderBy(tag => tag.Key, StringComparer.Ordinal)
        .Select(tag => $"{tag.Key}={Convert.ToString(tag.Value, CultureInfo.InvariantCulture)}"));

    private static readonly HashSet<string> InstrumentNames =
    [
        "datalinq.db.commands", "datalinq.queries", "datalinq.db.transactions.started",
        "datalinq.db.transactions.completed", "datalinq.db.mutations", "datalinq.db.mutation.affected_rows",
        "datalinq.db.command.duration", "datalinq.query.duration", "datalinq.db.transaction.duration",
        "datalinq.db.mutation.duration"
    ];

    private sealed record Observation(CommandMetricsSnapshot Commands, QueryMetricsSnapshot Queries,
        TransactionMetricsSnapshot Transactions, MutationMetricsSnapshot Mutations, RowCacheMetricsSnapshot RowCache,
        string[] Activities, string[] Measurements);
}
