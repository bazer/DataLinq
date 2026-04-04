using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesMutationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_MutateOnMissingModel_ThrowsArgumentNullException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Mutation_MutateOnMissingModel_ThrowsArgumentNullException),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        await AssertThrows<ArgumentNullException>(() =>
        {
            _ = employeesDatabase.Query().Employees
                .Where(x => x.emp_no == 423692592)
                .FirstOrDefault()!
                .Mutate();
        });
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_MutateOrNewOnMissingModel_ReturnsNewMutableEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Mutation_MutateOrNewOnMissingModel_ReturnsNewMutableEmployee),
            EmployeesSeedMode.Bogus);

        var employee = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no == 423692592)
            .FirstOrDefault()
            .MutateOrNew(
                birthDate: DateOnly.Parse("1990-01-01"),
                firstName: "name",
                gender: Employee.Employeegender.M,
                hireDate: DateOnly.Parse("2022-12-02"),
                lastName: "last name");

        await Assert.That(employee).IsNotNull();
        await Assert.That(employee.emp_no).IsNotEqualTo(423692592);
        await Assert.That(employee.IsNew()).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_ResetWithoutModel_RevertsToOriginalState(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Mutation_ResetWithoutModel_RevertsToOriginalState),
            EmployeesSeedMode.Bogus);

        var employee = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).First();
        var mutable = new MutableEmployee(employee);

        mutable.birth_date = DateOnly.Parse("1990-01-01");
        mutable.Reset();

        await Assert.That(mutable.IsNew()).IsFalse();
        await Assert.That(mutable.HasChanges()).IsFalse();
        await Assert.That(mutable.birth_date).IsEqualTo(employee.birth_date);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_ResetWithModel_RevertsToProvidedModel(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Mutation_ResetWithModel_RevertsToProvidedModel),
            EmployeesSeedMode.Bogus);

        var employee = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).First();
        var mutable = new MutableEmployee(employee)
        {
            birth_date = DateOnly.Parse("1990-01-01")
        };

        mutable.Reset(employee);

        await Assert.That(mutable.birth_date).IsEqualTo(employee.birth_date);
        await Assert.That(mutable.HasChanges()).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_SaveResetsChangeTrackingAndPersistsValues(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Mutation_SaveResetsChangeTrackingAndPersistsValues),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = employeesDatabase.Query().Employees.OrderBy(x => x.emp_no).First();
        var originalBirthDate = employee.birth_date;
        var mutable = employee.Mutate();
        var newBirthDate = DateOnly.Parse("1990-01-01");

        mutable.birth_date = newBirthDate;
        var saved = mutable.Save(employeesDatabase);

        await Assert.That(mutable.HasChanges()).IsFalse();
        await Assert.That(employee.birth_date).IsEqualTo(originalBirthDate);
        await Assert.That(employee.birth_date).IsNotEqualTo(newBirthDate);
        await Assert.That(saved.birth_date).IsEqualTo(newBirthDate);
        await Assert.That(mutable.birth_date).IsEqualTo(newBirthDate);
        await Assert.That(mutable.IsNew()).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_ChangingPropertyMarksMutableAsChanged(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Mutation_ChangingPropertyMarksMutableAsChanged),
            EmployeesSeedMode.Bogus);

        var employee = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).First();
        var mutable = employee.Mutate();

        mutable.birth_date = DateOnly.Parse("1990-01-01");

        await Assert.That(mutable.HasChanges()).IsTrue();
    }

    private static async Task AssertThrows<TException>(Action action)
        where TException : Exception
    {
        var threw = false;

        try
        {
            action();
        }
        catch (TException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }
}
