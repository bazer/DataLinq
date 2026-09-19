using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public partial class ImmutableRelation<T, TKey>
    where T : IImmutableInstance where TKey : notnull
{
    internal async Task<ImmutableArray<T>> GetValuesAsyncCore(CancellationToken token = default)
        => (await GetSnapshotAsync(token).ConfigureAwait(false)).Values;

    internal async Task<FrozenDictionary<DataLinqKey, T>> GetInstancesAsyncCore(CancellationToken token = default)
        => (await GetSnapshotAsync(token, buildDictionary: true).ConfigureAwait(false)).GetInstances();

    private async Task<RelationSnapshot> GetSnapshotAsync(CancellationToken token, bool buildDictionary = false)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        // Admission precedes waiting, so same-transaction overlap never becomes
        // implicit queuing behind this relation's owner.
        var source = GetDataSource();
        var identity = ReadExecutionIdentity.Capture(source, ExecutionOperationKind.RelationLoad);
        using var read = DataSourceAccess.BeginRead(source, "load asynchronous relation values", operationKind: identity.Operation);
        var stage = ExecutionFailureStage.Validation;
        try
        {
            var table = GetTableCache(source);
            var prepared = table.PrepareRelationRowsAsyncCore(foreignKey, property, source, read?.Step);
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
        catch (Exception failure) { identity.ReportLocalFailure(failure, source, read?.Step, token, stage); read?.ReportFailure(failure); throw; }
    }
}
