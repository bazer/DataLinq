using System.Collections.Generic;

namespace DataLinq.Tests;

public abstract class BaseTests
{
    public static DatabaseFixture Fixture { get; }

    static BaseTests()
    {
        Fixture = new DatabaseFixture();
    }

    public BaseTests()
    {
    }

    public static IEnumerable<object[]> GetEmployees()
    {
        foreach (var db in Fixture.AllEmployeesDb)
            yield return new object[] { db };
    }

    public static IEnumerable<object[]> GetEmployeeConnections()
    {
        foreach (var db in Fixture.EmployeeConnections)
            yield return new object[] { db };
    }
}