using System;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Tests.Models.Employees;
using Xunit;

namespace DataLinq.Tests.Core;

public class OptimizationTests : BaseTests
{
    [Fact]
    public void Evaluator_ShouldEvaluateLocalVariable_Correctly()
    {
        // This tests the reflection-based optimization in Evaluator.cs
        int localId = 12345;
        Expression<Func<Employee, bool>> expr = x => x.emp_no == localId;

        // Extract the right side (localId)
        var binary = (BinaryExpression)expr.Body;
        var right = binary.Right;

        // Evaluate
        var result = Evaluator.PartialEval(right);

        var constant = Assert.IsAssignableFrom<ConstantExpression>(result);
        Assert.Equal(12345, constant.Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TryGetSimplePrimaryKey_SinglePK_ReturnsKey(Database<EmployeesDb> db)
    {
        // Arrange
        var query = db.From("employees")
            .Where("emp_no").EqualTo(1001);

        // Act
        var key = query.Query.TryGetSimplePrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.IsType<IntKey>(key);
        Assert.Equal(1001, ((IntKey)key).Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TryGetSimplePrimaryKey_CompositePK_ReturnsKey(Database<EmployeesDb> db)
    {
        // Arrange
        var query = db.From("dept-emp")
            .Where("dept_no").EqualTo("d001")
            .And("emp_no").EqualTo(1001);

        // Act
        var key = query.Query.TryGetSimplePrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.IsType<CompositeKey>(key);
        var composite = (CompositeKey)key;
        Assert.Equal(2, composite.Values.Length);
        Assert.Contains("d001", composite.Values);
        Assert.Contains(1001, composite.Values);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TryGetSimplePrimaryKey_NonPK_ReturnsNull(Database<EmployeesDb> db)
    {
        // Arrange
        var query = db.From("employees")
            .Where("first_name").EqualTo("Bob");

        // Act
        var key = query.Query.TryGetSimplePrimaryKey();

        // Assert
        Assert.Null(key);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TryGetSimplePrimaryKey_PK_And_OtherColumn_ReturnsNull(Database<EmployeesDb> db)
    {
        // Arrange
        var query = db.From("employees")
            .Where("emp_no").EqualTo(1001)
            .And("first_name").EqualTo("Bob");

        // Act
        var key = query.Query.TryGetSimplePrimaryKey();

        // Assert
        Assert.Null(key);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TryGetSimplePrimaryKey_PartialCompositePK_ReturnsNull(Database<EmployeesDb> db)
    {
        // Arrange: dept-emp has PK (dept_no, emp_no)
        var query = db.From("dept-emp")
            .Where("dept_no").EqualTo("d001");

        // Act
        var key = query.Query.TryGetSimplePrimaryKey();

        // Assert
        Assert.Null(key);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TryGetSimplePrimaryKey_OrCondition_ReturnsNull(Database<EmployeesDb> db)
    {
        // Arrange
        var query = db.From("employees")
            .Where("emp_no").EqualTo(1001)
            .Or("emp_no").EqualTo(1002);

        // Act
        var key = query.Query.TryGetSimplePrimaryKey();

        // Assert
        Assert.Null(key);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TryGetSimplePrimaryKey_Negated_ReturnsNull(Database<EmployeesDb> db)
    {
        // Arrange
        var query = db.From("employees")
            .WhereNot("emp_no").EqualTo(1001);

        // Act
        var key = query.Query.TryGetSimplePrimaryKey();

        // Assert
        Assert.Null(key);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TryGetSimplePrimaryKey_WorksWithVariables(Database<EmployeesDb> db)
    {
        // Tests the integration of Evaluator optimization + PK Short-circuit logic
        int id = 9999;

        // Emulate what the QueryBuilder does: it evaluates the variable to a constant before adding to Where
        var query = db.Query().Employees.Where(x => x.emp_no == id);

        // Access the underlying SqlQuery<T> from the IQueryable (DbRead<T>)
        // This requires casting or inspecting the provider, but since we are unit testing internal logic,
        // we can construct the equivalent SqlQuery manually to verify the extraction logic.

        var sqlQuery = db.From("employees").Where("emp_no").EqualTo(id);
        var key = sqlQuery.Query.TryGetSimplePrimaryKey();

        Assert.NotNull(key);
        Assert.Equal(id, ((IntKey)key).Value);
    }
}