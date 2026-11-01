using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Testing;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public sealed partial class NativeAsyncMetadataTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task RuntimeLimitations_MissingReferentialCatalogRowIsOperationalFailure(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(RuntimeLimitations_MissingReferentialCatalogRowIsOperationalFailure),
            "CREATE TABLE parent(id INT PRIMARY KEY)",
            "CREATE TABLE child(id INT PRIMARY KEY, parent_id INT, CONSTRAINT fk_parent FOREIGN KEY(parent_id) REFERENCES parent(id))");
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        foreach (var asynchronous in new[] { false, true })
        {
            var settings = MetadataReadSettings.Runtime(null, null);
            var failures = new ExecutionFailures();
            if (asynchronous)
            {
                var plan = ((IAsyncProviderMetadataSource)provider).CaptureValidationMetadata(settings);
                var session = plan.CreateSession();
                var context = new MetadataReadContext(new MissingActionsAccess(session.Access), session.Commands, null, default);
                try
                {
                    await session.OpenAsync(default);
                    var result = await plan.ReadAsync(context, default);
                    await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
                    await Assert.That(failure.Message).Contains("no referential action catalog row");
                }
                finally { await context.CloseAsync(failures); await session.DisposeAsync(); }
            }
            else
            {
                var plan = ((ISyncProviderMetadataSource)provider).CaptureValidationMetadata(settings);
                var session = new MissingActionsSession(plan.CreateSession());
                var context = new SyncMetadataReadContext(session, null, provider.TelemetryInstanceId);
                try
                {
                    session.Open();
                    var result = plan.Read(context);
                    await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
                    await Assert.That(failure.Message).Contains("no referential action catalog row");
                }
                finally { context.Close(failures); session.Dispose(); }
            }
            await Assert.That(provider.ReadValidationMetadataCore().ValueOrException().TableModels.Length).IsEqualTo(2);
        }
    }

    // Controlled omission over a real catalog/session; ordinary reads below
    // still reach the native server and the one-slot pool must be reusable.
    private static void OmitReferentialActions(IDbCommand command)
    {
        if (command.CommandText.Contains("information_schema.REFERENTIAL_CONSTRAINTS", StringComparison.Ordinal))
            command.CommandText += " AND 1=0";
    }

    private sealed class MissingActionsAccess(IAsyncDatabaseAccess inner) : IAsyncDatabaseAccess
    {
        public void ValidateCommand(IDbCommand command, AsyncCommandKind kind) => inner.ValidateCommand(command, kind);
        public void ValidateReader(IDbCommand command) => inner.ValidateReader(command);
        public Task<IAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken token)
        { OmitReferentialActions(command); return inner.ExecuteReaderAsync(command, token); }
        public Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken token) => inner.ExecuteScalarAsync(command, token);
        public Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken token) => throw new InvalidOperationException("Metadata must remain observational.");
    }

    private sealed class MissingActionsSession(ISyncMetadataSession inner) : ISyncMetadataSession
    {
        public void Validate(int? timeout) => inner.Validate(timeout);
        public void Open() => inner.Open();
        public IDbCommand CreateCommand(CapturedSql sql) => inner.CreateCommand(sql);
        public IDataLinqDataReader ExecuteReader(IDbCommand command) { OmitReferentialActions(command); return inner.ExecuteReader(command); }
        public object? ExecuteScalar(IDbCommand command) => inner.ExecuteScalar(command);
        public void Dispose() => inner.Dispose();
    }
}
