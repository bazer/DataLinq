using System;
using System.Linq;
using Xunit;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Linq;

public class ContainsTranslationTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture fixture;

    public ContainsTranslationTests(DatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void EmptyArrayContains_ReturnsNoRows()
    {
        foreach (var db in fixture.AllEmployeesDb)
        {
            var results = db.Query().Employees.Where(e => new int[0].Contains(e.emp_no.Value)).ToList();
            Assert.Empty(results);
        }
    }

    [Fact]
    public void NegatedContains_FiltersRows()
    {
        foreach (var db in fixture.AllEmployeesDb)
        {
            var ids = db.Query().Employees.Select(e => e.emp_no.Value).Take(3).ToArray();
            Assert.True(ids.Length >= 2);
            var include = ids.Take(2).ToArray();

            var outside = db.Query().Employees.Where(e => !include.Contains(e.emp_no.Value)).Select(e => e.emp_no.Value).ToList();
            Assert.DoesNotContain(include[0], outside);
            Assert.DoesNotContain(include[1], outside);
        }
    }

    [Fact]
    public void OpImplicitReadOnlySpanContains_FiltersRows()
    {
        foreach (var db in fixture.AllEmployeesDb)
        {
            var ids = db.Query().Employees.Select(e => e.emp_no.Value).Take(2).ToArray();
            Assert.Equal(2, ids.Length);
            var arr = ids.ToArray();
            var results = db.Query().Employees.Where(e => ((ReadOnlySpan<int>)arr).Contains(e.emp_no.Value)).Select(e => e.emp_no.Value).ToList();
            Assert.All(ids, id => Assert.Contains(id, results));
        }
    }

    [Fact]
    public void ConstantItemContainsTrue_ReturnsAllRows()
    {
        foreach (var db in fixture.AllEmployeesDb)
        {
            var anyId = db.Query().Employees.Select(e => e.emp_no.Value).First();
            var allCount = db.Query().Employees.Count();
            var count = db.Query().Employees.Where(e => new[] { anyId }.Contains(anyId)).Count();
            Assert.Equal(allCount, count);
        }
    }

    [Fact]
    public void ConstantItemContainsFalse_ReturnsNoRows()
    {
        foreach (var db in fixture.AllEmployeesDb)
        {
            var ids = db.Query().Employees.Select(e => e.emp_no.Value).Take(2).ToArray();
            Assert.Equal(2, ids.Length);
            var count = db.Query().Employees.Where(e => new[] { ids[0] }.Contains(ids[1])).Count();
            Assert.Equal(0, count);
        }
    }
}
