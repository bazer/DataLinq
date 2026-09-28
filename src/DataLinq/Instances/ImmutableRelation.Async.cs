using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public partial class ImmutableRelation<T, TKey>
    where T : IImmutableInstance where TKey : notnull
{
    internal async Task<ImmutableArray<T>> GetValuesAsyncCore(CancellationToken token = default)
        => (await GetSnapshotAsync(token).ConfigureAwait(false)).Values;

    internal async Task<FrozenDictionary<DataLinqKey, T>> GetInstancesAsyncCore(CancellationToken token = default)
        => (await GetSnapshotAsync(token, buildDictionary: true).ConfigureAwait(false)).GetInstances();

    private async Task<RelationSnapshot> GetSnapshotAsync(CancellationToken token, bool buildDictionary = false,
        IDataSourceAccess? capturedSource = null, DataLinqKey? capturedKey = null,
        TransactionOperationGate.Step? owner = null)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        // Admission precedes waiting, so same-transaction overlap never becomes
        // implicit queuing behind this relation's owner.
        var source = capturedSource ?? GetDataSource();
        var identity = ReadExecutionIdentity.Capture(source, ExecutionOperationKind.RelationLoad);
        using var read = DataSourceAccess.BeginRead(source, "load asynchronous relation values", owner, operationKind: identity.Operation);
        var step = owner ?? read?.Step;
        var stage = ExecutionFailureStage.Validation;
        try
        {
            var table = GetTableCache(source);
            var key = capturedKey ?? ProviderKeyComponents.ToDataLinqKey(foreignKey);
            var prepared = table.PrepareRelationRowsAsyncCore(key, property, source, step);
            stage = ExecutionFailureStage.Materialization;
            token.ThrowIfCancellationRequested();
            var current = Volatile.Read(ref snapshot);
            if (current is not null && ReferenceEquals(current.Source, source))
            {
                table.MetricsHandle.RecordRelationCollectionCacheHit();
                if (buildDictionary) _ = current.GetInstances();
                return current;
            }
            await loadSlot.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                current = Volatile.Read(ref snapshot);
                if (current is not null && ReferenceEquals(current.Source, source))
                {
                    table.MetricsHandle.RecordRelationCollectionCacheHit();
                    if (buildDictionary) _ = current.GetInstances();
                    return current;
                }
                object generation;
                lock (loadLock) generation = clearGeneration;
                var readGeneration = table.CaptureReadGeneration();
                return await table.ExecuteRelationRowsAsyncCore(prepared, rows =>
                {
                    token.ThrowIfCancellationRequested();
                    var values = ToImmutableRelationValues(rows);
                    token.ThrowIfCancellationRequested();
                    return PublishSnapshot(source, table, values, generation, readGeneration, prepared.RelationKey, buildDictionary);
                }, token).ConfigureAwait(false);
            }
            finally { loadSlot.Release(); }
        }
        catch (Exception failure) { identity.ReportLocalFailure(failure, source, step, token, stage); read?.ReportFailure(failure); throw; }
    }

    // Reuse the reader enumerator's admission, cancellation, call guard and helper
    // drain lifecycle even when the complete relation is already cached.
    private sealed class RelationSnapshotRead(ImmutableRelation<T, TKey> relation, IDataSourceAccess source, DataLinqKey key)
        : IAsyncReaderSource, IAsyncReaderContinuation<T>
    {
        public bool RequiresInitialReader => false;
        public void Validate() { }
        public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void AddRow(IAsyncDataReader reader) => throw new NotSupportedException();
        public async Task<IReadOnlyList<T>> CompleteAsync(TransactionOperationGate.Step? owner, CancellationToken token) =>
            (await relation.GetSnapshotAsync(token, capturedSource: source, capturedKey: key, owner: owner).ConfigureAwait(false)).Values;
        // Load failures retain their child report. Once loading has succeeded,
        // cancellation between buffered rows has performed no further statement.
        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) =>
            new(Effects: ExecutionEffects.NoStatement, Integrity: TransactionIntegrity.Confirmed, RollbackAvailable: true);
    }
}
