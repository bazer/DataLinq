using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class GeneratedAsyncLookupTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarAndCompositeLookupsUseAllThreeSourcesAndRetainNullAndCancellationSemantics(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(ScalarAndCompositeLookupsUseAllThreeSourcesAndRetainNullAndCancellationSemantics), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var expected = database.Query().DepartmentEmployees.First();
        database.Cache.Clear();
        var loaded = await Dept_emp.GetAsync(expected.dept_no, expected.emp_no, database);
        await Assert.That(loaded!.emp_no).IsEqualTo(expected.emp_no);
        await Assert.That(await Dept_emp.GetAsync(expected.dept_no, expected.emp_no, database.Provider.ReadOnlyAccess, cancellationToken: default)).IsSameReferenceAs(loaded);
        await Assert.That((await Department.GetAsync(expected.dept_no, database))!.DeptNo).IsEqualTo(expected.dept_no);
        await Assert.That(await Department.GetAsync(null!, database)).IsNull();
        await Assert.That(await Dept_emp.GetAsync("none", expected.emp_no, database)).IsNull();
        await using var transaction = database.Transaction();
        await Assert.That((await Dept_emp.GetAsync(expected.dept_no, expected.emp_no, transaction))!.emp_no).IsEqualTo(expected.emp_no);
        await Assert.That(async () => { await Dept_emp.GetAsync(expected.dept_no, expected.emp_no, transaction, new(true)); })
            .Throws<OperationCanceledException>();
        await Assert.That((await Dept_emp.GetAsync(expected.dept_no, expected.emp_no, transaction))!.emp_no).IsEqualTo(expected.emp_no);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ConvertedKeysReachTheCanonicalAsyncLookup(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<ConvertedDefaultHydrationDb>.Create(descriptor, nameof(ConvertedKeysReachTheCanonicalAsyncLookup));
        var database = scope.Database;
        var inserted = await database.InsertAsync(new MutableConvertedDefaultHydrationRow());
        database.Cache.Clear();
        var loaded = await ConvertedDefaultHydrationRow.GetAsync(inserted.Id!.Value, database);
        await Assert.That(loaded!.Id).IsEqualTo(inserted.Id);
        await Assert.That((await ConvertedDefaultHydrationRow.GetAsync(inserted.Id!.Value, database.Provider.ReadOnlyAccess))!.Id).IsEqualTo(loaded.Id);
        await using var transaction = database.Transaction();
        await Assert.That((await ConvertedDefaultHydrationRow.GetAsync(inserted.Id!.Value, transaction, cancellationToken: default))!.Id).IsEqualTo(inserted.Id);
        await Assert.That(await ConvertedDefaultHydrationRow.GetAsync(new ConvertedAutoIncrementId(-1), transaction)).IsNull();
    }
}
