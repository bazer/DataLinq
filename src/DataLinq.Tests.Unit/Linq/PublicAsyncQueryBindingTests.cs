using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Linq;

namespace DataLinq.Tests.Unit.Linq;

public sealed class PublicAsyncQueryBindingTests
{
    [Test]
    public async Task ForeignProvidersAndWrappedForeignProvidersAreRejected()
    {
        var foreign = new[] { 1, 2 }.AsQueryable();
        var wrapped = new DataLinq.Queryable<int>(foreign.Provider, foreign.Expression);
        foreach (var query in new IQueryable<int>[] { foreign, wrapped })
        {
            await Assert.That(() => query.AsAsyncEnumerable()).Throws<NotSupportedException>();
            await Unsupported(() => query.ToListAsync());
            await Unsupported(() => query.ToArrayAsync());
            await Unsupported(() => query.FirstAsync());
            await Unsupported(() => query.FirstOrDefaultAsync());
            await Unsupported(() => query.SingleAsync());
            await Unsupported(() => query.SingleOrDefaultAsync());
            await Unsupported(() => query.LastAsync());
            await Unsupported(() => query.LastOrDefaultAsync());
            await Unsupported(() => query.AnyAsync());
            await Unsupported(() => query.CountAsync());
            await Unsupported(() => query.CountAsync(value => value > 1));
        }
    }

    [Test]
    public async Task NullArgumentsRetainPublicParameterNamesBeforeCancellation()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        IQueryable<int> missing = null!;
        var sourceFailure = await Assert.That(() => missing.AsAsyncEnumerable(canceled.Token)).Throws<ArgumentNullException>();
        await Assert.That(sourceFailure!.ParamName).IsEqualTo("source");
        var foreign = new[] { 1 }.AsQueryable();
        var predicateFailure = await Assert.That(async () => { await foreign.FirstAsync(null!, canceled.Token); }).Throws<ArgumentNullException>();
        await Assert.That(predicateFailure!.ParamName).IsEqualTo("predicate");
        var selectorFailure = await Assert.That(async () => { await foreign.SumAsync((Expression<Func<int, int>>)null!, canceled.Token); }).Throws<ArgumentNullException>();
        await Assert.That(selectorFailure!.ParamName).IsEqualTo("selector");
    }

    [Test]
    public async Task NumericOverloadsBindToTheirExactDeclaredResultTypes()
    {
        var query = new[] { 1 }.AsQueryable();
        await Unsupported<int>(() => query.SumAsync(value => (int)value, cancellationToken: default));
        await Unsupported<int?>(() => query.SumAsync(value => (int?)value, cancellationToken: default));
        await Unsupported<long>(() => query.SumAsync(value => (long)value, cancellationToken: default));
        await Unsupported<long?>(() => query.SumAsync(value => (long?)value, cancellationToken: default));
        await Unsupported<float>(() => query.SumAsync(value => (float)value, cancellationToken: default));
        await Unsupported<float?>(() => query.SumAsync(value => (float?)value, cancellationToken: default));
        await Unsupported<double>(() => query.SumAsync(value => (double)value, cancellationToken: default));
        await Unsupported<double?>(() => query.SumAsync(value => (double?)value, cancellationToken: default));
        await Unsupported<decimal>(() => query.SumAsync(value => (decimal)value, cancellationToken: default));
        await Unsupported<decimal?>(() => query.SumAsync(value => (decimal?)value, cancellationToken: default));
        await Unsupported<double>(() => query.AverageAsync(value => (int)value, cancellationToken: default));
        await Unsupported<double?>(() => query.AverageAsync(value => (int?)value, cancellationToken: default));
        await Unsupported<double>(() => query.AverageAsync(value => (long)value, cancellationToken: default));
        await Unsupported<double?>(() => query.AverageAsync(value => (long?)value, cancellationToken: default));
        await Unsupported<float>(() => query.AverageAsync(value => (float)value, cancellationToken: default));
        await Unsupported<float?>(() => query.AverageAsync(value => (float?)value, cancellationToken: default));
        await Unsupported<double>(() => query.AverageAsync(value => (double)value, cancellationToken: default));
        await Unsupported<double?>(() => query.AverageAsync(value => (double?)value, cancellationToken: default));
        await Unsupported<decimal>(() => query.AverageAsync(value => (decimal)value, cancellationToken: default));
        await Unsupported<decimal?>(() => query.AverageAsync(value => (decimal?)value, cancellationToken: default));
        await Unsupported<int>(() => query.MinAsync(value => value));
        await Unsupported<int>(() => query.MaxAsync(value => value));
        await Unsupported<int?>(() => query.MinAsync(value => (int?)value));
        await Unsupported<int?>(() => query.MaxAsync(value => (int?)value));
        await Unsupported<string?>(() => query.MinAsync(value => value.ToString()));
        await Unsupported<string?>(() => query.MaxAsync(value => value.ToString()));
    }

    private static async Task Unsupported<T>(Func<ValueTask<T>> action) =>
        await Assert.That(async () => { await action(); }).Throws<NotSupportedException>();
}
