using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using MySqlConnector;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public sealed class NativeAsyncMetadataFallbackTests
{
    private const string CreateView = "CREATE VIEW `header``view` AS SELECT id, label FROM fallback_values";
    private const string DropView = "DROP VIEW `header``view`";

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ForcedViewFallbackUsesNativeSqlAndPreservesFailureAndCancellation(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(ForcedViewFallbackUsesNativeSqlAndPreservesFailureAndCancellation),
            "CREATE TABLE fallback_values (id INT PRIMARY KEY, label VARCHAR(40) NOT NULL)", CreateView);
        var connectionString = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString)
            { Pooling = true, MaximumPoolSize = 1, ConnectionTimeout = 5 }.ConnectionString;
        using SqlProvider<EmployeesDb> provider = descriptor.DatabaseType == DatabaseType.MySQL
            ? new MySqlProvider<EmployeesDb>(connectionString, schema.Connection.DataSourceName)
            : new MariaDBProvider<EmployeesDb>(connectionString, schema.Connection.DataSourceName);
        var expected = (await provider.ReadValidationMetadataAsyncCore()).ValueOrException();
        foreach (var mode in new[] { "success", "error", "cancel" })
        {
            using var cancellation = new CancellationTokenSource();
            var settings = MetadataReadSettings.Runtime(TimeSpan.FromMilliseconds(1250), null);
            var plan = ((IAsyncProviderMetadataSource)provider).CaptureValidationMetadata(settings);
            plan.Validate();
            var session = plan.CreateSession();
            var access = new ForcedFallbackAccess(session.Access, async () =>
            {
                if (mode == "cancel") cancellation.Cancel();
                if (mode != "error") return;
                // Remove the actual view after catalog enumeration but before
                // SHOW CREATE VIEW. Its failure must come from the real server.
                await using var admin = new MySqlConnection(schema.Connection.ConnectionString);
                await admin.OpenAsync();
                await using var drop = new MySqlCommand(DropView, admin);
                await drop.ExecuteNonQueryAsync();
            });
            var context = new MetadataReadContext(access, session.Commands, settings.CommandTimeoutSeconds, cancellation.Token,
                new(ExecutionOperationKind.MetadataRead, provider.TelemetryInstanceId));
            var failures = new ExecutionFailures();
            Exception? observed = null;
            try
            {
                await session.OpenAsync(default);
                if (mode == "success")
                {
                    var actual = (await plan.ReadAsync(context, cancellation.Token)).ValueOrException();
                    await Assert.That(actual.IsFrozen).IsTrue();
                    await Assert.That(MetadataEquivalenceDigest.CreateText(actual)).IsEqualTo(MetadataEquivalenceDigest.CreateText(expected));
                    await Assert.That(((ViewDefinition)actual.TableModels.Single(model => model.Table.Type == TableType.View).Table).Definition)
                        .IsEqualTo(((ViewDefinition)expected.TableModels.Single(model => model.Table.Type == TableType.View).Table).Definition);
                }
                else
                {
                    if (mode == "error")
                        observed = await Assert.That(async () => { await plan.ReadAsync(context, cancellation.Token); }).Throws<MySqlException>();
                    else
                        observed = await Assert.That(async () => { await plan.ReadAsync(context, cancellation.Token); }).Throws<OperationCanceledException>();
                    await Assert.That(observed).IsSameReferenceAs(access.FallbackFailure);
                    var diagnostic = ExecutionFailureContexts.Get(observed!)!;
                    await Assert.That(diagnostic.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
                    await Assert.That(diagnostic.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
                    await Assert.That(diagnostic.Cause).IsEqualTo(mode == "error" ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Cancellation);
                    if (observed is OperationCanceledException canceled)
                        await Assert.That(canceled.CancellationToken).IsEqualTo(cancellation.Token);
                }
                await Assert.That(access.MaskedCatalogReads).IsEqualTo(1);
                await Assert.That(access.FallbackCalls).IsEqualTo(1);
                await Assert.That(access.FallbackSql).IsEqualTo($"SHOW CREATE VIEW `{schema.Connection.DataSourceName.Replace("`", "``")}`.`header``view`");
                await Assert.That(access.FallbackTimeout).IsEqualTo(2);
            }
            finally
            {
                try { await context.CloseAsync(failures); }
                finally { await session.DisposeAsync(); }
            }
            await Assert.That(failures.Primary).IsSameReferenceAs(observed);
            await Assert.That(failures.HasCleanupFailure).IsFalse();
            if (mode == "error") await provider.DatabaseAccess.ExecuteNonQueryAsyncCore(CreateView);
            // Reuses the same one-slot pool only after the owned session settles.
            var fresh = (await provider.ReadValidationMetadataAsyncCore()).ValueOrException();
            await Assert.That(MetadataEquivalenceDigest.CreateText(fresh)).IsEqualTo(MetadataEquivalenceDigest.CreateText(expected));
        }
    }

    // Force only the absent catalog value: all reader/command work, including
    // the unchanged SHOW CREATE VIEW fallback, still runs on the native driver.
    // This is branch coverage, not evidence that a real permission layout causes
    // information_schema to omit a definition while allowing SHOW CREATE VIEW.
    private sealed class ForcedFallbackAccess(IAsyncDatabaseAccess inner, Func<Task> beforeFallback)
        : IAsyncDatabaseAccess, IAsyncReadFailureEvidence
    {
        internal int MaskedCatalogReads;
        internal int FallbackCalls;
        internal string? FallbackSql;
        internal int FallbackTimeout;
        internal Exception? FallbackFailure;
        public void ValidateCommand(IDbCommand command, AsyncCommandKind kind) => inner.ValidateCommand(command, kind);
        public void ValidateReader(IDbCommand command) => inner.ValidateReader(command);
        public async Task<IAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken token)
        {
            const string catalogPrefix = "SELECT VIEW_DEFINITION FROM information_schema.VIEWS ";
            if (command.CommandText.StartsWith(catalogPrefix, StringComparison.Ordinal))
            {
                MaskedCatalogReads++;
                command.CommandText = "SELECT NULL AS VIEW_DEFINITION FROM information_schema.VIEWS " + command.CommandText[catalogPrefix.Length..];
            }
            if (!command.CommandText.StartsWith("SHOW CREATE VIEW ", StringComparison.Ordinal))
                return await inner.ExecuteReaderAsync(command, token);
            FallbackCalls++;
            FallbackSql = command.CommandText;
            FallbackTimeout = command.CommandTimeout;
            await beforeFallback();
            try { return await inner.ExecuteReaderAsync(command, token); }
            catch (Exception failure) { FallbackFailure = failure; throw; }
        }
        public Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken token) => inner.ExecuteScalarAsync(command, token);
        public Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken token) => throw new InvalidOperationException("Metadata must remain observational.");
        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) =>
            (inner as IAsyncReadFailureEvidence)?.GetReadFailureEvidence(failure) ?? new();
    }
}
