using System.Linq;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests;

public class SqlQueryTests : BaseTests
{
    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhere(Database<EmployeesDb> employeesDb)
    {
        var departement = employeesDb
            .From<Department>()
            .Where("dept_no").EqualTo("d005")
            .Select();

        Assert.Single(departement);
        Assert.Equal("d005", departement.Single().DeptNo);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void GetFromQueryWhere(Database<EmployeesDb> employeesDb)
    {
        var departement = employeesDb.Transaction().GetFromQuery<Department>("SELECT * FROM departments WHERE dept_no = 'd005'");

        Assert.Single(departement);
        Assert.Equal("d005", departement.Single().DeptNo);
    }
}