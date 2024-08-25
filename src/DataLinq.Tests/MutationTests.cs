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
}
