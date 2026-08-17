using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class PreparedQueryTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PreparedQuery_BindsCurrentScalarForEveryExecution(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PreparedQuery_BindsCurrentScalarForEveryExecution),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var employeeNumbers = database.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(2)
            .Select(employee => employee.emp_no!.Value)
            .ToArray();
        var prepared = database.PrepareQuery(
            employeeNumbers[0],
            employeeNumber => database.Query().Employees.Single(employee => employee.emp_no == employeeNumber));

        var first = prepared.Execute(database, employeeNumbers[0]);
        var second = prepared.Execute(database, employeeNumbers[1]);

        await Assert.That(first.emp_no).IsEqualTo(employeeNumbers[0]);
        await Assert.That(second.emp_no).IsEqualTo(employeeNumbers[1]);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PreparedQuery_SnapshotsCurrentLocalSequenceValues(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PreparedQuery_SnapshotsCurrentLocalSequenceValues),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var employeeNumbers = database.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(4)
            .Select(employee => employee.emp_no!.Value)
            .ToArray();
        var prototype = new PreparedInArgument(employeeNumbers[..2]);
        var prepared = database.PrepareQuery(
            prototype,
            argument => database.Query().Employees.Count(employee =>
                argument.EmployeeNumbers.Contains(employee.emp_no!.Value)));

        var firstValues = employeeNumbers[..2];
        var firstCount = prepared.Execute(database, new PreparedInArgument(firstValues));
        firstValues[0] = employeeNumbers[2];
        firstValues[1] = employeeNumbers[3];
        var secondCount = prepared.Execute(database, new PreparedInArgument(firstValues));

        await Assert.That(firstCount).IsEqualTo(2);
        await Assert.That(secondCount).IsEqualTo(2);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PreparedSequenceQuery_SnapshotsBeforeLazyEnumeration(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PreparedSequenceQuery_SnapshotsBeforeLazyEnumeration),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var employeeNumbers = database.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(4)
            .Select(employee => employee.emp_no!.Value)
            .ToArray();
        var prepared = database.PrepareSequenceQuery(
            new PreparedInArgument(employeeNumbers[..2]),
            argument => database.Query().Employees
                .Where(employee => argument.EmployeeNumbers.Contains(employee.emp_no!.Value))
                .OrderBy(employee => employee.emp_no));
        var invocationValues = employeeNumbers[..2];

        var pending = prepared.Execute(database, new PreparedInArgument(invocationValues));
        invocationValues[0] = employeeNumbers[2];
        invocationValues[1] = employeeNumbers[3];
        var actual = pending.Select(employee => employee.emp_no!.Value).ToArray();

        await Assert.That(actual).IsEquivalentTo(employeeNumbers[..2]);
    }

    [Test]
    public async Task PreparedSequenceQuery_BindsProjectedLocalSequence()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PreparedSequenceQuery_BindsProjectedLocalSequence),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var employeeNumbers = database.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(2)
            .Select(employee => employee.emp_no!.Value)
            .ToArray();
        var prototype = new PreparedProjectedInArgument(
            employeeNumbers.Select(number => new LocalEmployeeNumber(number)).ToArray());
        var prepared = database.PrepareSequenceQuery(
            prototype,
            argument => database.Query().Employees.Where(employee =>
                argument.EmployeeNumbers
                    .Select(number => number.Value)
                    .Contains(employee.emp_no!.Value)));

        var actual = prepared.Execute(database, prototype)
            .Select(employee => employee.emp_no!.Value)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(employeeNumbers);
    }

    [Test]
    public async Task PreparedQuery_RejectsClosureCapturedInvocationValues()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PreparedQuery_RejectsClosureCapturedInvocationValues),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var capturedEmployeeNumber = 10001;

        await AssertThrows<QueryTranslationException>(() => database.PrepareQuery(
            capturedEmployeeNumber,
            _ => database.Query().Employees.Any(employee => employee.emp_no == capturedEmployeeNumber)));
    }

    [Test]
    public async Task PreparedQuery_RejectsDifferentSpecializationShape()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PreparedQuery_RejectsDifferentSpecializationShape),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var prepared = database.PrepareQuery(
            new PreparedInArgument([10001, 10002]),
            argument => database.Query().Employees.Any(employee =>
                argument.EmployeeNumbers.Contains(employee.emp_no!.Value)));

        await AssertThrows<QueryPlanInvocationException>(() =>
            prepared.Execute(database, new PreparedInArgument([10001])));
    }

    [Test]
    public async Task PreparedQuery_SupportsConcurrentInvocation()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PreparedQuery_SupportsConcurrentInvocation),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var employeeNumbers = database.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Take(5)
            .Select(employee => employee.emp_no!.Value)
            .ToArray();
        var prepared = database.PrepareQuery(
            employeeNumbers[0],
            employeeNumber => database.Query().Employees.Any(employee => employee.emp_no == employeeNumber));
        var results = new bool[100];

        Parallel.For(0, results.Length, index =>
        {
            results[index] = prepared.Execute(database, employeeNumbers[index % employeeNumbers.Length]);
        });

        await Assert.That(results).DoesNotContain(false);
    }

    [Test]
    public async Task PreparedQuery_ExecutesAgainstTransactionAndChecksCancellation()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PreparedQuery_ExecutesAgainstTransactionAndChecksCancellation),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var employeeNumber = database.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Select(employee => employee.emp_no!.Value)
            .First();
        var prepared = database.PrepareQuery(
            employeeNumber,
            current => database.Query().Employees.Single(employee => employee.emp_no == current));

        using var transaction = database.Transaction();
        var employee = prepared.Execute(transaction, employeeNumber);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(employee.emp_no).IsEqualTo(employeeNumber);
        await AssertThrows<OperationCanceledException>(() =>
            prepared.Execute(transaction, employeeNumber, cancellation.Token));
        transaction.Rollback();
    }

    [Test]
    public async Task PreparedQuery_ExecutesAgainstCompatibleDatabaseInstance()
    {
        using var preparationScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PreparedQuery_ExecutesAgainstCompatibleDatabaseInstance) + "_preparation",
            EmployeesFixtureProfile.TinySeeded);
        using var executionScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PreparedQuery_ExecutesAgainstCompatibleDatabaseInstance) + "_execution",
            EmployeesFixtureProfile.TinySeeded);

        var preparationDatabase = preparationScope.Database;
        var employeeNumber = executionScope.Database.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Select(employee => employee.emp_no!.Value)
            .First();
        var prepared = preparationDatabase.PrepareQuery(
            employeeNumber,
            current => preparationDatabase.Query().Employees.Any(employee => employee.emp_no == current));

        var exists = prepared.Execute(executionScope.Database, employeeNumber);

        await Assert.That(exists).IsTrue();
    }

    [Test]
    [NotInParallel]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PreparedQuery_PreservesOrdinaryWarmSingleTelemetry(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PreparedQuery_PreservesOrdinaryWarmSingleTelemetry),
            EmployeesFixtureProfile.TinySeeded);

        var database = databaseScope.Database;
        var employeeNumber = database.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Select(employee => employee.emp_no!.Value)
            .First();
        var prepared = database.PrepareQuery(
            employeeNumber,
            current => database.Query().Employees.Single(employee => employee.emp_no == current));

        _ = database.Query().Employees.Single(employee => employee.emp_no == employeeNumber);
        DataLinqMetrics.Reset();
        _ = database.Query().Employees.Single(employee => employee.emp_no == employeeNumber);
        var ordinary = DataLinqMetrics.Snapshot();

        DataLinqMetrics.Reset();
        _ = prepared.Execute(database, employeeNumber);
        var preparedSnapshot = DataLinqMetrics.Snapshot();

        await Assert.That(preparedSnapshot.Queries).IsEqualTo(ordinary.Queries);
        await Assert.That(preparedSnapshot.Commands.ReaderExecutions).IsEqualTo(ordinary.Commands.ReaderExecutions);
        await Assert.That(preparedSnapshot.Commands.ScalarExecutions).IsEqualTo(ordinary.Commands.ScalarExecutions);
        await Assert.That(preparedSnapshot.Commands.Failures).IsEqualTo(ordinary.Commands.Failures);
        await Assert.That(preparedSnapshot.RowCache).IsEqualTo(ordinary.RowCache);
        await Assert.That(preparedSnapshot.Relations).IsEqualTo(ordinary.Relations);
    }

    private static async Task AssertThrows<TException>(Action action)
        where TException : Exception
    {
        var threw = false;
        try
        {
            action();
        }
        catch (TException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    private sealed record PreparedInArgument(int[] EmployeeNumbers);

    private sealed record PreparedProjectedInArgument(LocalEmployeeNumber[] EmployeeNumbers);

    private readonly record struct LocalEmployeeNumber(int Value);
}
