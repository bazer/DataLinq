using System;
using System.Data;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class PublicAsyncCommandTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicRawOverloadsShareManagedAndStandaloneOwnershipWithoutCompletingCallers(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicRawOverloadsShareManagedAndStandaloneOwnershipWithoutCompletingCallers), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        foreach (var standaloneMode in new[] { false, true })
        {
            await using var managed = database.Transaction();
            await using var standalone = standaloneMode ? database.Provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite) : null;
            IDatabaseAccess access = standalone ?? managed.DatabaseAccess;
            await access.ExecuteNonQueryAsync("INSERT INTO departments (dept_no, dept_name) VALUES ('w351', 'raw inserted')");
            using var update = Command("UPDATE departments SET dept_name='raw updated' WHERE dept_no='w351'");
            await Assert.That(await access.ExecuteNonQueryAsync(update)).IsEqualTo(1);
            const string countSql = "SELECT COUNT(*) FROM departments WHERE dept_no='w351'";
            using var count = Command(countSql);
            await Assert.That(await access.ExecuteScalarAsync<long>(countSql)).IsEqualTo(1L);
            await Assert.That(await access.ExecuteScalarAsync<long>(count)).IsEqualTo(1L);
            await Assert.That(Convert.ToInt64(await access.ExecuteScalarAsync(countSql))).IsEqualTo(1L);
            await Assert.That(Convert.ToInt64(await access.ExecuteScalarAsync(count))).IsEqualTo(1L);
            foreach (var borrowed in new[] { false, true })
            {
                const string sql = "SELECT 1 UNION ALL SELECT 2";
                using var command = Command(sql);
                await using (var reader = await (borrowed ? access.ExecuteReaderAsync(command) : access.ExecuteReaderAsync(sql)))
                {
                    await Assert.That(await reader.ReadNextRowAsync()).IsTrue();
                    await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
                    await Assert.That(async () => await Commit()).Throws<InvalidOperationException>();
                }
                var rows = borrowed ? access.ReadReaderAsync(command) : access.ReadReaderAsync(sql);
                var sum = 0;
                await foreach (var row in rows)
                {
                    sum += row.GetInt32(0);
                    await Assert.That(() => row.ReadNextRow()).Throws<InvalidOperationException>();
                }
                await Assert.That(sum).IsEqualTo(3);
                await Assert.That(command.CommandText).IsEqualTo(sql);
            }
            await Assert.That(async () => await access.ExecuteScalarAsync(count, new(true))).Throws<OperationCanceledException>();
            await Assert.That(await access.ExecuteScalarAsync<long>(count)).IsEqualTo(1L);
            await Assert.That(managed.Changes).IsEmpty();
            await Commit();
            await Assert.That(await database.Provider.DatabaseAccess.ExecuteScalarAsync<long>(countSql)).IsEqualTo(1L);
            await database.Provider.DatabaseAccess.ExecuteNonQueryAsync("DELETE FROM departments WHERE dept_no='w351'");

            Task Commit() => standaloneMode ? standalone!.CommitAsync() : managed.CommitAsync();
        }

        IDbCommand Command(string sql)
        {
            var command = database.From<Department>().SelectQuery().ToDbCommand();
            command.CommandText = sql;
            return command;
        }
    }
}
