using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Cache;

public partial class TableCache
{
    // Called only by a captured query continuation, after its initial capability
    // validation/read/cleanup, with the outer enumeration's private owner.
    internal async Task<IReadOnlyList<IImmutableInstance>> LoadQueryRowsAsync(
        IReadOnlyList<DataLinqKey> orderedKeys, IDataSourceAccess source,
        IAsyncSqlReaderFactory factory, TransactionOperationGate.Step? owner,
        Action<IAsyncReadFailureEvidence> observingRead, CancellationToken token)
    {
        EnsureTransactionRowCache(source, owner);
        var neutral = ProviderKeyComponents.SupportsNeutralSourceRowLoading(Table, source.Provider.DatabaseType);
        var services = owner is null ? (IDataLinqSourceRowServices)source : ((DataSourceAccess)source).GetOwnedRowServices(owner);
        var found = new Dictionary<DataLinqKey, IImmutableInstance>();
        var missing = new List<DataLinqKey>();
        var distinct = new HashSet<DataLinqKey>();
        var hits = 0;
        foreach (var key in orderedKeys)
        {
            token.ThrowIfCancellationRequested();
            if (GetRowFromCache(key, source, out var row))
            {
                found.TryAdd(key, row!);
                hits++;
            }
            else if (distinct.Add(key)) missing.Add(key);
        }
        MetricsHandle.RecordRowCacheHits(hits);
        MetricsHandle.RecordRowCacheMisses(orderedKeys.Count - hits);

        // Bind all finite missing-key batches before the first batch suspends. The
        // provider factory was captured before even the original key query began.
        var loader = new DataSourceAccessSourceRowLoader(source, owner, factory);
        var reads = new List<(AsyncBufferedRead<SourceRowLoadResult>? Neutral, AsyncBufferedRead<IReadOnlyList<LoadedCanonicalRow>>? ProviderMatched)>();
        for (var offset = 0; offset < missing.Count; offset += 500)
        {
            token.ThrowIfCancellationRequested();
            var request = SourcePrimaryKeyRowRequest.Borrow(Table, missing, offset, Math.Min(500, missing.Count - offset));
            reads.Add(neutral ? (loader.CaptureAsyncRead(request), null) : (null, loader.CaptureProviderMatchedAsyncRead(request)));
        }
        foreach (var read in reads)
        {
            token.ThrowIfCancellationRequested();
            observingRead((IAsyncReadFailureEvidence?)read.Neutral ?? read.ProviderMatched!);
            var generation = CaptureReadGeneration();
            if (read.Neutral is { } neutralRead)
                await neutralRead.ExecuteAsync((result, _, cancellation) => Materialize(result.Rows, cancellation), token).ConfigureAwait(false);
            else
                await read.ProviderMatched!.ExecuteAsync((result, _, cancellation) => Materialize(result, cancellation), token).ConfigureAwait(false);

            bool Materialize(IReadOnlyList<LoadedCanonicalRow> rows, CancellationToken cancellation)
            {
                foreach (var loaded in rows)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var row = services.MaterializationServices.MaterializeAfterKnownCacheMiss(
                        loaded with { ReadGeneration = generation });
                    found.TryAdd(loaded.CanonicalProviderKey, row);
                    MetricsHandle.RecordDatabaseRowsLoaded(1);
                }
                return true;
            }
        }

        var result = new List<IImmutableInstance>(orderedKeys.Count);
        foreach (var key in orderedKeys)
        {
            token.ThrowIfCancellationRequested();
            if (found.TryGetValue(key, out var row)) result.Add(row);
        }
        return result;
    }
}
