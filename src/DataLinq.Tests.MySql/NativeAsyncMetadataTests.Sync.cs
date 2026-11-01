using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;
using DataLinq.Testing;
using MySqlConnector;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public sealed partial class NativeAsyncMetadataTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task SyncMetadata_MatchesAsyncMetadataAndWarnings(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(SyncMetadata_MatchesAsyncMetadataAndWarnings), Schema);
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        var warnings = new List<string>();
        var actual = provider.ReadValidationMetadataCore(log: warnings.Add).ValueOrException();
        var syncWarnings = warnings.ToArray();
        warnings.Clear();
        var expected = (await provider.ReadValidationMetadataAsyncCore(log: warnings.Add)).ValueOrException();
        await Assert.That(MetadataEquivalenceDigest.CreateText(actual)).IsEqualTo(MetadataEquivalenceDigest.CreateText(expected));
        await Assert.That(syncWarnings.SequenceEqual(warnings)).IsTrue();
        await Assert.That(actual.IsFrozen).IsTrue();
        // A second read through the one-connection pool proves the first read returned it.
        await Assert.That(provider.ReadValidationMetadataCore().ValueOrException().TableModels.Length).IsEqualTo(3);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task SyncMetadata_UsesEffectiveSchemaAndDistinguishesEmptyFreshAndMissing(TestProviderDescriptor descriptor)
    {
        using var configured = ServerSchemaDatabase.Create(descriptor, nameof(SyncMetadata_UsesEffectiveSchemaAndDistinguishesEmptyFreshAndMissing), "CREATE TABLE wrong_database(id INT PRIMARY KEY)");
        using var effective = ServerSchemaDatabase.Create(descriptor, nameof(SyncMetadata_UsesEffectiveSchemaAndDistinguishesEmptyFreshAndMissing) + "_effective");
        using var provider = Provider(descriptor, configured.Connection.ConnectionString, effective.Connection.DataSourceName);
        var empty = provider.ReadValidationMetadataCore().ValueOrException();
        await Assert.That(empty.TableModels).IsEmpty();
        await Assert.That(empty.DbName).IsEqualTo(effective.Connection.DataSourceName);
        effective.ExecuteNonQuery("CREATE TABLE right_database(id INT PRIMARY KEY)");
        var fresh = provider.ReadValidationMetadataCore().ValueOrException();
        await Assert.That(fresh.TableModels.Single().Table.DbName).IsEqualTo("right_database");
        await Assert.That(empty.TableModels).IsEmpty();
        effective.ExecuteNonQuery($"DROP DATABASE {SqlIdentifier.Quote(effective.Connection.DataSourceName, "`")}");
        await Assert.That(provider.ReadValidationMetadataCore().TryUnwrap(out _, out _)).IsFalse();
        await Assert.That(provider.DatabaseExists()).IsFalse();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task SyncMetadata_EveryCommandUsesNativeDefaultOrExplicitTimeout(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(SyncMetadata_EveryCommandUsesNativeDefaultOrExplicitTimeout), Schema);
        using var provider = Provider(descriptor, new MySqlConnectionStringBuilder(schema.Connection.ConnectionString) { DefaultCommandTimeout = 7 }.ConnectionString, schema.Connection.DataSourceName);
        foreach (var timeout in new TimeSpan?[] { null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1250) })
        {
            var settings = MetadataReadSettings.Runtime(timeout, null);
            var plan = ((ISyncProviderMetadataSource)provider).CaptureValidationMetadata(settings);
            using var session = new RecordingSyncSession(plan.CreateSession());
            var context = new SyncMetadataReadContext(session, settings.CommandTimeoutSeconds, provider.TelemetryInstanceId);
            var failures = new ExecutionFailures();
            try { session.Open(); _ = plan.Read(context).ValueOrException(); }
            finally { context.Close(failures); session.Dispose(); }
            failures.ThrowIfAny();
            await Assert.That(session.Commands.Count).IsGreaterThanOrEqualTo(10);
            await Assert.That(session.Commands.Select(command => command.Connection).Distinct().Count()).IsEqualTo(1);
            foreach (var command in session.Commands)
            {
                await Assert.That(command.Timeout).IsEqualTo(timeout is null ? 7 : timeout == TimeSpan.Zero ? 0 : 2);
                await Assert.That(command.Connection.State).IsEqualTo(ConnectionState.Closed);
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task SyncMetadata_NativeTimeoutSettlesScalarAndReaderAndReturnsPoolConnection(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(SyncMetadata_NativeTimeoutSettlesScalarAndReaderAndReturnsPoolConnection),
            "CREATE TABLE blocked_row(id INT PRIMARY KEY, value INT); INSERT INTO blocked_row VALUES(1,1)");
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        await using var blocker = new MySqlConnection(schema.Connection.ConnectionString);
        await blocker.OpenAsync();
        await using var locked = await blocker.BeginTransactionAsync();
        await using (var update = new MySqlCommand("UPDATE blocked_row SET value=2 WHERE id=1", blocker, locked)) await update.ExecuteNonQueryAsync();
        foreach (var scalar in new[] { true, false })
        {
            var settings = MetadataReadSettings.Runtime(TimeSpan.FromSeconds(1), null);
            using var session = ((ISyncProviderMetadataSource)provider).CaptureValidationMetadata(settings).CreateSession();
            var context = new SyncMetadataReadContext(session, settings.CommandTimeoutSeconds, provider.TelemetryInstanceId);
            var failures = new ExecutionFailures();
            try
            {
                session.Open();
                var sql = new Sql("SELECT value FROM blocked_row WHERE id=1 FOR UPDATE");
                var failure = await Assert.That(() =>
                {
                    if (scalar) context.ExecuteScalar(sql);
                    else context.Read(sql, row => row.GetInt32(0));
                }).Throws<MySqlException>();
                await Assert.That(failure!.ErrorCode).IsEqualTo(MySqlErrorCode.CommandTimeoutExpired);
                await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
            }
            finally { context.Close(failures); session.Dispose(); }
            await Assert.That(failures.Primary).IsNotNull();
            await Assert.That(provider.ReadValidationMetadataCore().ValueOrException().TableModels.Length).IsEqualTo(1);
        }
        await locked.RollbackAsync();
    }

    private sealed class RecordingSyncSession(ISyncMetadataSession inner) : ISyncMetadataSession
    {
        private bool disposed;
        internal readonly List<(int Timeout, IDbConnection Connection)> Commands = [];
        private void Record(IDbCommand command) => Commands.Add((command.CommandTimeout, command.Connection!));
        public void Validate(int? seconds) => inner.Validate(seconds);
        public void Open() => inner.Open();
        public IDbCommand CreateCommand(CapturedSql sql) => inner.CreateCommand(sql);
        public IDataLinqDataReader ExecuteReader(IDbCommand command) { var reader = inner.ExecuteReader(command); Record(command); return reader; }
        public object? ExecuteScalar(IDbCommand command) { var result = inner.ExecuteScalar(command); Record(command); return result; }
        public void Dispose() { if (disposed) return; disposed = true; inner.Dispose(); }
    }
}
