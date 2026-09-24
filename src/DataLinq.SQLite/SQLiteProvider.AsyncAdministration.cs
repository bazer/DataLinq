using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public partial class SQLiteProvider<T> : IAsyncExistenceProbeSource, IAsyncJournalModeSource<SQLiteJournalMode>, IAsyncRootDisposal
{
    private OwnedRootDisposal? rootDisposal;
    private OwnedRootDisposal RootDisposal => LazyInitializer.EnsureInitialized(ref rootDisposal, () => new(() =>
        keepAliveConnection is null
            ? [RootCleanupStep.Resource(State.Dispose, State.DisposeAsyncCore, State.ValidateDisposal)]
            : [RootCleanupStep.Resource(State.Dispose, State.DisposeAsyncCore, State.ValidateDisposal),
                RootCleanupStep.Resource(keepAliveConnection.Dispose, ((IAsyncDisposable)keepAliveConnection).DisposeAsync)],
        TelemetryInstanceId));

    ValueTask IAsyncRootDisposal.DisposeAsyncCore() => RootDisposal.DisposeAsync();

    ExistenceProbePlan IAsyncExistenceProbeSource.CaptureExistenceProbe(ExistenceProbeRequest request)
    {
        RootDisposal.EnsureUsable();
        var options = new SqliteConnectionStringBuilder(ConnectionString);
        var memory = SQLiteConnectionStringFactory.IsInMemory(options);
        // SQLite's optional databaseName is intentionally ignored, as in its
        // synchronous probes: this provider already owns the effective identity.
        if (request.Kind is ExistenceProbeKind.FileOrServer or ExistenceProbeKind.Database)
            return ExistenceProbePlan.Local(RootDisposal.EnsureUsable, () => memory || File.Exists(options.DataSource));
        if (request.Kind != ExistenceProbeKind.Table) throw new ArgumentOutOfRangeException(nameof(request));
        if (string.IsNullOrEmpty(request.TableName)) throw new ArgumentNullException(nameof(request.TableName));
        if (!memory) options.Mode = SqliteOpenMode.ReadOnly;
        var sql = CapturedSql.Capture(new Sql("SELECT name FROM sqlite_master WHERE type='table' AND name=@tableName LIMIT 1")
            .AddParameter("@tableName", request.TableName));
        return ExistenceProbePlan.Reader(RootDisposal.EnsureUsable,
            () => new SQLiteAdministrativeSession(options.ConnectionString, dbAccess, sql, ExecutionOperationKind.ExistenceCheck));
    }

    IAsyncJournalModePlan IAsyncJournalModeSource<SQLiteJournalMode>.CaptureJournalMode(SQLiteJournalMode mode)
    {
        RootDisposal.EnsureUsable();
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var sql = CapturedSql.Capture(new Sql($"PRAGMA journal_mode = {mode}"));
        return new JournalPlan(this, sql);
    }

    private sealed class JournalPlan(SQLiteProvider<T> provider, CapturedSql sql) : IAsyncJournalModePlan
    {
        public void Validate() => provider.RootDisposal.EnsureUsable();
        public IAsyncJournalModeSession CreateSession() => new SQLiteAdministrativeSession(provider.ConnectionString,
            provider.dbAccess, sql, ExecutionOperationKind.ProviderConfiguration);
    }
}
