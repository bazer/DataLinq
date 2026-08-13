using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class EmployeesLocalProjectionTerminalTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ComputedRowLocalTerminals_PreserveSuccessEmptyAndMultipleRowSemantics(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ComputedRowLocalTerminals_PreserveSuccessEmptyAndMultipleRowSemantics),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var seedEmployees = employeesDatabase.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(2)
            .ToArray();

        await Assert.That(seedEmployees.Length).IsEqualTo(2);

        var firstEmployeeNumber = seedEmployees[0].emp_no!.Value;
        var lastEmployeeNumber = seedEmployees[1].emp_no!.Value;
        var missingEmployeeNumber = int.MaxValue;
        var multiple = employeesDatabase.Query().Employees
            .Where(employee =>
                employee.emp_no == firstEmployeeNumber ||
                employee.emp_no == lastEmployeeNumber)
            .OrderBy(employee => employee.emp_no)
            .Select(employee => employee.emp_no!.Value + 1);
        var single = employeesDatabase.Query().Employees
            .Where(employee => employee.emp_no == firstEmployeeNumber)
            .Select(employee => employee.emp_no!.Value + 1);
        var empty = employeesDatabase.Query().Employees
            .Where(employee => employee.emp_no == missingEmployeeNumber)
            .Select(employee => employee.emp_no!.Value + 1);
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, multiple);

        await Assert.That(invocation.Template.Projection).IsTypeOf<QueryPlanProjection.ComputedRowLocal>();
        await Assert.That(invocation.Template.Projection.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.AotSafe);
        await Assert.That(multiple.First()).IsEqualTo(firstEmployeeNumber + 1);
        await Assert.That(multiple.FirstOrDefault()).IsEqualTo(firstEmployeeNumber + 1);
        await Assert.That(multiple.Last()).IsEqualTo(lastEmployeeNumber + 1);
        await Assert.That(multiple.LastOrDefault()).IsEqualTo(lastEmployeeNumber + 1);
        await Assert.That(single.Single()).IsEqualTo(firstEmployeeNumber + 1);
        await Assert.That(single.SingleOrDefault()).IsEqualTo(firstEmployeeNumber + 1);

        await Assert.That(empty.FirstOrDefault()).IsEqualTo(default(int));
        await Assert.That(empty.LastOrDefault()).IsEqualTo(default(int));
        await Assert.That(empty.SingleOrDefault()).IsEqualTo(default(int));
        _ = Capture<InvalidOperationException>(() => empty.First());
        _ = Capture<InvalidOperationException>(() => empty.Last());
        _ = Capture<InvalidOperationException>(() => empty.Single());
        _ = Capture<InvalidOperationException>(() => multiple.Single());
        _ = Capture<InvalidOperationException>(() => multiple.SingleOrDefault());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ConstructorBackedAnonymousTerminals_PreserveSuccessEmptyAndMultipleRowSemantics(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ConstructorBackedAnonymousTerminals_PreserveSuccessEmptyAndMultipleRowSemantics),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var seedEmployees = employeesDatabase.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(2)
            .ToArray();

        await Assert.That(seedEmployees.Length).IsEqualTo(2);

        var firstEmployeeNumber = seedEmployees[0].emp_no!.Value;
        var lastEmployeeNumber = seedEmployees[1].emp_no!.Value;
        var firstName = seedEmployees[0].first_name.Trim();
        var lastName = seedEmployees[1].first_name.Trim();
        var missingEmployeeNumber = int.MaxValue;
        var multiple = employeesDatabase.Query().Employees
            .Where(employee =>
                employee.emp_no == firstEmployeeNumber ||
                employee.emp_no == lastEmployeeNumber)
            .OrderBy(employee => employee.emp_no)
            .Select(employee => new LocalTerminalBox(employee.first_name.Trim()));
        var single = employeesDatabase.Query().Employees
            .Where(employee => employee.emp_no == firstEmployeeNumber)
            .Select(employee => new LocalTerminalBox(employee.first_name.Trim()));
        var empty = employeesDatabase.Query().Employees
            .Where(employee => employee.emp_no == missingEmployeeNumber)
            .Select(employee => new LocalTerminalBox(employee.first_name.Trim()));
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, multiple);

        await Assert.That(invocation.Template.Projection).IsTypeOf<QueryPlanProjection.Anonymous>();
        await Assert.That(invocation.Template.Projection.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(multiple.First().Value).IsEqualTo(firstName);
        await Assert.That(multiple.FirstOrDefault()!.Value).IsEqualTo(firstName);
        await Assert.That(multiple.Last().Value).IsEqualTo(lastName);
        await Assert.That(multiple.LastOrDefault()!.Value).IsEqualTo(lastName);
        await Assert.That(single.Single().Value).IsEqualTo(firstName);
        await Assert.That(single.SingleOrDefault()!.Value).IsEqualTo(firstName);

        await Assert.That(empty.FirstOrDefault()).IsNull();
        await Assert.That(empty.LastOrDefault()).IsNull();
        await Assert.That(empty.SingleOrDefault()).IsNull();
        _ = Capture<InvalidOperationException>(() => empty.First());
        _ = Capture<InvalidOperationException>(() => empty.Last());
        _ = Capture<InvalidOperationException>(() => empty.Single());
        _ = Capture<InvalidOperationException>(() => multiple.Single());
        _ = Capture<InvalidOperationException>(() => multiple.SingleOrDefault());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ImplicitRelationJoinedRowLocalTerminals_PreserveCardinalitySemantics(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ImplicitRelationJoinedRowLocalTerminals_PreserveCardinalitySemantics),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var seedRows = employeesDatabase.Query().DepartmentEmployees
            .OrderBy(row => row.emp_no)
            .ThenBy(row => row.dept_no)
            .Take(2)
            .ToArray();

        await Assert.That(seedRows.Length).IsEqualTo(2);

        var firstEmployeeNumber = seedRows[0].emp_no;
        var firstDepartmentNumber = seedRows[0].dept_no;
        var secondEmployeeNumber = seedRows[1].emp_no;
        var secondDepartmentNumber = seedRows[1].dept_no;
        var firstLabel = seedRows[0].departments.Name.Trim() + ":" + firstEmployeeNumber;
        var secondLabel = seedRows[1].departments.Name.Trim() + ":" + secondEmployeeNumber;
        var multiple = employeesDatabase.Query().DepartmentEmployees
            .Where(row =>
                (row.emp_no == firstEmployeeNumber && row.dept_no == firstDepartmentNumber) ||
                (row.emp_no == secondEmployeeNumber && row.dept_no == secondDepartmentNumber))
            .OrderBy(row => row.emp_no)
            .ThenBy(row => row.dept_no)
            .Select(row => row.departments.Name.Trim() + ":" + row.emp_no);
        var single = employeesDatabase.Query().DepartmentEmployees
            .Where(row => row.emp_no == firstEmployeeNumber && row.dept_no == firstDepartmentNumber)
            .Select(row => row.departments.Name.Trim() + ":" + row.emp_no);
        var empty = employeesDatabase.Query().DepartmentEmployees
            .Where(row => row.emp_no == int.MaxValue && row.dept_no == "missing")
            .Select(row => row.departments.Name.Trim() + ":" + row.emp_no);
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, multiple);

        await Assert.That(invocation.Template.Projection).IsTypeOf<QueryPlanProjection.JoinedRowLocal>();
        await Assert.That(invocation.Template.Projection.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(invocation.Template.Sources.Any(source => source.Kind == QueryPlanSourceKind.ImplicitJoin))
            .IsTrue();
        await Assert.That(multiple.First()).IsEqualTo(firstLabel);
        await Assert.That(multiple.FirstOrDefault()).IsEqualTo(firstLabel);
        await Assert.That(multiple.Last()).IsEqualTo(secondLabel);
        await Assert.That(multiple.LastOrDefault()).IsEqualTo(secondLabel);
        await Assert.That(single.Single()).IsEqualTo(firstLabel);
        await Assert.That(single.SingleOrDefault()).IsEqualTo(firstLabel);

        await Assert.That(empty.FirstOrDefault()).IsNull();
        await Assert.That(empty.LastOrDefault()).IsNull();
        await Assert.That(empty.SingleOrDefault()).IsNull();
        _ = Capture<InvalidOperationException>(() => empty.First());
        _ = Capture<InvalidOperationException>(() => empty.Last());
        _ = Capture<InvalidOperationException>(() => empty.Single());
        _ = Capture<InvalidOperationException>(() => multiple.Single());
        _ = Capture<InvalidOperationException>(() => multiple.SingleOrDefault());
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

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }

    private sealed record LocalTerminalBox(string Value);
}
