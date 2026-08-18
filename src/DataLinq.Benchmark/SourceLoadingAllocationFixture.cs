using DataLinq.Instances;
using DataLinq.Memory;
using DataLinq.Metadata;

namespace DataLinq.Benchmark;

/// <summary>
/// Allocation attribution for the synchronous source-loading orchestration boundaries. Inputs are
/// precomputed so each method measures only the named boundary rather than provider setup or I/O.
/// </summary>
internal sealed class SourceLoadingAllocationFixture
{
    private const int ValidationRowCount = SourceRowLoadResult.LinearValidationThreshold;
    private const int ChunkSize = 500;
    private readonly TableDefinition table;
    private readonly List<DataLinqKey> batchKeys;
    private readonly CanonicalProviderValueRow[] validationRows;
    private readonly SourcePrimaryKeyRowRequest validationRequest;
    private readonly object[] publishedRows;

    internal SourceLoadingAllocationFixture()
    {
        var database = new MemoryDatabase<CanonicalKeyBenchmarkDatabase>();
        table = database.Metadata.TableModels
            .Single(tableModel => tableModel.Model.CsType.Type == typeof(CanonicalKeyBenchmarkScalarRow))
            .Table;
        batchKeys = Enumerable.Range(0, BenchmarkContext.BatchOperationCount)
            .Select(DataLinqKey.FromValue)
            .ToList();
        validationRows = Enumerable.Range(0, ValidationRowCount)
            .Select(id => CanonicalProviderValueRow.Create(table, new object?[] { id }))
            .ToArray();
        validationRequest = SourcePrimaryKeyRowRequest.Borrow(
            table,
            batchKeys,
            offset: 0,
            count: ValidationRowCount);
        publishedRows = Enumerable.Range(0, ValidationRowCount)
            .Select(static _ => new object())
            .ToArray();
    }

    internal int CreateBatchSlices()
    {
        var checksum = 0;

        for (var iteration = 0; iteration < BenchmarkContext.BatchOperationCount; iteration++)
        {
            var offset = (iteration & 1) * ChunkSize;
            var slice = new ReadOnlyListSlice<DataLinqKey>(batchKeys, offset, ChunkSize);
            checksum = unchecked(checksum + slice.Length + slice[0].GetHashCode());
        }

        return checksum;
    }

    internal int ConstructBorrowedRequests()
    {
        var checksum = 0;

        for (var iteration = 0; iteration < BenchmarkContext.BatchOperationCount; iteration++)
        {
            var request = SourcePrimaryKeyRowRequest.Borrow(
                table,
                batchKeys,
                iteration % (batchKeys.Count - ValidationRowCount),
                ValidationRowCount);
            checksum = unchecked(
                checksum + request.CanonicalProviderKeys[0].GetHashCode());
        }

        return checksum;
    }

    internal int ConstructLoaderResultStorage()
    {
        var checksum = 0;

        for (var iteration = 0; iteration < BenchmarkContext.BatchOperationCount; iteration++)
        {
            var builder = new SourceRowLoadResult.Builder(
                validationRequest,
                ValidationRowCount);
            checksum = unchecked(checksum + builder.Build().Rows.Length);
        }

        return checksum;
    }

    internal int ValidateLoaderResults()
    {
        var checksum = 0;

        for (var iteration = 0; iteration < BenchmarkContext.BatchOperationCount; iteration++)
        {
            var builder = new SourceRowLoadResult.Builder(
                validationRequest,
                ValidationRowCount);
            foreach (var row in validationRows)
                builder.Add(row);

            var result = builder.Build();
            checksum = unchecked(
                checksum +
                result.Rows.Length +
                result.Rows[^1].CanonicalProviderKey.GetHashCode());
        }

        return checksum;
    }

    internal int PublishCacheResults()
    {
        var checksum = 0;

        for (var iteration = 0; iteration < BenchmarkContext.BatchOperationCount; iteration++)
        {
            var destination = new Dictionary<DataLinqKey, object>(ValidationRowCount);
            for (var index = 0; index < ValidationRowCount; index++)
            {
                destination.Add(
                    validationRequest.CanonicalProviderKeys[index],
                    publishedRows[index]);
            }

            checksum = unchecked(checksum + destination.Count);
        }

        return checksum;
    }
}
