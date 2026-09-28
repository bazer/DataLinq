using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Linq;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class PublicAsyncQueryTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicTerminalsAndProjectionsMatchSynchronousQueries(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicTerminalsAndProjectionsMatchSynchronousQueries), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var query = database.Query().Departments.OrderBy(row => row.DeptNo);
        var expected = query.ToArray();
        await Assert.That((await query.ToListAsync()).Select(row => row.DeptNo).ToArray()).IsEquivalentTo(expected.Select(row => row.DeptNo).ToArray());
        await Assert.That(await query.Select(row => row.Name).ToArrayAsync()).IsEquivalentTo(expected.Select(row => row.Name).ToArray());
        var firstKey = expected[0].DeptNo;
        await Assert.That((await query.FirstAsync()).DeptNo).IsEqualTo(firstKey);
        await Assert.That((await query.FirstOrDefaultAsync())!.DeptNo).IsEqualTo(firstKey);
        await Assert.That((await query.LastAsync()).DeptNo).IsEqualTo(expected[^1].DeptNo);
        await Assert.That((await query.LastOrDefaultAsync())!.DeptNo).IsEqualTo(expected[^1].DeptNo);
        await Assert.That((await query.SingleAsync(row => row.DeptNo == firstKey)).DeptNo).IsEqualTo(firstKey);
        await Assert.That((await query.SingleOrDefaultAsync(row => row.DeptNo == firstKey))!.DeptNo).IsEqualTo(firstKey);
        await Assert.That((await query.FirstAsync(row => row.DeptNo == firstKey)).DeptNo).IsEqualTo(firstKey);
        await Assert.That((await query.FirstOrDefaultAsync(row => row.DeptNo == firstKey))!.DeptNo).IsEqualTo(firstKey);
        await Assert.That((await query.LastAsync(row => row.DeptNo == firstKey)).DeptNo).IsEqualTo(firstKey);
        await Assert.That((await query.LastOrDefaultAsync(row => row.DeptNo == firstKey))!.DeptNo).IsEqualTo(firstKey);
        await Assert.That(await query.CountAsync()).IsEqualTo(expected.Length);
        await Assert.That(await query.CountAsync(row => row.DeptNo == firstKey)).IsEqualTo(1);
        await Assert.That(await query.AnyAsync()).IsTrue();
        await Assert.That(await query.AnyAsync(row => row.DeptNo == "missing")).IsFalse();
        var one = query.Where(row => row.DeptNo == firstKey);
        await Assert.That((await one.SingleAsync()).DeptNo).IsEqualTo(firstKey);
        await Assert.That((await one.SingleOrDefaultAsync())!.DeptNo).IsEqualTo(firstKey);
        var anonymous = await query.Select(row => new { row.DeptNo, row.Name }).ToListAsync();
        await Assert.That(anonymous.Select(row => row.Name).ToArray()).IsEquivalentTo(expected.Select(row => row.Name).ToArray());
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicEmptyAndCardinalityResultsRetainLinqSemantics(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicEmptyAndCardinalityResultsRetainLinqSemantics), EmployeesFixtureProfile.TinySeeded);
        var query = scope.Database.Query().Departments.OrderBy(row => row.DeptNo);
        var empty = query.Where(row => row.DeptNo == "missing");
        await Assert.That(await empty.FirstOrDefaultAsync()).IsNull();
        await Assert.That(await empty.SingleOrDefaultAsync()).IsNull();
        await Assert.That(await empty.LastOrDefaultAsync()).IsNull();
        await Assert.That(async () => { await empty.FirstAsync(); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await empty.SingleAsync(); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await empty.LastAsync(); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await query.SingleAsync(); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await query.SingleOrDefaultAsync(); }).Throws<InvalidOperationException>();
        var emptyInts = scope.Database.Query().Managers.Where(row => row.dept_fk == "missing").Select(row => row.emp_no);
        int result = await emptyInts.SingleOrDefaultAsync();
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicAggregatesAndPreparedExecutionPreserveResultTypes(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicAggregatesAndPreparedExecutionPreserveResultTypes), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var query = database.Query().Managers;
        int sum = await query.SumAsync(row => row.emp_no);
        double average = await query.AverageAsync(row => row.emp_no);
        int minimum = await query.MinAsync(row => row.emp_no);
        int maximum = await query.MaxAsync(row => row.emp_no);
        await Assert.That(sum).IsEqualTo(query.Sum(row => row.emp_no));
        await Assert.That(Math.Abs(average - query.Average(row => row.emp_no)) < 0.0001).IsTrue();
        await Assert.That(minimum).IsEqualTo(query.Min(row => row.emp_no));
        await Assert.That(maximum).IsEqualTo(query.Max(row => row.emp_no));
        var nullable = database.Query().Employees;
        await Assert.That(await nullable.SumAsync(row => row.emp_no)).IsEqualTo(nullable.Sum(row => row.emp_no));
        await Assert.That(await nullable.MinAsync(row => row.emp_no)).IsEqualTo(nullable.Min(row => row.emp_no));
        await Assert.That(await nullable.MaxAsync(row => row.emp_no)).IsEqualTo(nullable.Max(row => row.emp_no));
        await Assert.That(Math.Abs((await nullable.AverageAsync(row => row.emp_no))!.Value - nullable.Average(row => row.emp_no)!.Value) < 0.0001).IsTrue();
        var empty = nullable.Where(row => row.first_name == "missing");
        await Assert.That(await empty.SumAsync(row => row.emp_no)).IsEqualTo(0);
        await Assert.That(await empty.AverageAsync(row => row.emp_no)).IsNull();
        await Assert.That(await empty.MinAsync(row => row.emp_no)).IsNull();
        await Assert.That(await empty.MaxAsync(row => row.emp_no)).IsNull();
        var plan = database.PrepareQuery(0, _ => database.Query().Departments.Count());
        using var transaction = database.Transaction();
        await Assert.That(await plan.ExecuteAsync(transaction, 0, cancellationToken: default)).IsEqualTo(database.Query().Departments.Count());
        await Assert.That(await transaction.Query().Departments.CountAsync()).IsEqualTo(database.Query().Departments.Count());
        var sequence = database.PrepareSequenceQuery("missing", key => database.Query().Departments.Where(row => row.DeptNo == key));
        // This is standard local async LINQ after explicit conversion/prepared execution.
        await Assert.That(await sequence.ExecuteAsync(database, "missing").CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicCancellationPreservesWarmQueryAndTransactionUsability(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicCancellationPreservesWarmQueryAndTransactionUsability), EmployeesFixtureProfile.TinySeeded);
        using var transaction = scope.Database.Transaction();
        var query = transaction.Query().Departments.OrderBy(row => row.DeptNo);
        var expected = await query.ToArrayAsync();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.That(async () => { await query.ToListAsync(cancellationToken: canceled.Token); }).Throws<OperationCanceledException>();
        await Assert.That(async () => { await query.CountAsync(cancellationToken: canceled.Token); }).Throws<OperationCanceledException>();
        await Assert.That((await query.ToArrayAsync()).Select(row => row.DeptNo).ToArray()).IsEquivalentTo(expected.Select(row => row.DeptNo).ToArray());
    }
}
