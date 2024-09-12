using System;
using System.Linq;
using DataLinq.Tests.Models.Employees;
using Xunit;

namespace DataLinq.Tests;

public class MutationTests : BaseTests
{
    private Helpers helpers = new Helpers();

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TestMutateNull(Database<EmployeesDb> employeesDb)
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            var employee = employeesDb.Query().Employees
                .Where(x => x.emp_no == 423692592)
                .FirstOrDefault().Mutate();
        });
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TestMutateNullNew(Database<EmployeesDb> employeesDb)
    {
        var employee = employeesDb.Query().Employees
            .Where(x => x.emp_no == 423692592)
            .FirstOrDefault().MutateOrNew();

        Assert.NotNull(employee);
        Assert.NotEqual(423692592, employee.emp_no);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Reset_Should_ResetToNewInstance(Database<EmployeesDb> employeesDb)
    {
        // Arrange
        var employee = employeesDb.Query().Employees.First();
        var mutable = new MutableEmployee(employee);

        // Act
        mutable.Reset();

        // Assert
        Assert.False(mutable.IsNew());
        Assert.False(mutable.HasChanges());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Reset_WithModel_Should_ResetToModelInstance(Database<EmployeesDb> employeesDb)
    {
        // Arrange
        var employee = employeesDb.Query().Employees.First();
        var mutable = new MutableEmployee(employee);
        
        // Act
        mutable.birth_date = DateOnly.Parse("1990-01-01");
        mutable.Reset(employee);

        // Assert
        Assert.Equal(employee.birth_date, mutable.birth_date);
        Assert.False(mutable.HasChanges());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Reset_WithRowData_Should_ResetToRowDataInstance(Database<EmployeesDb> employeesDb)
    {
        // Arrange
        var employee = employeesDb.Query().Employees.First();
        var rowData = employee.GetRowData();
        var mutable = new MutableEmployee(employee);

        // Act
        mutable.birth_date = DateOnly.Parse("1990-01-01");
        mutable.Reset(rowData);

        // Assert
        Assert.Equal(employee.birth_date, mutable.birth_date);
        Assert.False(mutable.HasChanges());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TestMutationSave_Should_ResetChanges(Database<EmployeesDb> employeesDb)
    {
        // Arrange
        var emp_no = 998999;
        var newBirthDate = DateOnly.FromDateTime(DateTime.Now.AddYears(-30));
        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var mutable = employee.Mutate();

        // Act
        mutable.birth_date = newBirthDate;
        var dbEmployee = mutable.Save(employeesDb);

        // Assert
        Assert.False(mutable.HasChanges());
        Assert.NotEqual(newBirthDate, employee.birth_date);
        Assert.Equal(newBirthDate, dbEmployee.birth_date);
        Assert.Equal(newBirthDate, mutable.birth_date);
        Assert.False(mutable.IsNew());
        Assert.False(mutable.HasChanges());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TestMutation_Should_HaveChanges(Database<EmployeesDb> employeesDb)
    {
        // Arrange
        var emp_no = 998998;
        var newBirthDate = DateOnly.Parse("1990-01-01");
        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var mutable = employee.Mutate();

        // Act
        mutable.birth_date = newBirthDate;

        // Assert
        Assert.True(mutable.HasChanges());
    }
}
