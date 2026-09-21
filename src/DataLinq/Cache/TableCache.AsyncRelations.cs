using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Cache;

public partial class TableCache
{
    // Captured before waiting for a relation's load slot. Binding is I/O-free;
    // this plan carries no ambient permission or live reader.
    internal sealed class PreparedRelationRows(
        TableCache cache, IDataSourceAccess source, ColumnIndex index, DataLinqKey key,
        TransactionOperationGate.Step? owner, IAsyncSqlReaderFactory? factory,
        AsyncBufferedRead<CanonicalProviderValueRow?>? single = null,
        AsyncBufferedRead<SourceIndexRowLoadResult>? neutral = null,
        AsyncBufferedRead<IReadOnlyList<LoadedCanonicalRow>>? matched = null,
        AsyncBufferedRead<IReadOnlyList<CanonicalProviderValueRow>>? keyless = null)
    {
        internal TableCache Cache { get; } = cache;
        internal IDataSourceAccess Source { get; } = source;
        internal ColumnIndex Index { get; } = index;
        internal DataLinqKey Key { get; } = key;
        internal RelationCacheKey? RelationKey => Key.IsNull ? null : new(Index, Key);
        internal TransactionOperationGate.Step? Owner { get; } = owner;
        internal IAsyncSqlReaderFactory? Factory { get; } = factory;
        internal AsyncBufferedRead<CanonicalProviderValueRow?>? Single { get; } = single;
        internal AsyncBufferedRead<SourceIndexRowLoadResult>? Neutral { get; } = neutral;
        internal AsyncBufferedRead<IReadOnlyList<LoadedCanonicalRow>>? Matched { get; } = matched;
        internal AsyncBufferedRead<IReadOnlyList<CanonicalProviderValueRow>>? Keyless { get; } = keyless;
    }

    internal PreparedRelationRows PrepareRelationRowsAsyncCore<TKey>(TKey foreignKey,
        RelationProperty property, IDataSourceAccess source, TransactionOperationGate.Step? owner)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        DataSourceAccess.EnsureReadAllowed(source, "capture asynchronous relation rows", owner, ExecutionOperationKind.RelationLoad);
        var index = property.RelationPart.GetOtherSide().ColumnIndex;
        if (!ReferenceEquals(index.Table, Table)) throw new ArgumentException("The relation index belongs to a different table.", nameof(property));
        var key = ProviderKeyComponents.ToDataLinqKey(foreignKey);
        if (ProviderKeyComponents.IsNull(foreignKey)) return new(this, source, index, key, owner, null);
        if (source is not IDataLinqSourceRowServices)
            throw new NotSupportedException("This read source does not support canonical model materialization.");
        var factory = IAsyncSqlReaderFactory.Require(source.DatabaseAccess).CaptureInvocation()
            ?? throw new InvalidOperationException("The async reader factory returned no invocation snapshot.");
        var loader = new DataSourceAccessSourceRowLoader(source, owner, factory, ExecutionOperationKind.RelationLoad);
        if (Table.PrimaryKeyColumns.SequenceEqual(index.Columns))
        {
            var single = ProviderKeyComponents.SupportsNeutralSourceRowLoading(Table, source.Provider.DatabaseType)
                ? loader.CaptureSingleAsyncRead(Table, key) : loader.CaptureProviderMatchedSingleAsyncRead(Table, key);
            single.Validate();
            return new(this, source, index, key, owner, factory, single: single);
        }
        if (TryGetCanonicalIndexSourceServices(key, index, source, out _, out var canonical))
        {
            var neutral = loader.CaptureAsyncRead(new SourceIndexRowRequest(Table, index, canonical));
            neutral.Validate();
            return new(this, source, index, key, owner, factory, neutral: neutral);
        }
        // Use the captured key after provider callbacks; never reread caller-owned
        // components when binding the predicate for this same cache identity.
        var sql = TryConvertScalarProviderColumnValue(key, index.Columns, source, out var column, out var value)
            ? new ScalarColumnRowsQuery(Table, source, column, value).ToSql()
            : new SqlQuery(Table, source).Where(index.Columns, key).SelectQuery().ToSql();
        if (Table.PrimaryKeyColumns.Count == 0)
        {
            // A view can be a candidate-key target without a primary key. Preserve
            // its rows and cardinality without inventing row/index cache identities.
            var keylessRows = new List<CanonicalProviderValueRow>();
            var keyless = new AsyncBufferedRead<IReadOnlyList<CanonicalProviderValueRow>>(source,
                factory.BindReader(CapturedSql.Capture(sql)),
                reader => keylessRows.Add(ProviderRowDecoder.DecodeFullRow(reader, Table, $"sql:{source.Provider.DatabaseType}:relation")),
                () => keylessRows, owner, operationKind: ExecutionOperationKind.RelationLoad);
            keyless.Validate();
            return new(this, source, index, key, owner, factory, keyless: keyless);
        }
        var rows = new List<LoadedCanonicalRow>();
        var matched = new AsyncBufferedRead<IReadOnlyList<LoadedCanonicalRow>>(source, factory.BindReader(CapturedSql.Capture(sql)), reader =>
        {
            var decoded = ProviderRowDecoder.DecodeFullRow(reader, Table, $"sql:{source.Provider.DatabaseType}:relation");
            if (!decoded.TryCreateCanonicalPrimaryKey(out var primary)) throw new InvalidOperationException("A relation row has no canonical primary key.");
            rows.Add(new(decoded, primary));
        }, () => rows, owner, operationKind: ExecutionOperationKind.RelationLoad);
        matched.Validate();
        return new(this, source, index, key, owner, factory, matched: matched);
    }

    internal async Task<IImmutableInstance[]> GetRelationRowsAsyncCore<TKey>(TKey key,
        RelationProperty property, IDataSourceAccess source, CancellationToken token = default)
        where TKey : notnull
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var identity = ReadExecutionIdentity.Capture(source, ExecutionOperationKind.RelationLoad);
        using var scope = DataSourceAccess.BeginRead(source, "load asynchronous relation rows", operationKind: identity.Operation);
        var stage = ExecutionFailureStage.Validation;
        try
        {
            var prepared = PrepareRelationRowsAsyncCore(key, property, source, scope?.Step);
            stage = ExecutionFailureStage.Materialization;
            return await ExecuteRelationRowsAsyncCore(prepared, token).ConfigureAwait(false);
        }
        catch (Exception failure) { identity.ReportLocalFailure(failure, source, scope?.Step, token, stage); scope?.ReportFailure(failure); throw; }
    }

    internal Task<IImmutableInstance[]> ExecuteRelationRowsAsyncCore(PreparedRelationRows prepared, CancellationToken token)
        => ExecuteRelationRowsAsyncCore(prepared, static rows => rows, token);

    internal async Task<TResult> ExecuteRelationRowsAsyncCore<TResult>(PreparedRelationRows prepared,
        Func<IImmutableInstance[], TResult> complete, CancellationToken token)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        ArgumentNullException.ThrowIfNull(complete);
        if (!ReferenceEquals(prepared.Cache, this)) throw new ArgumentException("This relation plan belongs to a different cache.", nameof(prepared));
        var source = prepared.Source;
        var step = prepared.Owner;
        var key = prepared.Key;
        var index = prepared.Index;
        IAsyncReadFailureEvidence? observed = null;
        try
        {
            EnsureTransactionRowCache(source, step);
            token.ThrowIfCancellationRequested();
            if (prepared.Factory is null) return complete([]);
            if (prepared.Keyless is { } keyless)
            {
                observed = keyless;
                return await keyless.ExecuteAsync((rows, _, cancellation) =>
                {
                    var services = step is null ? (IDataLinqSourceRowServices)source : ((DataSourceAccess)source).GetOwnedRowServices(step);
                    var result = new IImmutableInstance[rows.Count];
                    for (var i = 0; i < rows.Count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        result[i] = services.MaterializationServices.GetOrMaterialize(rows[i]);
                        MetricsHandle.RecordDatabaseRowsLoaded(1);
                    }
                    MetricsHandle.RecordRowCacheMisses(rows.Count);
                    cancellation.ThrowIfCancellationRequested();
                    return complete(result);
                }, token).ConfigureAwait(false);
            }
            if (prepared.Single is { } single)
            {
                var row = await GetProviderRowAsyncCore(key, source, token, step, prepared.Factory,
                    read => observed = read, single, ExecutionOperationKind.RelationLoad).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return complete(row is null ? [] : [row]);
            }
            var cacheMembership = source is ReadOnlyAccess && indexCachePolicy.type != IndexCacheType.None;
            if (cacheMembership && TryGetIndexCache(index)?.TryGet(key, out var keys) == true)
            {
                var result = await LoadQueryRowsAsync(keys!, source, prepared.Factory, step, read => observed = read, token,
                    ExecutionOperationKind.RelationLoad).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return complete(result.ToArray());
            }
            var generation = CaptureReadGeneration();
            observed = (IAsyncReadFailureEvidence?)prepared.Neutral ?? prepared.Matched!;
            return prepared.Neutral is { } neutral
                ? await neutral.ExecuteAsync((result, _, cancellation) => complete(Materialize(result.Rows, cancellation)), token).ConfigureAwait(false)
                : await prepared.Matched!.ExecuteAsync((result, _, cancellation) => complete(Materialize(result, cancellation)), token).ConfigureAwait(false);

            IImmutableInstance[] Materialize(IReadOnlyList<LoadedCanonicalRow> loaded, CancellationToken cancellation)
            {
                var services = step is null ? (IDataLinqSourceRowServices)source : ((DataSourceAccess)source).GetOwnedRowServices(step);
                var result = new IImmutableInstance[loaded.Count];
                var membership = new DataLinqKey[loaded.Count];
                var hits = 0;
                for (var i = 0; i < loaded.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var row = loaded[i];
                    membership[i] = row.CanonicalProviderKey;
                    if (GetRowFromCache(row.CanonicalProviderKey, source, out var cached))
                    {
                        hits++;
                        result[i] = cached!;
                    }
                    else
                    {
                        result[i] = services.MaterializationServices.MaterializeAfterKnownCacheMiss(row with { ReadGeneration = generation });
                        MetricsHandle.RecordDatabaseRowsLoaded(1);
                    }
                }
                MetricsHandle.RecordRowCacheHits(hits);
                MetricsHandle.RecordRowCacheMisses(loaded.Count - hits);
                cancellation.ThrowIfCancellationRequested();
                if (cacheMembership)
                    lock (publicationGate)
                        if (ReferenceEquals(generation, readGeneration)) GetIndexCache(index).TryAdd(key, membership);
                RefreshOccupancyMetrics();
                return result;
            }
        }
        catch (Exception failure)
        {
            if (source is Transaction transaction && step is not null && ExecutionFailureContexts.GetCurrent(failure) is null)
            {
                var failures = new ExecutionFailures();
                failures.AddReported(failure, ExecutionFailureStage.Materialization, failure is OperationCanceledException canceled &&
                    canceled.CancellationToken == token && token.IsCancellationRequested ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.MaterializationError);
                var evidence = new ReadFailureEvidence(Effects: ExecutionEffects.NoStatement, Integrity: TransactionIntegrity.Confirmed);
                var assessed = true;
                if (observed is not null)
                {
                    using var assessmentDiagnostics = ExecutionFailureScope.Begin();
                    try
                    {
                        evidence = observed.GetReadFailureEvidence(failure)
                            ?? throw new InvalidOperationException("The provider returned no failure evidence.");
                    }
                    catch (Exception assessment)
                    {
                        assessed = false;
                        evidence = new();
                        failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery, step.Kind);
                    }
                }
                var context = failures.Snapshot(evidence, ExecutionCompletion.NotAttempted,
                    ExecutionRecoveryPolicy.ForReadFailure(evidence, assessed && !failures.HasCleanupFailure), transaction.TransactionID,
                    step.Kind, step.ProviderInstanceId);
                transaction.RecordAsyncReadFailure(step, context);
                ExecutionFailureContexts.Attach(failure, context);
            }
            throw;
        }
    }
}
