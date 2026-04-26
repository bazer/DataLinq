using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesInstanceEqualityTests
{
    private readonly EmployeesTestData _employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Immutable_EqualsSameCachedInstance(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(Immutable_EqualsSameCachedInstance));
        var employeeNumber = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).Select(x => x.emp_no!.Value).First();

        var employeeA = GetCachedEmployee(databaseScope.Database, employeeNumber);
        var employeeB = GetCachedEmployee(databaseScope.Database, employeeNumber);

        await Assert.That(employeeA.Equals(employeeB)).IsTrue();
        await Assert.That(employeeA.GetHashCode()).IsEqualTo(employeeB.GetHashCode());
        await Assert.That(ReferenceEquals(employeeA, employeeB)).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Immutable_EqualsDifferentInstancesWithSamePrimaryKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(Immutable_EqualsDifferentInstancesWithSamePrimaryKey));
        var employeeNumber = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).Select(x => x.emp_no!.Value).First();

        var employeeA = GetFreshEmployee(databaseScope.Database, employeeNumber);
        var employeeB = GetFreshEmployee(databaseScope.Database, employeeNumber);

        await Assert.That(ReferenceEquals(employeeA, employeeB)).IsFalse();
        await Assert.That(employeeA.Equals(employeeB)).IsTrue();
        await Assert.That(employeeB.Equals(employeeA)).IsTrue();
        await Assert.That(employeeA.GetHashCode()).IsEqualTo(employeeB.GetHashCode());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Immutable_EqualsDifferentRows_False(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(Immutable_EqualsDifferentRows_False));
        var employeeNumbers = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).Select(x => x.emp_no!.Value).Take(2).ToList();

        var employeeA = GetFreshEmployee(databaseScope.Database, employeeNumbers[0]);
        var employeeB = GetFreshEmployee(databaseScope.Database, employeeNumbers[1]);

        await Assert.That(employeeA.Equals(employeeB)).IsFalse();
        await Assert.That(employeeB.Equals(employeeA)).IsFalse();
        await Assert.That(employeeA.GetHashCode()).IsNotEqualTo(employeeB.GetHashCode());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutable_EqualsDifferentInstancesWithSamePrimaryKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(Mutable_EqualsDifferentInstancesWithSamePrimaryKey));
        var employeeNumber = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).Select(x => x.emp_no!.Value).First();

        var mutableA = GetFreshEmployee(databaseScope.Database, employeeNumber).Mutate();
        var mutableB = GetFreshEmployee(databaseScope.Database, employeeNumber).Mutate();

        await Assert.That(ReferenceEquals(mutableA, mutableB)).IsFalse();
        await Assert.That(mutableA.Equals(mutableB)).IsTrue();
        await Assert.That(mutableA.GetHashCode()).IsEqualTo(mutableB.GetHashCode());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutable_EqualsAfterNonPrimaryKeyMutation_StillUsesPrimaryKeyIdentity(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(Mutable_EqualsAfterNonPrimaryKeyMutation_StillUsesPrimaryKeyIdentity));
        var employeeNumber = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).Select(x => x.emp_no!.Value).First();

        var employee = GetFreshEmployee(databaseScope.Database, employeeNumber);
        var mutableA = employee.Mutate();
        var mutableB = employee.Mutate();

        mutableA.first_name = "Changed_" + Guid.NewGuid().ToString("N")[..8];

        await Assert.That(mutableA.HasChanges()).IsTrue();
        await Assert.That(mutableB.HasChanges()).IsFalse();
        await Assert.That(mutableA.Equals(mutableB)).IsTrue();
        await Assert.That(mutableA.GetHashCode()).IsEqualTo(mutableB.GetHashCode());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Immutable_AndMutable_EqualsAcrossTypesByPrimaryKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(Immutable_AndMutable_EqualsAcrossTypesByPrimaryKey));
        var employeeNumber = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).Select(x => x.emp_no!.Value).First();

        var immutable = GetFreshEmployee(databaseScope.Database, employeeNumber);
        var mutable = immutable.Mutate();

        await Assert.That(immutable.Equals(mutable)).IsTrue();
        await Assert.That(mutable.Equals(immutable)).IsTrue();
        await Assert.That(immutable.GetHashCode()).IsEqualTo(mutable.GetHashCode());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Collections_UsePrimaryKeyEqualityForPersistedInstances(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(Collections_UsePrimaryKeyEqualityForPersistedInstances));
        var employeeNumbers = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).Select(x => x.emp_no!.Value).Take(2).ToList();

        var employeeA1 = GetFreshEmployee(databaseScope.Database, employeeNumbers[0]);
        var employeeA2 = GetFreshEmployee(databaseScope.Database, employeeNumbers[0]);
        var employeeB = GetFreshEmployee(databaseScope.Database, employeeNumbers[1]);

        var list = new List<Employee> { employeeA1 };
        var set = new HashSet<Employee> { employeeA1 };
        var dictionary = new Dictionary<MutableEmployee, string>();
        var mutableKey = employeeA1.Mutate();
        dictionary[mutableKey] = "value";
        mutableKey.first_name = "Changed_" + Guid.NewGuid().ToString("N")[..8];

        var groups = new List<Employee> { employeeA1, employeeB, employeeA2 }.GroupBy(x => x).ToList();

        await Assert.That(list.Remove(employeeA2)).IsTrue();
        await Assert.That(set.Contains(employeeA2)).IsTrue();
        await Assert.That(dictionary.ContainsKey(mutableKey)).IsTrue();
        await Assert.That(dictionary[mutableKey]).IsEqualTo("value");
        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups.Single(g => g.Key.emp_no == employeeNumbers[0]).Count()).IsEqualTo(2);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NewMutable_DoesNotEqualPersistedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(NewMutable_DoesNotEqualPersistedEmployee));
        var employeeNumber = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).Select(x => x.emp_no!.Value).First();

        var existingEmployee = GetFreshEmployee(databaseScope.Database, employeeNumber);
        var newEmployee = _employees.NewEmployee();

        await Assert.That(newEmployee.IsNew()).IsTrue();
        await Assert.That(existingEmployee.Equals(newEmployee)).IsFalse();
        await Assert.That(newEmployee.Equals(existingEmployee)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SaveTransition_ChangesHashCodeAndThenUsesPersistedPrimaryKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(SaveTransition_ChangesHashCodeAndThenUsesPersistedPrimaryKey),
            EmployeesSeedMode.None);

        var database = databaseScope.Database;
        var employee = _employees.NewEmployee();
        var hashBeforeSave = employee.GetHashCode();

        var savedEmployee = database.Save(employee);
        database.Provider.State.ClearCache();
        var fetchedEmployee = database.Query().Employees.Single(x => x.emp_no == employee.emp_no);
        var hashAfterSave = employee.GetHashCode();

        await Assert.That(employee.IsNew()).IsFalse();
        await Assert.That(employee.emp_no).IsNotNull();
        await Assert.That(hashAfterSave).IsNotEqualTo(hashBeforeSave);
        await Assert.That(employee.Equals(savedEmployee)).IsTrue();
        await Assert.That(employee.Equals(fetchedEmployee)).IsTrue();
        await Assert.That(hashAfterSave).IsEqualTo(savedEmployee.GetHashCode());
        await Assert.That(hashAfterSave).IsEqualTo(fetchedEmployee.GetHashCode());
    }

    private static Employee GetCachedEmployee(Database<EmployeesDb> database, int employeeNumber) =>
        database.Query().Employees.Single(x => x.emp_no == employeeNumber);

    private static Employee GetFreshEmployee(Database<EmployeesDb> database, int employeeNumber)
    {
        database.Provider.State.ClearCache();
        return database.Query().Employees.Single(x => x.emp_no == employeeNumber);
    }
}
