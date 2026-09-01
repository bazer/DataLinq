using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesMutationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_MutateOnMissingModel_ThrowsArgumentNullException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_MutateOnMissingModel_ThrowsArgumentNullException),
            EmployeesFixtureProfile.TinySeeded);

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
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_MutateOrNewOnMissingModel_ReturnsNewMutableEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_MutateOrNewOnMissingModel_ReturnsNewMutableEmployee),
            EmployeesFixtureProfile.TinySeeded);

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
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_MutateOrNewOnExistingRequiredKey_UpdatesWithoutTrackingPrimaryKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_MutateOrNewOnExistingRequiredKey_UpdatesWithoutTrackingPrimaryKey),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var department = database.Query().Departments.OrderBy(row => row.DeptNo).First();
        var updatedName = $"Updated {department.DeptNo}";

        var mutable = department.MutateOrNew(
            deptNo: department.DeptNo,
            name: updatedName);

        await Assert.That(mutable.GetChanges().Any(change => change.Key.PrimaryKey)).IsFalse();

        var saved = mutable.Save(database);

        await Assert.That(saved.DeptNo).IsEqualTo(department.DeptNo);
        await Assert.That(saved.Name).IsEqualTo(updatedName);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_MutateOrNewOnExistingRequiredKey_RejectsDifferentPrimaryKeyImmediately(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_MutateOrNewOnExistingRequiredKey_RejectsDifferentPrimaryKeyImmediately),
            EmployeesFixtureProfile.TinySeeded);

        var department = databaseScope.Database.Query().Departments
            .OrderBy(row => row.DeptNo)
            .First();

        var exception = Capture<ArgumentException>(() => department.MutateOrNew(
            deptNo: "z112",
            name: department.Name));

        await Assert.That(exception.ParamName).IsEqualTo("deptNo");
        await Assert.That(exception.Message).Contains("authoritative key");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_MutateOrNewOnMissingRequiredKey_InsertsSuppliedPrimaryKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_MutateOrNewOnMissingRequiredKey_InsertsSuppliedPrimaryKey),
            EmployeesFixtureProfile.TinySeeded);

        const string departmentNumber = "z112";
        const string departmentName = "Issue 112";
        var database = databaseScope.Database;
        var missing = database.Query().Departments
            .FirstOrDefault(row => row.DeptNo == departmentNumber);

        var mutable = missing.MutateOrNew(
            deptNo: departmentNumber,
            name: departmentName);
        var saved = mutable.Save(database);

        await Assert.That(saved.DeptNo).IsEqualTo(departmentNumber);
        await Assert.That(saved.Name).IsEqualTo(departmentName);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_MutateOrNewOnExistingCompositeKey_ValidatesAllKeyComponents(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_MutateOrNewOnExistingCompositeKey_ValidatesAllKeyComponents),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var assignment = database.Query().DepartmentEmployees
            .OrderBy(row => row.dept_no)
            .ThenBy(row => row.emp_no)
            .First();
        var updatedToDate = assignment.to_date.AddDays(-1);

        var mutable = assignment.MutateOrNew(
            deptNo: assignment.dept_no,
            empNo: assignment.emp_no,
            fromDate: assignment.from_date,
            toDate: updatedToDate);

        await Assert.That(mutable.GetChanges().Any(change => change.Key.PrimaryKey)).IsFalse();

        var saved = mutable.Save(database);

        await Assert.That(saved.dept_no).IsEqualTo(assignment.dept_no);
        await Assert.That(saved.emp_no).IsEqualTo(assignment.emp_no);
        await Assert.That(saved.to_date).IsEqualTo(updatedToDate);

        var exception = Capture<ArgumentException>(() => assignment.MutateOrNew(
            deptNo: assignment.dept_no,
            empNo: assignment.emp_no + 1,
            fromDate: assignment.from_date,
            toDate: assignment.to_date));

        await Assert.That(exception.ParamName).IsEqualTo("empNo");
        await Assert.That(exception.Message).Contains("authoritative key");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_ResetWithoutModel_RevertsToOriginalState(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_ResetWithoutModel_RevertsToOriginalState),
            EmployeesFixtureProfile.TinySeeded);

        var employee = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).First();
        var mutable = new MutableEmployee(employee);

        mutable.birth_date = DateOnly.Parse("1990-01-01");
        mutable.Reset();

        await Assert.That(mutable.IsNew()).IsFalse();
        await Assert.That(mutable.HasChanges()).IsFalse();
        await Assert.That(mutable.birth_date).IsEqualTo(employee.birth_date);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_ResetWithModel_RevertsToProvidedModel(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_ResetWithModel_RevertsToProvidedModel),
            EmployeesFixtureProfile.TinySeeded);

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
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_SaveResetsChangeTrackingAndPersistsValues(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_SaveResetsChangeTrackingAndPersistsValues),
            EmployeesFixtureProfile.TinySeeded);

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
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Mutation_ChangingPropertyMarksMutableAsChanged(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Mutation_ChangingPropertyMarksMutableAsChanged),
            EmployeesFixtureProfile.TinySeeded);

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

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
