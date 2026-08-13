using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class GeneratedNeutralMaterializationTests
{
    [Test]
    public async Task NeutralSource_LoadersComposeWithGeneratedMaterializationAndIdentityCache()
    {
        var metadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel<GeneratedNeutralMaterializationDb>()
            .ValueOrException();
        var table = metadata.TableModels
            .Single(x => x.Model.CsType.Type == typeof(GeneratedNeutralMaterializationRow))
            .Table;
        var index = table.ColumnIndices.Single(x => x.Name == "ix_generated_neutral_group");
        var readSource = new NeutralReadSource(
            metadata,
            [CreateCanonicalRow(table, id: 42, groupId: 7, name: "neutral")]);
        var primaryServices = (IDataLinqSourceRowServices)readSource;
        var indexServices = (IDataLinqIndexRowServices)readSource;

        var primaryRequest = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(42)]);
        var primaryResult = primaryServices.RowLoader.Load(primaryRequest);
        var primaryMaterialized = primaryServices.MaterializationServices.GetOrMaterialize(
            primaryResult.Rows.Single());

        var indexRequest = new SourceIndexRowRequest(
            table,
            index,
            DataLinqKey.FromValue(7));
        var indexResult = indexServices.IndexRowLoader.Load(indexRequest);
        var indexMaterialized = indexServices.MaterializationServices.GetOrMaterialize(
            indexResult.Rows.Single());

        var row = primaryMaterialized as ImmutableGeneratedNeutralMaterializationRow;
        object sourceIdentity = readSource;
        await Assert.That(row).IsNotNull();
        await Assert.That(row!.Id).IsEqualTo(42);
        await Assert.That(row.GroupId).IsEqualTo(7);
        await Assert.That(row.Name).IsEqualTo("neutral");
        await Assert.That(row.GetReadSource()).IsSameReferenceAs(readSource);
        await Assert.That(sourceIdentity is IDataSourceAccess).IsFalse();
        await Assert.That(sourceIdentity is IDatabaseProvider).IsFalse();
        await Assert.That(sourceIdentity is IDatabaseAccess).IsFalse();
        await Assert.That(indexServices.MaterializationServices)
            .IsSameReferenceAs(primaryServices.MaterializationServices);
        await Assert.That(indexMaterialized).IsSameReferenceAs(primaryMaterialized);

        await Assert.That(primaryResult.Request).IsSameReferenceAs(primaryRequest);
        await Assert.That(primaryResult.Table).IsSameReferenceAs(table);
        await Assert.That(indexResult.Request).IsSameReferenceAs(indexRequest);
        await Assert.That(indexResult.Table).IsSameReferenceAs(table);
        await Assert.That(indexResult.Index).IsSameReferenceAs(index);
        await Assert.That(readSource.RowsEnumerated).IsEqualTo(2);
        await Assert.That(readSource.CacheLookupCalls).IsEqualTo(2);
        await Assert.That(readSource.CacheHits).IsEqualTo(1);
        await Assert.That(readSource.CacheMisses).IsEqualTo(1);
        await Assert.That(readSource.MaterializationMetrics).IsEqualTo(1);
        await Assert.That(readSource.PublicationCalls).IsEqualTo(1);
        await Assert.That(readSource.CacheInsertionMetrics).IsEqualTo(1);
        await Assert.That(readSource.CachedRowCount).IsEqualTo(1);

        var exception = Capture<InvalidOperationException>(() => row.GetDataSource());
        await Assert.That(exception.Message).Contains(nameof(IDataSourceAccess));
        await Assert.That(exception.Message).Contains(nameof(IImmutableInstance.GetReadSource));
    }

    [Test]
    public async Task NeutralSource_PreCancelledLoadsDoNotEnumerateOrMaterialize()
    {
        var metadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel<GeneratedNeutralMaterializationDb>()
            .ValueOrException();
        var table = metadata.TableModels
            .Single(x => x.Model.CsType.Type == typeof(GeneratedNeutralMaterializationRow))
            .Table;
        var index = table.ColumnIndices.Single(x => x.Name == "ix_generated_neutral_group");
        var readSource = new NeutralReadSource(
            metadata,
            [CreateCanonicalRow(table, id: 42, groupId: 7, name: "neutral")]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var primaryRequest = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(42)],
            cancellation.Token);
        var indexRequest = new SourceIndexRowRequest(
            table,
            index,
            DataLinqKey.FromValue(7),
            cancellation.Token);

        var primaryException = Capture<OperationCanceledException>(() =>
            ((IDataLinqSourceRowServices)readSource).RowLoader.Load(primaryRequest));
        var indexException = Capture<OperationCanceledException>(() =>
            ((IDataLinqIndexRowServices)readSource).IndexRowLoader.Load(indexRequest));

        await Assert.That(primaryException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(indexException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(readSource.RowsEnumerated).IsEqualTo(0);
        await Assert.That(readSource.CacheLookupCalls).IsEqualTo(0);
        await Assert.That(readSource.MaterializationMetrics).IsEqualTo(0);
        await Assert.That(readSource.PublicationCalls).IsEqualTo(0);
        await Assert.That(readSource.CachedRowCount).IsEqualTo(0);
    }

    private static CanonicalProviderValueRow CreateCanonicalRow(
        TableDefinition table,
        int id,
        int groupId,
        string name)
    {
        var canonicalValues = new object?[table.ColumnCount];
        canonicalValues[table.GetColumnByDbName("id").Index] = id;
        canonicalValues[table.GetColumnByDbName("group_id").Index] = groupId;
        canonicalValues[table.GetColumnByDbName("name").Index] = name;
        return CanonicalProviderValueRow.Create(table, canonicalValues);
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }

    private sealed class NeutralReadSource :
        IDataLinqSourceRowServices,
        IDataLinqIndexRowServices,
        ISourceRowLoader,
        ISourceIndexRowLoader,
        IReadSourceMaterializationCache
    {
        private readonly CanonicalProviderValueRow[] rows;
        private readonly RowCache rowCache = new();
        private readonly IModelMaterializationServices materializationServices;

        internal NeutralReadSource(
            DatabaseDefinition metadata,
            IEnumerable<CanonicalProviderValueRow> rows)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            ArgumentNullException.ThrowIfNull(rows);
            this.rows = rows.ToArray();

            foreach (var row in this.rows)
            {
                if (!ReferenceEquals(row.Table.Database, metadata))
                {
                    throw new ArgumentException(
                        $"Canonical row table '{row.Table.DbName}' is not owned by the neutral source metadata.",
                        nameof(rows));
                }
            }

            materializationServices = new ModelMaterializationServices(
                "generated-neutral-test",
                new ReadSourceModelMaterializationRuntime(this, this));
        }

        public DatabaseDefinition Metadata { get; }
        public int RowsEnumerated { get; private set; }
        public int CacheLookupCalls { get; private set; }
        public int CacheHits { get; private set; }
        public int CacheMisses { get; private set; }
        public int MaterializationMetrics { get; private set; }
        public int PublicationCalls { get; private set; }
        public int CacheInsertionMetrics { get; private set; }
        public int CachedRowCount => rowCache.Count;

        IModelMaterializationServices IDataLinqReadServices.MaterializationServices =>
            materializationServices;

        ISourceRowLoader IDataLinqSourceRowServices.RowLoader => this;
        ISourceIndexRowLoader IDataLinqIndexRowServices.IndexRowLoader => this;

        public SourceRowLoadResult Load(SourcePrimaryKeyRowRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateOwnedTable(request.Table);
            request.ThrowIfCancellationRequested();

            var requestedKeys = new HashSet<DataLinqKey>(request.CanonicalProviderKeys);
            var loadedRows = new List<CanonicalProviderValueRow>();
            foreach (var row in rows)
            {
                request.ThrowIfCancellationRequested();
                RowsEnumerated++;

                if (row.TryCreateCanonicalPrimaryKey(out var key) && requestedKeys.Contains(key))
                    loadedRows.Add(row);
            }

            return new SourceRowLoadResult(request, loadedRows);
        }

        public SourceIndexRowLoadResult Load(SourceIndexRowRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateOwnedTable(request.Table);
            request.ThrowIfCancellationRequested();

            var loadedRows = new List<CanonicalProviderValueRow>();
            foreach (var row in rows)
            {
                request.ThrowIfCancellationRequested();
                RowsEnumerated++;

                if (MatchesIndex(row, request))
                    loadedRows.Add(row);
            }

            return new SourceIndexRowLoadResult(request, loadedRows);
        }

        public bool TryGetCached(
            TableDefinition table,
            DataLinqKey canonicalProviderKey,
            out IImmutableInstance? instance)
        {
            ValidateOwnedTable(table);
            CacheLookupCalls++;
            return rowCache.TryGetValue(canonicalProviderKey, out instance);
        }

        public ModelCachePublicationResult PublishCached(
            TableDefinition table,
            DataLinqKey canonicalProviderKey,
            RowData rowData,
            IImmutableInstance instance)
        {
            ValidateOwnedTable(table);
            if (!ReferenceEquals(rowData.Table, table))
                throw new ArgumentException("Published row metadata does not match the cache table.", nameof(rowData));

            PublicationCalls++;
            if (rowCache.TryAddRow(canonicalProviderKey, rowData, instance))
                return ModelCachePublicationResult.Inserted();

            IImmutableInstance? existing;
            if (rowCache.TryGetValue(canonicalProviderKey, out existing) && existing is not null)
                return ModelCachePublicationResult.Existing(existing);

            throw new InvalidOperationException(
                $"Neutral row cache failed to publish key '{canonicalProviderKey}' for table '{table.DbName}'.");
        }

        public void RecordCacheLookup(TableDefinition table, bool hit)
        {
            ValidateOwnedTable(table);
            if (hit)
                CacheHits++;
            else
                CacheMisses++;
        }

        public void RecordMaterialization(TableDefinition table)
        {
            ValidateOwnedTable(table);
            MaterializationMetrics++;
        }

        public void RecordCacheInsertion(TableDefinition table)
        {
            ValidateOwnedTable(table);
            CacheInsertionMetrics++;
        }

        private static bool MatchesIndex(
            CanonicalProviderValueRow row,
            SourceIndexRowRequest request)
        {
            for (var componentIndex = 0; componentIndex < request.Index.Columns.Count; componentIndex++)
            {
                if (!Equals(
                    row[request.Index.Columns[componentIndex]],
                    request.CanonicalProviderIndexKey.GetValue(componentIndex)))
                {
                    return false;
                }
            }

            return true;
        }

        private void ValidateOwnedTable(TableDefinition table)
        {
            ArgumentNullException.ThrowIfNull(table);
            if (!ReferenceEquals(table.Database, Metadata))
            {
                throw new InvalidOperationException(
                    $"Table '{table.DbName}' is not owned by the neutral source metadata.");
            }
        }
    }
}

[Database("generated_neutral_materialization")]
public sealed partial class GeneratedNeutralMaterializationDb : IDatabaseModel
{
    public GeneratedNeutralMaterializationDb(IDataLinqReadSource readSource)
    {
        ReadSource = readSource;
    }

    internal IDataLinqReadSource ReadSource { get; }

    public DbRead<GeneratedNeutralMaterializationRow> Rows { get; } = null!;
}

[Table("generated_neutral_materialization_rows")]
public abstract partial class GeneratedNeutralMaterializationRow
    : Immutable<GeneratedNeutralMaterializationRow, GeneratedNeutralMaterializationDb>,
      ITableModel<GeneratedNeutralMaterializationDb>
{
    protected GeneratedNeutralMaterializationRow(
        IRowData rowData,
        IDataSourceAccess dataSource)
        : base(rowData, dataSource)
    {
    }

    protected GeneratedNeutralMaterializationRow(
        IRowData rowData,
        IDataLinqReadSource readSource)
        : base(rowData, readSource)
    {
    }

    [PrimaryKey]
    [Column("id")]
    public abstract int Id { get; }

    [Index("ix_generated_neutral_group", IndexCharacteristic.Simple, IndexType.BTREE)]
    [Column("group_id")]
    public abstract int GroupId { get; }

    [Column("name")]
    public abstract string Name { get; }
}
