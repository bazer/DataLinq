using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;
using DataLinq.Tests.Models;
//using DataLinq.Tests.Models.Allround;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DataLinq.Tests;

public class SourceGeneratorTests
{
    public static DatabaseFixture fixture;

    static SourceGeneratorTests()
    {
        fixture = new DatabaseFixture();
    }

    public static IEnumerable<object[]> GetEmployees()
    {
        foreach (var db in fixture.AllEmployeesDb)
            yield return new object[] { db };
    }

    public SourceGeneratorTests()
    {
        foreach (var employeesDb in fixture.AllEmployeesDb)
        {
            employeesDb.Provider.State.ClearCache();
        }
    }


    

    //[Theory]
    //[MemberData(nameof(GetEmployees))]
    //public void CheckBasics(Database<EmployeesDb> employeesDb)
    //{
    //    //var proxy = new Titles();
    //    //var event2 = proxy.Event;
    //    //ITitles iTitle = ITitles
    //    //var temp = proxy.Event;
    //    //Assert.Equal("Generator: Discount", proxy.Generated);
    //    //Assert.NotNull(proxy);
    //}
}