using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

[ParallelLimiter<EmployeesTransactionLifecycleParallelLimit>]
public sealed class EmployeesLocalProjectionTransactionTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RetainedLocalProjections_ReadUncommittedValuesFromTransactionRoot(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(RetainedLocalProjections_ReadUncommittedValuesFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        var link = database.Query().DepartmentEmployees
            .OrderBy(row => row.emp_no)
            .ThenBy(row => row.dept_no)
            .First();
        var employeeNumber = link.emp_no;
        var departmentNumber = link.dept_no;
        var employee = database.Query().Employees.Single(row => row.emp_no == employeeNumber);
        var department = database.Query().Departments.Single(row => row.DeptNo == departmentNumber);
        var originalFirstName = employee.first_name;
        var originalDepartmentName = department.Name;
        var updatedFirstName = $"Txn{employeeNumber}";
        var updatedDepartmentName = $"Txn-{departmentNumber}";
        var mutableEmployee = employee.Mutate();
        var mutableDepartment = department.Mutate();

        mutableEmployee.first_name = updatedFirstName;
        mutableDepartment.Name = updatedDepartmentName;

        using var transaction = database.Transaction();
        _ = transaction.Update(mutableEmployee);
        _ = transaction.Update(mutableDepartment);

        var computedQuery = transaction.Query().Employees
            .Where(row => row.emp_no == employeeNumber)
            .Select(row => row.first_name.Trim() + ":" + row.emp_no!.Value);
        var anonymousQuery = transaction.Query().Employees
            .Where(row => row.emp_no == employeeNumber)
            .Select(row => new TransactionLocalBox(row.first_name.Trim()));
        var joinedQuery = transaction.Query().DepartmentEmployees
            .Where(row => row.emp_no == employeeNumber && row.dept_no == departmentNumber)
            .Select(row => row.departments.Name.Trim() + ":" + row.emp_no);

        var computedInvocation = ExpressionQueryPlanParser.Convert(database, computedQuery);
        var anonymousInvocation = ExpressionQueryPlanParser.Convert(database, anonymousQuery);
        var joinedInvocation = ExpressionQueryPlanParser.Convert(database, joinedQuery);
        var computed = computedQuery.Single();
        var anonymous = anonymousQuery.ToArray();
        var joined = joinedQuery.Single();

        await Assert.That(computedInvocation.Template.Projection)
            .IsTypeOf<QueryPlanProjection.ComputedRowLocal>();
        await Assert.That(anonymousInvocation.Template.Projection)
            .IsTypeOf<QueryPlanProjection.Anonymous>();
        await Assert.That(joinedInvocation.Template.Projection)
            .IsTypeOf<QueryPlanProjection.JoinedRowLocal>();
        await Assert.That(joinedInvocation.Template.Sources.Any(source => source.Kind == QueryPlanSourceKind.ImplicitJoin))
            .IsTrue();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(computed).IsEqualTo($"{updatedFirstName}:{employeeNumber}");
        await Assert.That(anonymous.Length).IsEqualTo(1);
        await Assert.That(anonymous[0].Value).IsEqualTo(updatedFirstName);
        await Assert.That(joined).IsEqualTo($"{updatedDepartmentName}:{employeeNumber}");
        await Assert.That(computed).IsNotEqualTo($"{originalFirstName.Trim()}:{employeeNumber}");
        await Assert.That(joined).IsNotEqualTo($"{originalDepartmentName.Trim()}:{employeeNumber}");

        transaction.Rollback();
        database.Provider.State.ClearCache();

        var restoredEmployee = database.Query().Employees.Single(row => row.emp_no == employeeNumber);
        var restoredDepartment = database.Query().Departments.Single(row => row.DeptNo == departmentNumber);

        await Assert.That(restoredEmployee.first_name).IsEqualTo(originalFirstName);
        await Assert.That(restoredDepartment.Name).IsEqualTo(originalDepartmentName);
    }

    private sealed record TransactionLocalBox(string Value);
}
