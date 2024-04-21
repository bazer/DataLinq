using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests;

public class ThreadingTests : BaseTests
{
    private Helpers helpers = new Helpers();

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void ReadParallel(Database<Employees> employeesDb)
    {
        Parallel.For(0, 10, i =>
        {
            SetAndTest(1004, employeesDb);
            SetAndTest(1005, employeesDb);
            SetAndTest(1006, employeesDb);
            SetAndTest(1007, employeesDb);
            SetAndTest(1008, employeesDb);
        });
    }

    private void SetAndTest(int value, Database<Employees> employeesDb)
    {
        var employee = employeesDb.Query().Employees.Single(x => x.emp_no == value);
        Assert.Equal(value, employee.emp_no);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void CommitTransactionParallel(Database<Employees> employeesDb)
    {
        var emp_no = 999990;

        Parallel.For(0, 10, i =>
        {
            var id = emp_no - i;

            var employee = helpers.GetEmployee(id, employeesDb);
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            using var transaction = employeesDb.Transaction();
            var dbEmployeeReturn = transaction.Update(employeeMut);

            transaction.Commit();

            var dbEmployee = employeesDb.Query().Employees.Single(x => x.emp_no == id);
            Assert.NotEqual(orgBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            Assert.Equal(newBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        });
    }

    //[Fact]
    //public void LazyLoadSingleValue()
    //{
    //    var emp_no = 10001;

    //    Parallel.For(0, 100, i =>
    //    {
    //        var manager = fixture.employeesDb.Query().dept_manager.Single(x => x.dept_no == "d005" && x.emp_no == emp_no + i);

    //        Assert.NotNull(manager.departments);
    //        Assert.Equal("d005", manager.departments.dept_no);
    //    });
    //}

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void LazyLoadList(Database<Employees> employeesDb)
    {
        Parallel.For(0, 100, i =>
        {
            var department = employeesDb.Query().Departments.Single(x => x.DeptNo == "d005");

            Assert.NotNull(department.Managers);
            Assert.NotEmpty(department.Managers);
            Assert.True(10 < department.Managers.Count());
            Assert.Equal("d005", department.Managers.First().Department.DeptNo);
        });
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void MakeSnapshot(Database<Employees> employeesDb)
    {
        var rand = Random.Shared;

        Parallel.For(0, 100, i =>
        {
            var salaryLow = rand.Next(0, 200000);
            var salaryHigh = rand.Next(salaryLow, 200000);

            var salaries = employeesDb.Query().salaries.Where(x => x.salary > salaryLow && x.salary < salaryHigh).Take(10).ToList();
            var snapshot = employeesDb.Provider.State.Cache.MakeSnapshot();

            Assert.NotEmpty(salaries);
            Assert.NotEqual(0, snapshot.TableCaches.Single(x => x.TableName == "salaries").TotalBytes);
        });
    }
}