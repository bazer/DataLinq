using System;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Memory;
using DataLinq.Metadata;

namespace DataLinq.Benchmark;

/// <summary>
/// Isolates the allocation-sensitive portion of cold materialization. The legacy comparison
/// deliberately reconstructs the composite key at the same downstream hand-off points that
/// existed before loaded rows began carrying their source-validated key.
/// </summary>
internal sealed class CanonicalKeyPropagationAllocationFixture
{
    private readonly IDataLinqReadSource readSource;
    private readonly CanonicalProviderValueRow scalarRow;
    private readonly CanonicalProviderValueRow compositeRow;
    private readonly CanonicalProviderValueRow typedIdRow;
    private readonly CanonicalProviderValueRow converterBackedRow;
    private readonly CanonicalProviderValueRow binaryRow;

    internal CanonicalKeyPropagationAllocationFixture()
    {
        var database = new MemoryDatabase<CanonicalKeyBenchmarkDatabase>();
        readSource = database.ReadSource;
        scalarRow = CreateRow(
            FindTable<CanonicalKeyBenchmarkScalarRow>(database.Metadata),
            42);
        compositeRow = CreateRow(
            FindTable<CanonicalKeyBenchmarkCompositeRow>(database.Metadata),
            42,
            "employee-42");
        typedIdRow = CreateRow(
            FindTable<CanonicalKeyBenchmarkTypedIdRow>(database.Metadata),
            42);
        converterBackedRow = CreateRow(
            FindTable<CanonicalKeyBenchmarkReferenceRow>(database.Metadata),
            "employee-42");
        binaryRow = CreateRow(
            FindTable<CanonicalKeyBenchmarkBinaryRow>(database.Metadata),
            new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 });
    }

    internal int PropagateScalarKeys() => RunPropagated(scalarRow);
    internal int PropagateCompositeKeys() => RunPropagated(compositeRow);
    internal int PropagateTypedIdKeys() => RunPropagated(typedIdRow);
    internal int PropagateConverterBackedKeys() => RunPropagated(converterBackedRow);
    internal int PropagateBinaryKeys() => RunPropagated(binaryRow);

    internal int ReconstructCompositeKeys()
    {
        var checksum = 0;

        for (var index = 0; index < BenchmarkContext.BatchOperationCount; index++)
        {
            var boundaryKey = CreatePrimaryKey(compositeRow);
            var cacheOrchestrationKey = CreatePrimaryKey(compositeRow);
            var rowData = ProviderRowMaterializer.Materialize(
                compositeRow,
                "benchmark.key-propagation.legacy");
            var immutable = new AllocationImmutable(rowData, readSource);
            var publicationKey = CreatePrimaryKey(compositeRow);

            checksum = unchecked(
                checksum +
                boundaryKey.GetHashCode() +
                cacheOrchestrationKey.GetHashCode() +
                immutable.PrimaryKeys().GetHashCode() +
                publicationKey.GetHashCode());
        }

        return checksum;
    }

    private int RunPropagated(CanonicalProviderValueRow providerRow)
    {
        var checksum = 0;

        for (var index = 0; index < BenchmarkContext.BatchOperationCount; index++)
        {
            var loadedRow = new LoadedCanonicalRow(providerRow, CreatePrimaryKey(providerRow));
            var rowData = ProviderRowMaterializer.Materialize(
                loadedRow.ProviderRow,
                "benchmark.key-propagation.propagated");
            var immutable = new AllocationImmutable(
                new KnownCanonicalPrimaryKeyRowData(rowData, loadedRow.CanonicalProviderKey),
                readSource);

            // The same key instance feeds orchestration, immutable identity, and publication.
            checksum = unchecked(
                checksum +
                loadedRow.CanonicalProviderKey.GetHashCode() +
                loadedRow.CanonicalProviderKey.GetHashCode() +
                immutable.PrimaryKeys().GetHashCode() +
                loadedRow.CanonicalProviderKey.GetHashCode());
        }

        return checksum;
    }

    private static DataLinqKey CreatePrimaryKey(CanonicalProviderValueRow row) =>
        row.TryCreateCanonicalPrimaryKey(out var key)
            ? key
            : throw new InvalidOperationException(
                $"Allocation benchmark table '{row.Table.DbName}' unexpectedly has no primary key.");

    private static CanonicalProviderValueRow CreateRow(
        TableDefinition table,
        params object?[] canonicalValues) =>
        CanonicalProviderValueRow.Create(table, canonicalValues);

    private static TableDefinition FindTable<TModel>(DatabaseDefinition metadata) =>
        metadata.TableModels
            .Single(tableModel => tableModel.Model.CsType.Type == typeof(TModel))
            .Table;

    private sealed class AllocationImmutable(IRowData rowData, IDataLinqReadSource source)
        : Immutable<CanonicalKeyBenchmarkScalarRow, CanonicalKeyBenchmarkDatabase>(rowData, source);
}
