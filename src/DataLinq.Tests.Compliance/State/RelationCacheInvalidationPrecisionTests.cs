using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

public class RelationCacheInvalidationPrecisionTests
{
    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_NonRelationColumnUpdate_ClearsOnlyRelationsContainingChangedRow(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_NonRelationColumnUpdate_ClearsOnlyRelationsContainingChangedRow));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var creator = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var secondCreator = databaseScope.Database.Query().Accounts.Single(x => x.Id == 2);
            var creatorInvoices = creator.CreatedInvoices;
            var secondCreatorInvoices = secondCreator.CreatedInvoices;

            await Assert.That(creatorInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
            await Assert.That(secondCreatorInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var beforeUpdate = GetTableMetrics(databaseScope.Database, "runtime_invoices");
            var invoice = creatorInvoices.Single().Mutate();
            invoice.Number = "INV-100-RENAMED";

            using (var transaction = databaseScope.Database.Transaction())
            {
                _ = transaction.Update(invoice);
                transaction.Commit();
            }

            await Assert.That(creatorInvoices.Single().Number).IsEqualTo("INV-100-RENAMED");
            await Assert.That(secondCreatorInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var afterUpdate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            await Assert.That(afterUpdate.Relations.CollectionLoads)
                .IsEqualTo(beforeUpdate.Relations.CollectionLoads + 1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_ForeignKeyMove_ClearsOldAndNewBucketsOnly(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_ForeignKeyMove_ClearsOldAndNewBucketsOnly));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var firstAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var secondAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 2);
            var firstCreatedInvoices = firstAccount.CreatedInvoices;
            var secondCreatedInvoices = secondAccount.CreatedInvoices;
            var firstApprovedInvoices = firstAccount.ApprovedInvoices;

            await Assert.That(firstCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);
            await Assert.That(firstApprovedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var beforeUpdate = GetTableMetrics(databaseScope.Database, "runtime_invoices");
            var invoice = firstCreatedInvoices.Single().Mutate();
            invoice.CreatedByAccountId = 2;

            using (var transaction = databaseScope.Database.Transaction())
            {
                _ = transaction.Update(invoice);
                transaction.Commit();
            }

            await Assert.That(firstCreatedInvoices).IsEmpty();
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).Order().ToArray()).IsEquivalentTo([100, 101]);
            await Assert.That(firstApprovedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var afterUpdate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            await Assert.That(afterUpdate.Relations.CollectionLoads)
                .IsEqualTo(beforeUpdate.Relations.CollectionLoads + 2);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_EventWithOldAndNewRelationValues_ClearsOldAndNewBucketsOnly(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_EventWithOldAndNewRelationValues_ClearsOldAndNewBucketsOnly));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var firstAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var secondAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 2);
            var firstCreatedInvoices = firstAccount.CreatedInvoices;
            var secondCreatedInvoices = secondAccount.CreatedInvoices;
            var firstApprovedInvoices = firstAccount.ApprovedInvoices;

            await Assert.That(firstCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);
            await Assert.That(firstApprovedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var beforeInvalidate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            using (var transaction = databaseScope.Database.Transaction())
            {
                transaction.DatabaseAccess.ExecuteNonQuery(
                    "UPDATE runtime_invoices SET created_by_account_id = 2 WHERE id = 100");
                transaction.Commit();
            }

            var result = databaseScope.Database.Cache.Invalidate(CacheInvalidationEvent.Row(
                "runtime_invoices",
                DataLinqKeyComponents.FromValue(100),
                changedColumns: ["created_by_account_id"],
                changedIndexValues:
                [
                    CacheIndexInvalidation.OldAndNew(
                        "created_by_account_id",
                        DataLinqKeyComponents.FromValue(1),
                        DataLinqKeyComponents.FromValue(2))
                ]));

            await Assert.That(result.UsedConservativeFallback).IsFalse();
            await Assert.That(result.FreshnessState).IsEqualTo(CacheFreshnessState.ExternallyInvalidated);
            await Assert.That(firstCreatedInvoices).IsEmpty();
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).Order().ToArray()).IsEquivalentTo([100, 101]);
            await Assert.That(firstApprovedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var afterInvalidate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            await Assert.That(afterInvalidate.Relations.CollectionLoads)
                .IsEqualTo(beforeInvalidate.Relations.CollectionLoads + 2);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_EventMissingRelationValues_DowngradesToTableWideClear(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_EventMissingRelationValues_DowngradesToTableWideClear));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var firstAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var secondAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 2);
            var firstCreatedInvoices = firstAccount.CreatedInvoices;
            var secondCreatedInvoices = secondAccount.CreatedInvoices;
            var firstApprovedInvoices = firstAccount.ApprovedInvoices;

            await Assert.That(firstCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);
            await Assert.That(firstApprovedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var beforeInvalidate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            using (var transaction = databaseScope.Database.Transaction())
            {
                transaction.DatabaseAccess.ExecuteNonQuery(
                    "UPDATE runtime_invoices SET created_by_account_id = 2 WHERE id = 100");
                transaction.Commit();
            }

            var result = databaseScope.Database.Cache.Invalidate(CacheInvalidationEvent.Row(
                "runtime_invoices",
                DataLinqKeyComponents.FromValue(100),
                changedColumns: ["created_by_account_id"]));

            await Assert.That(result.UsedConservativeFallback).IsTrue();
            await Assert.That(result.FreshnessState).IsEqualTo(CacheFreshnessState.ExternallyInvalidated);
            await Assert.That(firstCreatedInvoices).IsEmpty();
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).Order().ToArray()).IsEquivalentTo([100, 101]);
            await Assert.That(firstApprovedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var afterInvalidate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            await Assert.That(afterInvalidate.Relations.CollectionLoads)
                .IsEqualTo(beforeInvalidate.Relations.CollectionLoads + 3);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_EventRejectsMalformedIndexValues(TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_EventRejectsMalformedIndexValues));

        SeedRelationDatabase(databaseScope.Database);

        await AssertThrows<ArgumentException>(() =>
            databaseScope.Database.Cache.Invalidate(CacheInvalidationEvent.Row(
                "runtime_invoices",
                DataLinqKeyComponents.FromValue(100),
                changedColumns: ["created_by_account_id"],
                changedIndexValues:
                [
                    CacheIndexInvalidation.OldAndNew(
                        "created_by_account_id",
                        DataLinqKeyComponents.FromValue("wrong-type"),
                        DataLinqKeyComponents.FromValue(2))
                ])));

        await AssertThrows<ArgumentException>(() =>
            databaseScope.Database.Cache.Invalidate(CacheInvalidationEvent.Row(
                "runtime_invoices",
                DataLinqKeyComponents.FromValue(100),
                changedColumns: ["created_by_account_id"],
                changedIndexValues:
                [
                    CacheIndexInvalidation.OldAndNew(
                        "created_by_account_id",
                        DataLinqKeyComponents.FromValues(1, 2),
                        DataLinqKeyComponents.FromValue(2))
                ])));
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_DuplicateForeignKeyScalarValue_DoesNotClearDifferentRelationIndex(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_DuplicateForeignKeyScalarValue_DoesNotClearDifferentRelationIndex));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var firstAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var firstCreatedInvoices = firstAccount.CreatedInvoices;
            var firstApprovedInvoices = firstAccount.ApprovedInvoices;

            await Assert.That(firstCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
            await Assert.That(firstApprovedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var beforeUpdate = GetTableMetrics(databaseScope.Database, "runtime_invoices");
            var invoice = firstApprovedInvoices.Single().Mutate();
            invoice.ApprovedByAccountId = 2;

            using (var transaction = databaseScope.Database.Transaction())
            {
                _ = transaction.Update(invoice);
                transaction.Commit();
            }

            await Assert.That(firstCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
            await Assert.That(firstApprovedInvoices).IsEmpty();

            var afterUpdate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            await Assert.That(afterUpdate.Relations.CollectionLoads)
                .IsEqualTo(beforeUpdate.Relations.CollectionLoads + 1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_InsertClearsOnlyRelationBucketThatGainsRow(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_InsertClearsOnlyRelationBucketThatGainsRow));

        try
        {
            SeedAccountsOnly(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var firstAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var secondAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 2);
            var firstCreatedInvoices = firstAccount.CreatedInvoices;
            var secondCreatedInvoices = secondAccount.CreatedInvoices;

            await Assert.That(firstCreatedInvoices).IsEmpty();
            await Assert.That(secondCreatedInvoices).IsEmpty();

            var beforeInsert = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            using (var transaction = databaseScope.Database.Transaction())
            {
                _ = transaction.Insert(CreateInvoice(200, createdByAccountId: 1, approvedByAccountId: 2, "INV-200"));
                transaction.Commit();
            }

            await Assert.That(firstCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([200]);
            await Assert.That(secondCreatedInvoices).IsEmpty();

            var afterInsert = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            await Assert.That(afterInsert.Relations.CollectionLoads)
                .IsEqualTo(beforeInsert.Relations.CollectionLoads + 1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_DeleteClearsOnlyRelationsContainingDeletedRow(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_DeleteClearsOnlyRelationsContainingDeletedRow));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var firstAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var secondAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 2);
            var firstCreatedInvoices = firstAccount.CreatedInvoices;
            var secondCreatedInvoices = secondAccount.CreatedInvoices;

            await Assert.That(firstCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var beforeDelete = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            using (var transaction = databaseScope.Database.Transaction())
            {
                transaction.Delete(firstCreatedInvoices.Single());
                transaction.Commit();
            }

            await Assert.That(firstCreatedInvoices).IsEmpty();
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var afterDelete = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            await Assert.That(afterDelete.Relations.CollectionLoads)
                .IsEqualTo(beforeDelete.Relations.CollectionLoads + 1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_ReferenceRelationClearsWhenReferencedRowChanges(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_ReferenceRelationClearsWhenReferencedRowChanges));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var invoice = databaseScope.Database.Query().Invoices.Single(x => x.Id == 100);
            var createdByAccount = invoice.CreatedByAccount;

            await Assert.That(createdByAccount.Name).IsEqualTo("Creator");

            var beforeUpdate = GetTableMetrics(databaseScope.Database, "runtime_accounts");
            var account = createdByAccount.Mutate();
            account.Name = "Creator Updated";

            using (var transaction = databaseScope.Database.Transaction())
            {
                _ = transaction.Update(account);
                transaction.Commit();
            }

            await Assert.That(invoice.CreatedByAccount.Name).IsEqualTo("Creator Updated");

            var afterUpdate = GetTableMetrics(databaseScope.Database, "runtime_accounts");

            await Assert.That(afterUpdate.Relations.ReferenceLoads)
                .IsEqualTo(beforeUpdate.Relations.ReferenceLoads + 1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_EventParentRowInvalidationClearsReferenceRelation(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_EventParentRowInvalidationClearsReferenceRelation));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var invoice = databaseScope.Database.Query().Invoices.Single(x => x.Id == 100);
            var createdByAccount = invoice.CreatedByAccount;

            await Assert.That(createdByAccount.Name).IsEqualTo("Creator");

            var beforeInvalidate = GetTableMetrics(databaseScope.Database, "runtime_accounts");

            using (var transaction = databaseScope.Database.Transaction())
            {
                transaction.DatabaseAccess.ExecuteNonQuery(
                    "UPDATE runtime_accounts SET name = 'Creator Event' WHERE id = 1");
                transaction.Commit();
            }

            var result = databaseScope.Database.Cache.Invalidate(CacheInvalidationEvent.Row(
                "runtime_accounts",
                DataLinqKeyComponents.FromValue(1),
                changedColumns: ["name"]));

            await Assert.That(result.UsedConservativeFallback).IsFalse();
            await Assert.That(result.FreshnessState).IsEqualTo(CacheFreshnessState.ExternallyInvalidated);
            await Assert.That(invoice.CreatedByAccount.Name).IsEqualTo("Creator Event");

            var afterInvalidate = GetTableMetrics(databaseScope.Database, "runtime_accounts");

            await Assert.That(afterInvalidate.Relations.ReferenceLoads)
                .IsEqualTo(beforeInvalidate.Relations.ReferenceLoads + 1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_ManualChildInvalidationFallsBackToTableWideClear(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_ManualChildInvalidationFallsBackToTableWideClear));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var firstAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 1);
            var secondAccount = databaseScope.Database.Query().Accounts.Single(x => x.Id == 2);
            var firstCreatedInvoices = firstAccount.CreatedInvoices;
            var secondCreatedInvoices = secondAccount.CreatedInvoices;

            await Assert.That(firstCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([100]);
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var beforeInvalidate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            using (var transaction = databaseScope.Database.Transaction())
            {
                transaction.DatabaseAccess.ExecuteNonQuery(
                    "UPDATE runtime_invoices SET number = 'INV-100-EXTERNAL' WHERE id = 100");
                transaction.Commit();
            }

            _ = databaseScope.Database.Cache.Invalidate<RuntimeInvoice, int>(100);

            await Assert.That(firstCreatedInvoices.Single().Number).IsEqualTo("INV-100-EXTERNAL");
            await Assert.That(secondCreatedInvoices.Select(x => x.Id).ToArray()).IsEquivalentTo([101]);

            var afterInvalidate = GetTableMetrics(databaseScope.Database, "runtime_invoices");

            await Assert.That(afterInvalidate.Relations.CollectionLoads)
                .IsEqualTo(beforeInvalidate.Relations.CollectionLoads + 2);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCache_ManualParentInvalidationClearsReferenceRelation(TestProviderDescriptor provider)
    {
        DataLinqMetrics.Reset();

        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(RelationCache_ManualParentInvalidationClearsReferenceRelation));

        try
        {
            SeedRelationDatabase(databaseScope.Database);
            databaseScope.Database.Provider.State.ClearCache();
            DataLinqMetrics.Reset();

            var invoice = databaseScope.Database.Query().Invoices.Single(x => x.Id == 100);
            var createdByAccount = invoice.CreatedByAccount;

            await Assert.That(createdByAccount.Name).IsEqualTo("Creator");

            var beforeInvalidate = GetTableMetrics(databaseScope.Database, "runtime_accounts");

            using (var transaction = databaseScope.Database.Transaction())
            {
                transaction.DatabaseAccess.ExecuteNonQuery(
                    "UPDATE runtime_accounts SET name = 'Creator External' WHERE id = 1");
                transaction.Commit();
            }

            _ = databaseScope.Database.Cache.Invalidate<RuntimeAccount, int>(1);

            await Assert.That(invoice.CreatedByAccount.Name).IsEqualTo("Creator External");

            var afterInvalidate = GetTableMetrics(databaseScope.Database, "runtime_accounts");

            await Assert.That(afterInvalidate.Relations.ReferenceLoads)
                .IsEqualTo(beforeInvalidate.Relations.ReferenceLoads + 1);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    private static Mutable<RuntimeInvoice> CreateInvoice(
        int id,
        int createdByAccountId,
        int approvedByAccountId,
        string number)
    {
        var invoice = new Mutable<RuntimeInvoice>();
        invoice[nameof(RuntimeInvoice.Id)] = id;
        invoice[nameof(RuntimeInvoice.CreatedByAccountId)] = createdByAccountId;
        invoice[nameof(RuntimeInvoice.ApprovedByAccountId)] = approvedByAccountId;
        invoice[nameof(RuntimeInvoice.Number)] = number;
        return invoice;
    }

    private static void SeedAccountsOnly(Database<MultipleForeignKeyRelationDb> database)
    {
        using var transaction = database.Transaction();

        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_accounts (id, name) VALUES (1, 'Creator')");
        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_accounts (id, name) VALUES (2, 'Approver')");

        transaction.Commit();
    }

    private static void SeedRelationDatabase(Database<MultipleForeignKeyRelationDb> database)
    {
        SeedAccountsOnly(database);

        using var transaction = database.Transaction();

        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_invoices (id, created_by_account_id, approved_by_account_id, number) VALUES (100, 1, 2, 'INV-100')");
        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_invoices (id, created_by_account_id, approved_by_account_id, number) VALUES (101, 2, 1, 'INV-101')");

        transaction.Commit();
    }

    private static DataLinqTableMetricsSnapshot GetTableMetrics<TDatabase>(
        Database<TDatabase> database,
        string tableName)
        where TDatabase : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<TDatabase>
    {
        return DataLinqMetrics.Snapshot()
            .Providers
            .Single(x => x.ProviderInstanceId == database.Provider.TelemetryInstanceId)
            .Tables
            .Single(x => x.TableName == tableName);
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
