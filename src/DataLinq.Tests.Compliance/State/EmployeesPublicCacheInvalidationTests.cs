using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

public class EmployeesPublicCacheInvalidationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_Clear_RemovesRowsFromAllLoadedTableCaches(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_Clear_RemovesRowsFromAllLoadedTableCaches),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        _ = database.Query().Employees.OrderBy(x => x.emp_no).First();
        _ = database.Query().Departments.OrderBy(x => x.DeptNo).First();

        var employeeCache = GetTableCache<Employee, EmployeesDb>(database);
        var departmentCache = GetTableCache<Department, EmployeesDb>(database);

        await Assert.That(employeeCache.RowCount).IsEqualTo(1);
        await Assert.That(departmentCache.RowCount).IsEqualTo(1);

        database.Cache.Clear();

        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
        await Assert.That(departmentCache.RowCount).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_ClearTable_RemovesOnlySelectedTableRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_ClearTable_RemovesOnlySelectedTableRows),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        _ = database.Query().Employees.OrderBy(x => x.emp_no).First();
        _ = database.Query().Departments.OrderBy(x => x.DeptNo).First();

        var employeeCache = GetTableCache<Employee, EmployeesDb>(database);
        var departmentCache = GetTableCache<Department, EmployeesDb>(database);

        database.Cache.ClearTable<Employee>();

        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
        await Assert.That(departmentCache.RowCount).IsEqualTo(1);

        database.Cache.ClearTable(GetTable<Department, EmployeesDb>(database));

        await Assert.That(departmentCache.RowCount).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateScalarProviderKey_RemovesOneCachedRow(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateScalarProviderKey_RemovesOneCachedRow),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        var cachedEmployee = database.Query().Employees.OrderBy(x => x.emp_no).First();
        var employeeNumber = cachedEmployee.emp_no ?? throw new InvalidOperationException("Seed employee is missing its primary key.");
        var cachedEmployeeAgain = database.Query().Employees.Single(x => x.emp_no == employeeNumber);
        var employeeCache = GetTableCache<Employee, EmployeesDb>(database);

        await Assert.That(ReferenceEquals(cachedEmployee, cachedEmployeeAgain)).IsTrue();
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);

        var invalidated = database.Cache.Invalidate<Employee, int>(employeeNumber);

        await Assert.That(invalidated).IsTrue();
        await Assert.That(employeeCache.RowCount).IsEqualTo(0);

        var reloadedEmployee = database.Query().Employees.Single(x => x.emp_no == employeeNumber);

        await Assert.That(ReferenceEquals(cachedEmployee, reloadedEmployee)).IsFalse();
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateEvent_Row_RemovesCachedRow(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateEvent_Row_RemovesCachedRow),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        var cachedEmployee = database.Query().Employees.OrderBy(x => x.emp_no).First();
        var employeeNumber = cachedEmployee.emp_no ?? throw new InvalidOperationException("Seed employee is missing its primary key.");
        var employeeCache = GetTableCache<Employee, EmployeesDb>(database);

        await Assert.That(employeeCache.RowCount).IsEqualTo(1);

        var result = database.Cache.Invalidate(CacheInvalidationEvent.Row(
            "employees",
            DataLinqKeyComponents.FromValue(employeeNumber),
            changedColumns: [nameof(Employee.first_name)],
            freshnessToken: "employees-100"));

        await Assert.That(result.RowsRemoved).IsEqualTo(1);
        await Assert.That(result.UsedConservativeFallback).IsFalse();
        await Assert.That(result.FreshnessState).IsEqualTo(CacheFreshnessState.ExternallyInvalidated);
        await Assert.That(result.FreshnessToken).IsEqualTo("employees-100");
        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateEvent_Table_ClearsSelectedTable(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateEvent_Table_ClearsSelectedTable),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        _ = database.Query().Employees.OrderBy(x => x.emp_no).First();
        _ = database.Query().Departments.OrderBy(x => x.DeptNo).First();

        var employeeCache = GetTableCache<Employee, EmployeesDb>(database);
        var departmentCache = GetTableCache<Department, EmployeesDb>(database);

        var result = database.Cache.Invalidate(CacheInvalidationEvent.Table("employees"));

        await Assert.That(result.TablesCleared).IsEqualTo(1);
        await Assert.That(result.UsedConservativeFallback).IsTrue();
        await Assert.That(result.FreshnessState).IsEqualTo(CacheFreshnessState.ExternallyInvalidated);
        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
        await Assert.That(departmentCache.RowCount).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateUnknownProviderKey_IsNoOp(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateUnknownProviderKey_IsNoOp),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        _ = database.Query().Employees.OrderBy(x => x.emp_no).First();
        var employeeCache = GetTableCache<Employee, EmployeesDb>(database);

        var invalidated = database.Cache.Invalidate<Employee, int>(int.MaxValue);

        await Assert.That(invalidated).IsFalse();
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateMany_RemovesEachCachedRow(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateMany_RemovesEachCachedRow),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        var employees = database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(2)
            .ToList();
        var keys = employees
            .Select(x => DataLinqKeyComponents.FromValue(x.emp_no ?? throw new InvalidOperationException("Seed employee is missing its primary key.")))
            .ToArray();
        var employeeCache = GetTableCache<Employee, EmployeesDb>(database);

        await Assert.That(employeeCache.RowCount).IsEqualTo(2);

        var rowsRemoved = database.Cache.InvalidateMany<Employee>(keys);

        await Assert.That(rowsRemoved).IsEqualTo(2);
        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateCompositeProviderKeyComponents_RemovesCachedRow(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateCompositeProviderKeyComponents_RemovesCachedRow),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        var cachedDepartmentEmployee = database.Query().DepartmentEmployees
            .OrderBy(x => x.dept_no)
            .ThenBy(x => x.emp_no)
            .First();
        var departmentEmployeeCache = GetTableCache<Dept_emp, EmployeesDb>(database);

        await Assert.That(departmentEmployeeCache.RowCount).IsEqualTo(1);

        var key = DataLinqKeyComponents.FromValues(cachedDepartmentEmployee.dept_no, cachedDepartmentEmployee.emp_no);
        var invalidated = database.Cache.Invalidate<Dept_emp>(key);

        await Assert.That(invalidated).IsTrue();
        await Assert.That(departmentEmployeeCache.RowCount).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateTableDefinition_UsesDynamicProviderKeyComponents(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateTableDefinition_UsesDynamicProviderKeyComponents),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        database.Provider.State.ClearCache();

        var cachedEmployee = database.Query().Employees.OrderBy(x => x.emp_no).First();
        var employeeNumber = cachedEmployee.emp_no ?? throw new InvalidOperationException("Seed employee is missing its primary key.");
        var employeeTable = GetTable<Employee, EmployeesDb>(database);
        var employeeCache = GetTableCache<Employee, EmployeesDb>(database);

        await Assert.That(employeeCache.RowCount).IsEqualTo(1);

        var invalidated = database.Cache.Invalidate(employeeTable, DataLinqKeyComponents.FromValue(employeeNumber));

        await Assert.That(invalidated).IsTrue();
        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateDynamicComponents_RejectsMismatchedArityAndTypes(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateDynamicComponents_RejectsMismatchedArityAndTypes),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;

        await AssertThrows<ArgumentException>(() =>
            database.Cache.Invalidate<Dept_emp>(DataLinqKeyComponents.FromValue("d001")));

        await AssertThrows<ArgumentException>(() =>
            database.Cache.Invalidate<Dept_emp>(DataLinqKeyComponents.FromValues(10001, "d001")));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_InvalidateEvent_RejectsMissingAndMalformedFields(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_InvalidateEvent_RejectsMissingAndMalformedFields),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;

        await AssertThrows<ArgumentException>(() =>
            database.Cache.Invalidate(new CacheInvalidationEvent
            {
                Scope = CacheInvalidationScope.Row,
                ProviderPrimaryKeys = [DataLinqKeyComponents.FromValue(10001)]
            }));

        await AssertThrows<ArgumentException>(() =>
            database.Cache.Invalidate(new CacheInvalidationEvent
            {
                Scope = CacheInvalidationScope.Row,
                TableName = "employees"
            }));

        await AssertThrows<ArgumentException>(() =>
            database.Cache.Invalidate(CacheInvalidationEvent.Row(
                "employees",
                DataLinqKeyComponents.FromValues(10001, "extra"))));

        await AssertThrows<ArgumentException>(() =>
            database.Cache.Invalidate(CacheInvalidationEvent.Row(
                "employees",
                DataLinqKeyComponents.FromValue("wrong-type"))));

        await AssertThrows<ArgumentException>(() =>
            database.Cache.Invalidate(CacheInvalidationEvent.Database(databaseName: "wrong_database")));
    }

    private static TableDefinition GetTable<TModel, TDatabase>(Database<TDatabase> database)
        where TModel : class, IImmutableInstance
        where TDatabase : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<TDatabase>
    {
        return database.Provider.Metadata.GetTableModel(typeof(TModel)).Table;
    }

    private static TableCache GetTableCache<TModel, TDatabase>(Database<TDatabase> database)
        where TModel : class, IImmutableInstance
        where TDatabase : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<TDatabase>
    {
        return database.Provider.GetTableCache(GetTable<TModel, TDatabase>(database));
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
}
