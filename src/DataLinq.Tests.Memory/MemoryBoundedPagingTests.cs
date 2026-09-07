using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryBoundedPagingTests
{
    [Test]
    [Arguments(false, 0)]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 0)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    public async Task PagesMatchLinqAcrossInputOrdersFiltersAndBounds(bool descending, int inputOrder)
    {
        var ids = Enumerable.Range(-500, 1000).Append(int.MinValue).Append(int.MaxValue).ToArray();
        var input = inputOrder switch
        {
            0 => ids.OrderBy(id => id).ToArray(),
            1 => ids.OrderByDescending(id => id).ToArray(),
            _ => ids.OrderBy(id => unchecked((uint)id * 2654435761u)).ToArray()
        };
        foreach (var minimum in new[] { int.MinValue, 0, 499, int.MaxValue })
        foreach (var (skip, take) in new[] { (0, 5), (3, 5), (100, 5), (0, 0), (250, 5), (1000, 5), (int.MaxValue, int.MaxValue) })
        {
            var database = CreateDatabase(input);
            var filtered = database.Query().Rows.Where(row => row.Id >= minimum);
            var query = descending ? filtered.OrderByDescending(row => row.Id) : filtered.OrderBy(row => row.Id);
            var actual = query.Skip(skip).Take(take).ToArray();
            var expected = input.Where(id => id >= minimum);
            expected = descending ? expected.OrderByDescending(id => id) : expected.OrderBy(id => id);
            await Assert.That(string.Join(",", actual.Select(row => row.Id)))
                .IsEqualTo(string.Join(",", expected.Skip(skip).Take(take)));
            await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(take == 0 ? 0L : input.Length);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo((long)actual.Length);
        }
    }

    [Test]
    public async Task SmallPageAllocationDoesNotGrowWithTableSize()
    {
        var small = CreateDatabase(Enumerable.Range(0, 2000).Reverse().ToArray());
        var large = CreateDatabase(Enumerable.Range(0, 20000).Reverse().ToArray());
        var smallQuery = small.Query().Rows.OrderBy(row => row.Id).Skip(100).Take(5).Select(row => row.Id);
        var largeQuery = large.Query().Rows.OrderBy(row => row.Id).Skip(100).Take(5).Select(row => row.Id);
        for (var index = 0; index < 10; index++)
        {
            _ = smallQuery.ToArray();
            _ = largeQuery.ToArray();
        }

        var smallBytes = AllocatedPerQuery(smallQuery);
        var largeBytes = AllocatedPerQuery(largeQuery);
        // A broad bound catches full-table buffers without testing timing or exact object sizes.
        await Assert.That(largeBytes).IsLessThan(smallBytes + 64 * 1024);
        await Assert.That(large.Diagnostics.Materializations).IsEqualTo(0L);
    }

    [Test]
    public async Task SmallPageHonorsCancellationBeforeAndBetweenResults()
    {
        var database = CreateDatabase(Enumerable.Range(0, 1000).Reverse().ToArray());
        var query = database.Query().Rows.OrderByDescending(row => row.Id).Skip(100).Take(5);
        using var cancellation = new CancellationTokenSource();
        using var results = database.Execute(query, cancellation.Token).GetEnumerator();
        await Assert.That(results.MoveNext()).IsTrue();
        await Assert.That(results.Current.Id).IsEqualTo(899);
        cancellation.Cancel();
        await Assert.That(() => results.MoveNext()).Throws<OperationCanceledException>();
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1L);
        var before = database.Diagnostics;
        await Assert.That(() => database.Execute(query, cancellation.Token).ToArray()).Throws<OperationCanceledException>();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    private static long AllocatedPerQuery(IQueryable<int> query)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 20; index++)
            _ = query.ToArray();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreateDatabase(int[] ids)
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var table = database.Metadata.GetTableModel(typeof(MemoryPrimitiveRow)).Table;
        return database.SeedCanonical<MemoryPrimitiveRow>(ids.Select(id =>
        {
            var row = new object?[table.ColumnCount];
            row[table.GetColumnByDbName("id").Index] = id;
            row[table.GetColumnByDbName("group_id").Index] = 7;
            row[table.GetColumnByDbName("name").Index] = "row";
            return row;
        }).ToArray());
    }
}
