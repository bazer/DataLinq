using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class ProviderEquivalentPrimaryKeyLookupTests
{
    [Test]
    [NotInParallel]
    [MethodDataSource(
        typeof(ProviderEquivalentPrimaryKeyLookupTests),
        nameof(CaseInsensitiveServerProviders))]
    public async Task StaticGet_ProviderEquivalentStringKeyReturnsStoredCanonicalRow(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(StaticGet_ProviderEquivalentStringKeyReturnsStoredCanonicalRow),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        DataLinqMetrics.Reset();
        var caseEquivalent = Department.Get("D005", database);
        var repeatedCaseEquivalent = Department.Get("D005", database);
        var snapshot = DataLinqMetrics.Snapshot();

        await Assert.That(caseEquivalent).IsNotNull();
        await Assert.That(caseEquivalent!.DeptNo).IsEqualTo("d005");
        await Assert.That(repeatedCaseEquivalent)
            .IsSameReferenceAs(caseEquivalent);
        await Assert.That(snapshot.Commands.ReaderExecutions).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Hits).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Misses).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Stores).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Materializations).IsEqualTo(1);

        database.Provider.State.ClearCache();

        var paddingEquivalent = Department.Get("d005 ", database);

        await Assert.That(paddingEquivalent).IsNotNull();
        await Assert.That(paddingEquivalent!.DeptNo).IsEqualTo("d005");
    }

    public static IEnumerable<Func<TestProviderDescriptor>> CaseInsensitiveServerProviders()
    {
        foreach (var providerFactory in TestProviderDataSources.ActiveProviders())
        {
            var provider = providerFactory();
            if (provider.DatabaseType is DatabaseType.MySQL or DatabaseType.MariaDB)
                yield return () => provider with { };
        }
    }
}
