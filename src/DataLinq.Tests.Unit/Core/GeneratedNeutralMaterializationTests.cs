using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
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
    public async Task GeneratedImmutableFactory_MaterializesWithNeutralReadSourceEndToEnd()
    {
        var metadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel<GeneratedNeutralMaterializationDb>()
            .ValueOrException();
        var table = metadata.TableModels
            .Single(x => x.Model.CsType.Type == typeof(GeneratedNeutralMaterializationRow))
            .Table;
        var canonicalValues = new object?[table.ColumnCount];
        canonicalValues[table.GetColumnByDbName("id").Index] = 42;
        canonicalValues[table.GetColumnByDbName("name").Index] = "neutral";

        IDataLinqReadSource readSource = new NeutralReadSource(metadata);
        var cache = new NoCacheMaterializationCache();
        var runtime = new ReadSourceModelMaterializationRuntime(readSource, cache);
        var services = new ModelMaterializationServices("generated-neutral-test", runtime);

        var materialized = services.GetOrMaterialize(
            CanonicalProviderValueRow.Create(table, canonicalValues));

        var row = materialized as ImmutableGeneratedNeutralMaterializationRow;
        await Assert.That(row).IsNotNull();
        await Assert.That(row!.Id).IsEqualTo(42);
        await Assert.That(row.Name).IsEqualTo("neutral");
        await Assert.That(row.GetReadSource()).IsSameReferenceAs(readSource);
        await Assert.That(readSource is IDataSourceAccess).IsFalse();
        await Assert.That(cache.CacheLookupCalls).IsEqualTo(1);
        await Assert.That(cache.CacheMissMetrics).IsEqualTo(1);
        await Assert.That(cache.MaterializationMetrics).IsEqualTo(1);
        await Assert.That(cache.PublicationCalls).IsEqualTo(1);

        var exception = Capture<InvalidOperationException>(() => row.GetDataSource());
        await Assert.That(exception.Message).Contains(nameof(IDataSourceAccess));
        await Assert.That(exception.Message).Contains(nameof(IImmutableInstance.GetReadSource));
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

    private sealed class NeutralReadSource(DatabaseDefinition metadata) : IDataLinqReadSource
    {
        public DatabaseDefinition Metadata { get; } = metadata;
    }

    private sealed class NoCacheMaterializationCache : IReadSourceMaterializationCache
    {
        public int CacheLookupCalls { get; private set; }
        public int CacheMissMetrics { get; private set; }
        public int MaterializationMetrics { get; private set; }
        public int PublicationCalls { get; private set; }

        public bool TryGetCached(
            TableDefinition table,
            DataLinqKey canonicalProviderKey,
            out IImmutableInstance? instance)
        {
            CacheLookupCalls++;
            instance = null;
            return false;
        }

        public ModelCachePublicationResult PublishCached(
            TableDefinition table,
            DataLinqKey canonicalProviderKey,
            RowData rowData,
            IImmutableInstance instance)
        {
            PublicationCalls++;
            return ModelCachePublicationResult.NotCached();
        }

        public void RecordCacheLookup(TableDefinition table, bool hit)
        {
            if (!hit)
                CacheMissMetrics++;
        }

        public void RecordMaterialization(TableDefinition table)
        {
            MaterializationMetrics++;
        }

        public void RecordCacheInsertion(TableDefinition table)
        {
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

    [Column("name")]
    public abstract string Name { get; }
}
