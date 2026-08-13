using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Linq;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class EmployeesDirectProjectionTerminalTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DirectProjectionSequencesAndTerminals_MatchFromTransactionRoot(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DirectProjectionSequencesAndTerminals_MatchFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnlyScalar = employeesDatabase.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(3)
            .Select(employee => employee.emp_no!.Value)
            .ToArray();
        var transactionScalar = transaction.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(3)
            .Select(employee => employee.emp_no!.Value)
            .ToArray();
        var readOnlyRows = employeesDatabase.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(3)
            .Select(employee => new
            {
                EmployeeNumber = employee.emp_no,
                employee.first_name
            })
            .ToArray();
        var transactionRows = transaction.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(3)
            .Select(employee => new
            {
                EmployeeNumber = employee.emp_no,
                employee.first_name
            })
            .ToArray();

        await Assert.That(transactionScalar.SequenceEqual(readOnlyScalar)).IsTrue();
        await Assert.That(transactionRows.Select(static row => (row.EmployeeNumber, row.first_name))
                .SequenceEqual(readOnlyRows.Select(static row => (row.EmployeeNumber, row.first_name))))
            .IsTrue();
        await Assert.That(transaction.Query().Employees
                .OrderBy(employee => employee.emp_no)
                .Select(employee => employee.emp_no!.Value)
                .First())
            .IsEqualTo(readOnlyScalar[0]);
        await Assert.That(transaction.Query().Employees
                .OrderBy(employee => employee.emp_no)
                .Select(employee => new { EmployeeNumber = employee.emp_no, employee.first_name })
                .First()
                .EmployeeNumber)
            .IsEqualTo(readOnlyRows[0].EmployeeNumber);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarMemberTerminals_PreserveSuccessEmptyAndMultipleRowSemantics(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ScalarMemberTerminals_PreserveSuccessEmptyAndMultipleRowSemantics),
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
            .Select(employee => employee.emp_no!.Value);
        var single = employeesDatabase.Query().Employees
            .Where(employee => employee.emp_no == firstEmployeeNumber)
            .Select(employee => employee.emp_no!.Value);
        var empty = employeesDatabase.Query().Employees
            .Where(employee => employee.emp_no == missingEmployeeNumber)
            .Select(employee => employee.emp_no!.Value);
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, multiple);

        await Assert.That(invocation.Template.Projection).IsTypeOf<QueryPlanProjection.ScalarMember>();
        await Assert.That(multiple.First()).IsEqualTo(firstEmployeeNumber);
        await Assert.That(multiple.FirstOrDefault()).IsEqualTo(firstEmployeeNumber);
        await Assert.That(multiple.Last()).IsEqualTo(lastEmployeeNumber);
        await Assert.That(multiple.LastOrDefault()).IsEqualTo(lastEmployeeNumber);
        await Assert.That(single.Single()).IsEqualTo(firstEmployeeNumber);
        await Assert.That(single.SingleOrDefault()).IsEqualTo(firstEmployeeNumber);

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
    public async Task RootSqlRowTerminals_PreserveSuccessEmptyAndMultipleRowSemantics(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RootSqlRowTerminals_PreserveSuccessEmptyAndMultipleRowSemantics),
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
            .Select(employee => new
            {
                EmployeeNumber = employee.emp_no,
                employee.first_name
            });
        var single = employeesDatabase.Query().Employees
            .Where(employee => employee.emp_no == firstEmployeeNumber)
            .Select(employee => new
            {
                EmployeeNumber = employee.emp_no,
                employee.first_name
            });
        var empty = employeesDatabase.Query().Employees
            .Where(employee => employee.emp_no == missingEmployeeNumber)
            .Select(employee => new
            {
                EmployeeNumber = employee.emp_no,
                employee.first_name
            });
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, multiple);

        await Assert.That(invocation.Template.Projection).IsTypeOf<QueryPlanProjection.SqlRow>();
        await Assert.That(multiple.First().EmployeeNumber).IsEqualTo(firstEmployeeNumber);
        await Assert.That(multiple.FirstOrDefault()!.EmployeeNumber).IsEqualTo(firstEmployeeNumber);
        await Assert.That(multiple.Last().EmployeeNumber).IsEqualTo(lastEmployeeNumber);
        await Assert.That(multiple.LastOrDefault()!.EmployeeNumber).IsEqualTo(lastEmployeeNumber);
        await Assert.That(single.Single().EmployeeNumber).IsEqualTo(firstEmployeeNumber);
        await Assert.That(single.SingleOrDefault()!.EmployeeNumber).IsEqualTo(firstEmployeeNumber);

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
}
