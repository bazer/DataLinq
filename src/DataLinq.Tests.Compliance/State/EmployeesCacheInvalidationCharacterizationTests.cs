using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

public class EmployeesCacheInvalidationCharacterizationTests
{
    private readonly EmployeesTestData employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_UpdateCommit_InvalidatesReadOnlyRowCache(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_UpdateCommit_InvalidatesReadOnlyRowCache),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = employees.GetOrCreateEmployee(999970, employeesDatabase);
        var employeeNumber = employee.emp_no;
        var employeeCache = GetTableCache<Employee, EmployeesDb>(employeesDatabase);

        employeesDatabase.Provider.State.ClearCache();

        var cachedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);
        var cachedEmployeeAgain = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);
        await Assert.That(ReferenceEquals(cachedEmployee, cachedEmployeeAgain)).IsTrue();
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);
        await Assert.That(employeeCache.TotalBytes).IsGreaterThan(0);

        var newBirthDate = cachedEmployee.birth_date.AddDays(1);
        var mutable = cachedEmployee.Mutate();
        mutable.birth_date = newBirthDate;

        using (var transaction = employeesDatabase.Transaction())
        {
            var updatedEmployee = transaction.Update(mutable);
            await Assert.That(updatedEmployee.birth_date).IsEqualTo(newBirthDate);
            transaction.Commit();
        }

        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
        await Assert.That(employeeCache.TotalBytes).IsEqualTo(0);

        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);

        await Assert.That(persistedEmployee.birth_date).IsEqualTo(newBirthDate);
        await Assert.That(ReferenceEquals(cachedEmployee, persistedEmployee)).IsFalse();
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_DeleteCommit_RemovesReadOnlyRowCacheEntry(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_DeleteCommit_RemovesReadOnlyRowCacheEntry),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = employees.GetOrCreateEmployee(999971, employeesDatabase);
        var employeeNumber = employee.emp_no;
        var employeeCache = GetTableCache<Employee, EmployeesDb>(employeesDatabase);

        employeesDatabase.Provider.State.ClearCache();

        var cachedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);

        using (var transaction = employeesDatabase.Transaction())
        {
            transaction.Delete(cachedEmployee);
            transaction.Commit();
        }

        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
        await Assert.That(employeesDatabase.Query().Employees.Any(x => x.emp_no == employeeNumber)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_NonPrimaryKeyMaterialization_UsesProviderKeyCacheThroughUpdateAndDelete(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_NonPrimaryKeyMaterialization_UsesProviderKeyCacheThroughUpdateAndDelete),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = employees.GetOrCreateEmployee(999975, employeesDatabase);
        var employeeNumber = employee.emp_no;
        var firstName = employee.first_name;
        var employeeCache = GetTableCache<Employee, EmployeesDb>(employeesDatabase);

        employeesDatabase.Provider.State.ClearCache();

        var cachedEmployee = employeesDatabase.Query().Employees.Single(x => x.first_name == firstName && x.emp_no == employeeNumber);
        var cachedEmployeeAgain = employeesDatabase.Query().Employees.Single(x => x.first_name == firstName && x.emp_no == employeeNumber);

        await Assert.That(ReferenceEquals(cachedEmployee, cachedEmployeeAgain)).IsTrue();
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);

        var newBirthDate = cachedEmployee.birth_date.AddDays(1);
        var mutable = cachedEmployee.Mutate();
        mutable.birth_date = newBirthDate;

        using (var transaction = employeesDatabase.Transaction())
        {
            _ = transaction.Update(mutable);
            transaction.Commit();
        }

        await Assert.That(employeeCache.RowCount).IsEqualTo(0);

        var reloadedEmployee = employeesDatabase.Query().Employees.Single(x => x.first_name == firstName && x.emp_no == employeeNumber);

        await Assert.That(reloadedEmployee.birth_date).IsEqualTo(newBirthDate);
        await Assert.That(ReferenceEquals(cachedEmployee, reloadedEmployee)).IsFalse();
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);

        using (var transaction = employeesDatabase.Transaction())
        {
            transaction.Delete(reloadedEmployee);
            transaction.Commit();
        }

        await Assert.That(employeeCache.RowCount).IsEqualTo(0);
        await Assert.That(employeesDatabase.Query().Employees.Any(x => x.first_name == firstName && x.emp_no == employeeNumber)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_UpdateBeforeCommit_UsesTransactionLocalRowCache(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_UpdateBeforeCommit_UsesTransactionLocalRowCache),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = employees.GetOrCreateEmployee(999974, employeesDatabase);
        var employeeNumber = employee.emp_no;
        var employeeCache = GetTableCache<Employee, EmployeesDb>(employeesDatabase);

        employeesDatabase.Provider.State.ClearCache();

        using var transaction = employeesDatabase.Transaction();
        var transactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employeeNumber);
        var transactionEmployeeAgain = transaction.Query().Employees.Single(x => x.emp_no == employeeNumber);

        await Assert.That(ReferenceEquals(transactionEmployee, transactionEmployeeAgain)).IsTrue();
        await Assert.That(employeeCache.TransactionRowsCount).IsEqualTo(1);
        await Assert.That(employeeCache.GetTransactionRows(transaction).Count()).IsEqualTo(1);

        var newBirthDate = transactionEmployee.birth_date.AddDays(1);
        var mutable = transactionEmployee.Mutate();
        mutable.birth_date = newBirthDate;

        var updatedEmployee = transaction.Update(mutable);
        var transactionEmployeeAfterUpdate = transaction.Query().Employees.Single(x => x.emp_no == employeeNumber);

        await Assert.That(ReferenceEquals(updatedEmployee, transactionEmployeeAfterUpdate)).IsTrue();
        await Assert.That(transactionEmployeeAfterUpdate.birth_date).IsEqualTo(newBirthDate);
        await Assert.That(employeeCache.TransactionRowsCount).IsEqualTo(1);
        await Assert.That(employeeCache.GetTransactionRows(transaction).Count()).IsEqualTo(1);

        transaction.Rollback();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_Rollback_DoesNotInvalidateReadOnlyRowCacheForUncommittedMutation(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_Rollback_DoesNotInvalidateReadOnlyRowCacheForUncommittedMutation),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = employees.GetOrCreateEmployee(999972, employeesDatabase);
        var employeeNumber = employee.emp_no;
        var employeeCache = GetTableCache<Employee, EmployeesDb>(employeesDatabase);

        employeesDatabase.Provider.State.ClearCache();

        var cachedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);
        var originalBirthDate = cachedEmployee.birth_date;
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);

        var mutable = cachedEmployee.Mutate();
        mutable.birth_date = originalBirthDate.AddDays(1);

        using (var transaction = employeesDatabase.Transaction())
        {
            _ = transaction.Update(mutable);

            await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
            await Assert.That(employeeCache.RowCount).IsEqualTo(1);

            transaction.Rollback();
        }

        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);

        await Assert.That(persistedEmployee.birth_date).IsEqualTo(originalBirthDate);
        await Assert.That(ReferenceEquals(cachedEmployee, persistedEmployee)).IsTrue();
        await Assert.That(employeeCache.RowCount).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_ForeignKeyUpdate_ClearsExistingRelationCollections(TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(Cache_ForeignKeyUpdate_ClearsExistingRelationCollections));

        SeedRelationDatabase(databaseScope.Database);
        databaseScope.Database.Provider.State.ClearCache();

        var creator = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
        var approver = databaseScope.Database.Query().Accounts.Single(x => x.Id == 2);
        var creatorInvoices = creator.CreatedInvoices;
        var approverInvoices = approver.CreatedInvoices;

        await Assert.That(creatorInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
        await Assert.That(approverInvoices).IsEmpty();

        var invoice = creatorInvoices.Single().Mutate();
        invoice.CreatedByAccountId = 2;

        using (var transaction = databaseScope.Database.Transaction())
        {
            _ = transaction.Update(invoice);
            transaction.Commit();
        }

        await Assert.That(creatorInvoices).IsEmpty();
        await Assert.That(approverInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_StateChange_NotifiesSubscribersForChangedTable(TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(Cache_StateChange_NotifiesSubscribersForChangedTable));

        SeedRelationDatabase(databaseScope.Database);
        databaseScope.Database.Provider.State.ClearCache();

        var invoice = databaseScope.Database.Query().Invoices.Single(x => x.Id == 100);
        var invoiceCache = GetTableCache<RuntimeInvoice, MultipleForeignKeyRelationDb>(databaseScope.Database);
        var subscriber = new CountingCacheNotification();
        invoiceCache.SubscribeToChanges(subscriber);

        var mutable = invoice.Mutate();
        mutable.Number = "INV-100-SUB";

        using (var transaction = databaseScope.Database.Transaction())
        {
            _ = transaction.Update(mutable);
            await Assert.That(subscriber.ClearCount).IsEqualTo(0);
            transaction.Commit();
        }

        await Assert.That(subscriber.ClearCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_UnchangedForeignKeyUpdate_ClearsRelationCollectionsContainingChangedRows(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(Cache_UnchangedForeignKeyUpdate_ClearsRelationCollectionsContainingChangedRows));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var creator = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var creatorInvoices = creator.CreatedInvoices;

            await Assert.That(creatorInvoices.Count).IsEqualTo(1);
            await Assert.That(creatorInvoices.Count).IsEqualTo(1);

            var beforeUpdateMetrics = GetTableMetrics(
                databaseScope.Database,
                "runtime_invoices");

            await Assert.That(beforeUpdateMetrics.Relations.CollectionLoads).IsEqualTo(1);
            await Assert.That(beforeUpdateMetrics.Relations.CollectionCacheHits).IsEqualTo(1);

            var invoice = creatorInvoices.Single().Mutate();
            invoice.Number = "INV-100-RENAMED";

            using (var transaction = databaseScope.Database.Transaction())
            {
                _ = transaction.Update(invoice);
                transaction.Commit();
            }

            await Assert.That(creatorInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);

            var afterUpdateMetrics = GetTableMetrics(
                databaseScope.Database,
                "runtime_invoices");

            await Assert.That(afterUpdateMetrics.Relations.CollectionLoads)
                .IsEqualTo(beforeUpdateMetrics.Relations.CollectionLoads + 1);
            await Assert.That(afterUpdateMetrics.Relations.CollectionCacheHits)
                .IsGreaterThanOrEqualTo(beforeUpdateMetrics.Relations.CollectionCacheHits);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_StateChangeInvalidation_RecordsMaintenanceTelemetry(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Cache_StateChangeInvalidation_RecordsMaintenanceTelemetry),
            EmployeesSeedMode.Bogus);

        try
        {
            var measurements = new List<(string InstrumentName, long Value, Dictionary<string, object?> Tags)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "DataLinq")
                    meterListener.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                measurements.Add((instrument.Name, measurement, ToTagDictionary(tags)));
            });
            listener.Start();

            var employee = employees.GetOrCreateEmployee(999973, databaseScope.Database);
            var employeeNumber = employee.emp_no;
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();
            measurements.Clear();

            var cachedEmployee = databaseScope.Database.Query().Employees.Single(x => x.emp_no == employeeNumber);
            var mutable = cachedEmployee.Mutate();
            mutable.birth_date = cachedEmployee.birth_date.AddDays(1);

            using (var transaction = databaseScope.Database.Transaction())
            {
                _ = transaction.Update(mutable);
                transaction.Commit();
            }

            var preciseMaintenanceOperations = measurements
                .Where(x =>
                    x.InstrumentName == "datalinq.cache.maintenance.operations" &&
                    HasTag(x.Tags, "datalinq.table", "employees") &&
                    HasTag(x.Tags, "datalinq.cache.operation", "state_change_precise"))
                .Sum(x => x.Value);

            var tableMaintenanceOperations = measurements
                .Where(x =>
                    x.InstrumentName == "datalinq.cache.maintenance.operations" &&
                    HasTag(x.Tags, "datalinq.table", "employees") &&
                    HasTag(x.Tags, "datalinq.cache.operation", "state_change_table"))
                .Sum(x => x.Value);

            var rowsRemoved = measurements
                .Where(x =>
                    x.InstrumentName == "datalinq.cache.rows.removed" &&
                    HasTag(x.Tags, "datalinq.table", "employees") &&
                    HasTag(x.Tags, "datalinq.cache.operation", "state_change_precise"))
                .Sum(x => x.Value);

            var tableMetrics = GetTableMetrics(databaseScope.Database, "employees");

            await Assert.That(preciseMaintenanceOperations).IsGreaterThanOrEqualTo(1L);
            await Assert.That(tableMaintenanceOperations).IsGreaterThanOrEqualTo(1L);
            await Assert.That(rowsRemoved).IsGreaterThanOrEqualTo(1L);
            await Assert.That(tableMetrics.Cleanup.Operations).IsGreaterThanOrEqualTo(2L);
            await Assert.That(tableMetrics.Cleanup.RowsRemoved).IsGreaterThanOrEqualTo(1L);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    private static void SeedRelationDatabase(Database<MultipleForeignKeyRelationDb> database)
    {
        using var transaction = database.Transaction();

        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_accounts (id, name) VALUES (1, 'Creator')");
        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_accounts (id, name) VALUES (2, 'Approver')");
        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_invoices (id, created_by_account_id, approved_by_account_id, number) VALUES (100, 1, 2, 'INV-100')");

        transaction.Commit();
    }

    private static TableCache GetTableCache<TModel, TDatabase>(Database<TDatabase> database)
        where TModel : class, IImmutableInstance
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var table = database.Provider.Metadata.GetTableModel(typeof(TModel)).Table;
        return database.Provider.GetTableCache(table);
    }

    private static DataLinqTableMetricsSnapshot GetTableMetrics<TDatabase>(
        Database<TDatabase> database,
        string tableName)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        return DataLinqMetrics.Snapshot()
            .Providers
            .Single(x => x.ProviderInstanceId == database.Provider.TelemetryInstanceId)
            .Tables
            .Single(x => x.TableName == tableName);
    }

    private static bool HasTag(Dictionary<string, object?> tags, string key, object? value)
        => tags.TryGetValue(key, out var actual) && Equals(actual, value);

    private static Dictionary<string, object?> ToTagDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
            dictionary[tag.Key] = tag.Value;

        return dictionary;
    }

    private sealed class CountingCacheNotification : ICacheNotification
    {
        private int clearCount;

        public int ClearCount => clearCount;

        public void Clear()
        {
            Interlocked.Increment(ref clearCount);
        }
    }
}
