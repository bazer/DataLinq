using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class RawMutationCommandTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RetainedMutationBuildersCreateExecutableParameterizedCallerOwnedCommands(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(descriptor, "raw_mutation_commands");
        using (var transaction = scope.Database.Transaction())
        {
            const string inserted = "literal ' text; --";
            using (var command = transaction.From("runtime_accounts").Set("id", 42).Set("name", inserted).InsertQuery().ToDbCommand())
            {
                await Assert.That(command.Parameters.Count).IsEqualTo(2);
                await Assert.That(command.CommandText).DoesNotContain(inserted);
                await Assert.That(transaction.DatabaseAccess.ExecuteNonQuery(command)).IsEqualTo(1);
            }
            await Assert.That(transaction.DatabaseAccess.ExecuteScalar<string>("SELECT name FROM runtime_accounts WHERE id = 42")).IsEqualTo(inserted);

            var updateQuery = transaction.From("runtime_accounts").Set("name", "updated");
            updateQuery.Where("id").EqualTo(42);
            using (var command = updateQuery.UpdateQuery().ToDbCommand())
            {
                await Assert.That(command.Parameters.Count).IsEqualTo(2);
                await Assert.That(transaction.DatabaseAccess.ExecuteNonQuery(command)).IsEqualTo(1);
            }
            // WhereGroup forwarding also exposes the supported command builders.
            var filtered = transaction.From("runtime_accounts").Where("id").EqualTo(42);
            filtered.Set("name", "forwarded");
            using (var command = filtered.UpdateQuery().ToDbCommand())
                await Assert.That(transaction.DatabaseAccess.ExecuteNonQuery(command)).IsEqualTo(1);
            await Assert.That(transaction.DatabaseAccess.ExecuteScalar<string>("SELECT name FROM runtime_accounts WHERE id = 42")).IsEqualTo("forwarded");

            using (var command = transaction.From("runtime_accounts").Where("id").EqualTo(42).DeleteQuery().ToDbCommand())
            {
                await Assert.That(command.Parameters.Count).IsEqualTo(1);
                await Assert.That(transaction.DatabaseAccess.ExecuteNonQuery(command)).IsEqualTo(1);
                await Assert.That(transaction.DatabaseAccess.ExecuteNonQuery(command)).IsEqualTo(0);
            }
            await Assert.That(Convert.ToInt32(transaction.DatabaseAccess.ExecuteScalar("SELECT COUNT(*) FROM runtime_accounts"))).IsEqualTo(0);
            using (var command = transaction.From("runtime_accounts").Set("id", 43).Set("name", "rolled back").InsertQuery().ToDbCommand())
                await Assert.That(transaction.DatabaseAccess.ExecuteNonQuery(command)).IsEqualTo(1);
            transaction.Rollback();
        }
        await Assert.That(scope.Database.Query().Accounts.Count()).IsEqualTo(0);
    }
}
