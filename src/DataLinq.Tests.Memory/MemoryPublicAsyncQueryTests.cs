using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryPublicAsyncQueryTests
{
    [Test]
    public async Task PublicTerminalsUseTheExistingMemorySubsetAndCompleteImmediately()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>()
            .SeedCanonical<MemoryPrimitiveRow>([1, 7, "one"], [2, 7, "two"], [3, 8, "three"]);
        var query = database.Query().Rows;
        var count = query.CountAsync(row => row.GroupId == 7);
        await Assert.That(count.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await count).IsEqualTo(2);
        await Assert.That(await query.AnyAsync(row => row.Id == 2)).IsTrue();
        await Assert.That((await query.SingleAsync(row => row.Id == 2)).Name).IsEqualTo("two");
        await Assert.That(await query.SingleOrDefaultAsync(row => row.Id == 99)).IsNull();
        await Assert.That((await query.OrderBy(row => row.Id).FirstAsync()).Id).IsEqualTo(1);
        await Assert.That((await query.OrderBy(row => row.Id).FirstOrDefaultAsync())!.Id).IsEqualTo(1);
        await Assert.That(await query.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync()).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(async () => { await query.SumAsync(row => row.Id); }).Throws<QueryTranslationException>();
        await Assert.That(async () => { await query.LastAsync(); }).Throws<QueryTranslationException>();
    }

    [Test]
    public async Task PublicSequenceHonorsBothTokensDuringBufferedIteration()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>()
            .SeedCanonical<MemoryPrimitiveRow>([1, 7, "one"], [2, 7, "two"]);
        var query = database.Query().Rows.OrderBy(row => row.Id).Select(row => row.Id);
        foreach (var cancelMethod in new[] { true, false })
        {
            using var method = new CancellationTokenSource();
            using var enumeration = new CancellationTokenSource();
            await using var rows = query.AsAsyncEnumerable(method.Token).GetAsyncEnumerator(enumeration.Token);
            await Assert.That(await rows.MoveNextAsync()).IsTrue();
            if (cancelMethod) method.Cancel(); else enumeration.Cancel();
            await Assert.That(async () => { await rows.MoveNextAsync(); }).Throws<OperationCanceledException>();
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.That(async () => { await query.CountAsync(cancellationToken: canceled.Token); }).Throws<OperationCanceledException>();
        await Assert.That(await query.CountAsync()).IsEqualTo(2);
    }
}
