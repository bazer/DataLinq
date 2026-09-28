using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class PublicAsyncFluentTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task FluentAsyncReadsPreserveRowsKeysModelsAndCapturedSelections(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(FluentAsyncReadsPreserveRowsKeysModelsAndCapturedSelections), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var query = database.From<Department>().OrderBy("dept_no").Limit(2);
        var select = query.SelectQuery();
        var expected = query.Select().ToArray();
        var sql = select.ToSql().Text;
        var models = await select.ExecuteAsAsync<Department>().ToArrayAsync();
        await Assert.That(models.Select(row => row.DeptNo).ToArray()).IsEquivalentTo(expected.Select(row => row.DeptNo).ToArray());
        await Assert.That((await select.ExecuteAsync().ToArrayAsync()).Length).IsEqualTo(expected.Length);
        await Assert.That((await query.SelectAsync().ToArrayAsync()).Length).IsEqualTo(expected.Length);
        await Assert.That(select.ToSql().Text).IsEqualTo(sql);
        var rows = await select.ReadRowsAsync().ToArrayAsync();
        var keyColumn = database.Provider.Metadata.GetTableModel(typeof(Department)).Table.GetColumnByDbName("dept_no");
        await Assert.That(rows[0][keyColumn]).IsEqualTo(expected[0].DeptNo);
        await Assert.That((await select.ReadFirstRowAsync())![keyColumn]).IsEqualTo(expected[0].DeptNo);
        var keys = await select.ReadKeysAsync().ToArrayAsync();
        await Assert.That(keys[0]).IsEqualTo(expected[0].PrimaryKeys());
        var grouped = await select.ReadPrimaryAndForeignKeysAsync(new ColumnIndex("group", IndexCharacteristic.Simple, IndexType.BTREE, [keyColumn])).ToArrayAsync();
        await Assert.That(grouped[0].fk).IsEqualTo(keys[0]);
        await Assert.That(grouped[0].pks.Single()).IsEqualTo(keys[0]);
        var scalar = database.From<Department>().SelectQuery().What("COUNT(*)");
        await Assert.That(await scalar.ExecuteScalarAsync<long>()).IsEqualTo(scalar.ExecuteScalar<long>());
        await Assert.That(await scalar.ExecuteScalarAsync()).IsEqualTo(scalar.ExecuteScalar());

        await using var transaction = database.Transaction();
        await using var cursors = transaction.From<Department>().OrderBy("dept_no").SelectQuery().ReadReaderAsync().GetAsyncEnumerator();
        await Assert.That(await cursors.MoveNextAsync()).IsTrue();
        var cursor = cursors.Current;
        await Assert.That(cursor.GetString(cursor.GetOrdinal("dept_no"))).IsEqualTo(expected[0].DeptNo);
        await Assert.That(() => cursor.ReadNextRow()).Throws<InvalidOperationException>();
        await Assert.That(async () => await cursor.DisposeAsync()).Throws<InvalidOperationException>();
        await Assert.That(async () => await transaction.CommitAsync()).Throws<InvalidOperationException>();
        await cursors.DisposeAsync();
        await transaction.RollbackAsync();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RawModelAsyncReadsBorrowCommandsAndRespectManagedOwnership(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(RawModelAsyncReadsBorrowCommandsAndRespectManagedOwnership), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        DataSourceAccess source = database.Provider.ReadOnlyAccess;
        const string sql = "SELECT dept_name, dept_no FROM departments ORDER BY dept_no";
        var expected = source.GetFromQuery<Department>(sql).ToArray();
        var rows = await source.GetFromQueryAsync<Department>(query: sql).ToArrayAsync();
        await Assert.That(rows.Select(row => row.DeptNo).ToArray()).IsEquivalentTo(expected.Select(row => row.DeptNo).ToArray());
        using var command = database.From<Department>().SelectQuery().ToDbCommand();
        var commandText = command.CommandText;
        var first = await source.GetFromCommandAsync<Department>(dbCommand: command).ToArrayAsync();
        var second = await source.GetFromCommandAsync<Department>(command).ToArrayAsync();
        await Assert.That(first.Length).IsEqualTo(second.Length);
        await Assert.That(command.CommandText).IsEqualTo(commandText);
        await using var transaction = database.Transaction();
        var transactional = await transaction.GetFromQueryAsync<Department>(sql).ToArrayAsync();
        await Assert.That(transactional.Length).IsEqualTo(expected.Length);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(async () => await transaction.GetFromQueryAsync<Department>(sql, new(true)).ToArrayAsync())
            .Throws<OperationCanceledException>();
        await transaction.CommitAsync();
    }
}
