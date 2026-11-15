using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using DataLinq.Tests.Unit.Core;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit;

public sealed class SqliteTestDatabaseTests
{
    [Test]
    public async Task ParallelFixturesOwnDifferentStoresAndReleaseTheLastConnection()
    {
        var fixtures = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(SqliteTestDatabase<EmployeesDb>.Create)));
        var connections = fixtures.Select(fixture => fixture.Database.Provider.ConnectionString).ToArray();
        try
        {
            await Assert.That(connections.Distinct().Count()).IsEqualTo(fixtures.Length);
            foreach (var fixture in fixtures)
            {
                await Assert.That(fixture.Query().Employees.Any()).IsFalse();
                fixture.Seed(s => { s.Insert(Employee()); });
                using var connection = new SqliteConnection(fixture.Database.Provider.ConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA foreign_keys";
                await Assert.That((long)command.ExecuteScalar()!).IsEqualTo(1L);
                command.CommandText = "SELECT COUNT(*) FROM employees";
                await Assert.That((long)command.ExecuteScalar()!).IsEqualTo(1L);
            }
        }
        finally
        {
            foreach (var fixture in fixtures) await fixture.DisposeAsync();
        }
        foreach (var identity in connections)
        {
            using var reopened = new SqliteConnection(identity);
            reopened.Open();
            using var command = reopened.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master";
            await Assert.That((long)command.ExecuteScalar()!).IsEqualTo(0L);
        }
    }

    [Test]
    public async Task RelatedSeedsReturnGeneratedKeysAndRollBackAllWritesOnDependentFailure()
    {
        await using var fixture = await SqliteTestDatabase<EmployeesDb>.CreateAsync();
        var (employee, salary) = await fixture.SeedAsync(async (s, ct) =>
        {
            var row = await s.InsertAsync(Employee(), ct);
            return (row, await s.InsertAsync(Salary(row.emp_no!.Value), ct));
        });
        var persisted = fixture.Query().Employees.Single();
        await Assert.That(persisted.emp_no).IsEqualTo(employee.emp_no);
        await Assert.That(persisted.salaries.Single().FromDate).IsEqualTo(salary.FromDate);
        await Assert.That(salary.employees.emp_no).IsEqualTo(employee.emp_no);

        await Assert.That(async () => await fixture.SeedAsync(async (s, ct) =>
        {
            await s.InsertAsync(Employee(), ct);
            await s.InsertAsync(Salary(-1), ct);
        })).Throws<SqliteException>();
        fixture.Database.Provider.State.ClearCache();
        await Assert.That(fixture.Query().Employees.Single().emp_no).IsEqualTo(employee.emp_no);
    }

    [Test]
    public async Task SeedRejectsAsyncDelegatesBeforeTheirFirstWrite()
    {
        using var fixture = SqliteTestDatabase<EmployeesDb>.Create();
        var calls = 0;
        Task Callback(IDataLinqSession<EmployeesDb> s) { calls++; return Task.CompletedTask; }
        Task<int> Result(IDataLinqSession<EmployeesDb> s) { calls++; return Task.FromResult(1); }
        ValueTask Value(IDataLinqSession<EmployeesDb> s) { calls++; return default; }
        ValueTask<int> ValueResult(IDataLinqSession<EmployeesDb> s) { calls++; return ValueTask.FromResult(1); }
#pragma warning disable TUnit0031 // Deliberate invalid input. Seed must reject this delegate without invoking it.
        Action<IDataLinqSession<EmployeesDb>> asyncVoid = async s => { calls++; await Task.Yield(); };
#pragma warning restore TUnit0031
        Action<IDataLinqSession<EmployeesDb>> multicast = s => calls++;
        multicast += asyncVoid;

        Action[] attempts = [
            () => { fixture.Seed(Callback); }, () => { fixture.Seed(Result); },
            () => { fixture.Seed(Value); }, () => { fixture.Seed(ValueResult); },
            () => { fixture.Seed<Task>(Callback); }, () => { fixture.Seed<Task<int>>(Result); },
            () => { fixture.Seed<ValueTask>(Value); }, () => { fixture.Seed<ValueTask<int>>(ValueResult); },
            () => { fixture.Seed<object>(Result); }, () => fixture.Seed(multicast),
            () => { fixture.Seed(async s => { calls++; await Task.Yield(); }); }
        ];
        foreach (var attempt in attempts)
        {
            var failure = await Assert.That(() => attempt()).Throws<ArgumentException>();
            await Assert.That(failure!.Message).Contains("SeedAsync");
        }
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncSeedRejectsMulticastDelegatesBeforeAnyCallbackRuns()
    {
        using var fixture = SqliteTestDatabase<EmployeesDb>.Create();
        var calls = 0;
        Task<int> Step(IDataLinqSession<EmployeesDb> session, CancellationToken token) => Task.FromResult(++calls);
        Func<IDataLinqSession<EmployeesDb>, CancellationToken, Task> action = Step;
        action += Step;
        Func<IDataLinqSession<EmployeesDb>, CancellationToken, Task<int>> result = Step;
        result += Step;

        await Assert.That(async () => await fixture.SeedAsync(action)).Throws<ArgumentException>();
        await Assert.That(async () => { await fixture.SeedAsync(result); }).Throws<ArgumentException>();
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncSeedWaitsForTheWholeCallbackBeforeReturningItsResult()
    {
        using var fixture = SqliteTestDatabase<EmployeesDb>.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> InsertPair(IDataLinqSession<EmployeesDb> session)
        {
            session.Insert(Employee());
            entered.SetResult();
            await release.Task;
            session.Insert(Employee());
            return session.Query().Employees.Count();
        }

        await Assert.That(() => { fixture.Seed(InsertPair); }).Throws<ArgumentException>();
        await Assert.That(entered.Task.IsCompleted).IsFalse();
        await Assert.That(fixture.Query().Employees.Any()).IsFalse();
        var pending = fixture.SeedAsync((session, _) => InsertPair(session));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(pending.IsCompleted).IsFalse();
        }
        finally { release.TrySetResult(); }
        await Assert.That(await pending).IsEqualTo(2);
        await Assert.That(fixture.Database.Provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM employees")).IsEqualTo(2L);
    }

    [Test]
    public async Task CancellationPreservesTheTokenAndRollsBackAnEarlierInsert()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.That(async () => { await SqliteTestDatabase<EmployeesDb>.CreateAsync(canceled.Token); })
            .Throws<OperationCanceledException>();
        using var fixture = SqliteTestDatabase<EmployeesDb>.Create();
        using var source = new CancellationTokenSource();
        await Assert.That(async () => await fixture.SeedAsync(async (s, token) =>
        {
            await s.InsertAsync(Employee(), token);
            source.Cancel();
            token.ThrowIfCancellationRequested();
        }, source.Token)).Throws<OperationCanceledException>();
        await Assert.That(fixture.Query().Employees.Any()).IsFalse();
    }

    [Test]
    public async Task ConvertedPrimaryKeyRoundTripsThroughTheFixture()
    {
        using var fixture = SqliteTestDatabase<TransactionMutationGuardDb>.Create();
        var row = fixture.Seed(s => s.Insert(new MutableTransactionMutationGuardReferenceIdRow
            { Id = new TransactionMutationGuardReferenceId(73), Value = "converted" }));
        fixture.Database.Provider.State.ClearCache();
        await Assert.That(fixture.Query().ReferenceIdRows.Single().Id.Value).IsEqualTo(row.Id.Value);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnsupportedCheckReleasesTheStoreAndNamesTheConstraint(bool asynchronous)
    {
        using var observer = new SqliteConnection();
        FixtureCheckDb.Observe.Value = source =>
        {
            observer.ConnectionString = source.Provider.ConnectionString;
            observer.Open();
            using var command = observer.CreateCommand();
            command.CommandText = "CREATE TABLE keeper_marker (id INTEGER)";
            command.ExecuteNonQuery();
        };
        try
        {
            var failure = asynchronous
                ? await Assert.That(async () => { await SqliteTestDatabase<FixtureCheckDb>.CreateAsync(); }).Throws<NotSupportedException>()
                : await Assert.That(() => SqliteTestDatabase<FixtureCheckDb>.Create()).Throws<NotSupportedException>();
            await Assert.That(failure!.Message).Contains("ck_amount").And.Contains("fixture_amounts");
            observer.Close();
            observer.Open();
            using var command = observer.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='keeper_marker'";
            await Assert.That((long)command.ExecuteScalar()!).IsEqualTo(0L);
        }
        finally { FixtureCheckDb.Observe.Value = null; }
    }

    private static MutableEmployee Employee() => new()
    {
        first_name = "Ada", last_name = "Lovelace", gender = Models.Employees.Employee.Employeegender.F,
        birth_date = new DateOnly(1980, 1, 1), hire_date = new DateOnly(2020, 1, 1)
    };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProvisioningBindsTheColumnsOfEveryView(bool asynchronous)
    {
        var failure = asynchronous
            ? await Assert.That(async () => { await SqliteTestDatabase<FixtureViewDb>.CreateAsync(); }).Throws<SqliteException>()
            : await Assert.That(() => SqliteTestDatabase<FixtureViewDb>.Create()).Throws<SqliteException>();
        await Assert.That(failure!.Message).Contains("missing_source");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PhysicalIndexOnViewFailsDuringProvisioning(bool asynchronous)
    {
        var failure = asynchronous
            ? await Assert.That(async () => { using var fixture = await SqliteTestDatabase<RelationViewDb>.CreateAsync(); }).Throws<NotSupportedException>()
            : await Assert.That(() => { using var fixture = SqliteTestDatabase<RelationViewDb>.Create(); }).Throws<NotSupportedException>();
        await Assert.That(failure!.Message).Contains("relation_unique_view").And.Contains("UX_relation_view_code");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ThrowingModelConstructorReleasesCacheMaintenance(bool asynchronous)
    {
        CacheCleanupScheduler? scheduler = null;
        var constructorFailure = new InvalidOperationException("The test model failed to initialize.");
        FixtureCheckDb.Observe.Value = source =>
        {
            scheduler = source.Provider.State.Cache.CleanupScheduler;
            throw constructorFailure;
        };
        try
        {
            var failure = asynchronous
                ? await Assert.That(async () => { await SqliteTestDatabase<FixtureCheckDb>.CreateAsync(); }).Throws<InvalidOperationException>()
                : await Assert.That(() => SqliteTestDatabase<FixtureCheckDb>.Create()).Throws<InvalidOperationException>();
            await Assert.That(failure).IsSameReferenceAs(constructorFailure);
            await Assert.That(scheduler).IsNotNull();
            await Assert.That(scheduler!.IsRunning).IsFalse();
            await Assert.That(() => scheduler.Start()).Throws<ObjectDisposedException>();
        }
        finally
        {
            FixtureCheckDb.Observe.Value = null;
            scheduler?.Dispose();
        }
    }

    [Test]
    public async Task CancellationAfterModelConstructionReleasesTheKeeper()
    {
        using var observer = new SqliteConnection();
        using var source = new CancellationTokenSource();
        FixtureViewDb.Observe.Value = identity =>
        {
            observer.ConnectionString = identity;
            observer.Open();
            using var command = observer.CreateCommand();
            command.CommandText = "PRAGMA user_version = 19";
            command.ExecuteNonQuery();
            source.Cancel();
        };
        try
        {
            var failure = await Assert.That(async () =>
                { await SqliteTestDatabase<FixtureViewDb>.CreateAsync(source.Token); }).Throws<OperationCanceledException>();
            await Assert.That(failure!.CancellationToken).IsEqualTo(source.Token);
            observer.Close();
            observer.Open();
            using var command = observer.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            await Assert.That((long)command.ExecuteScalar()!).IsEqualTo(0L);
        }
        finally { FixtureViewDb.Observe.Value = null; }
    }

    private static MutableSalaries Salary(int id) => new()
    {
        emp_no = id, FromDate = new DateOnly(2026, 1, 1), ToDate = new DateOnly(2026, 12, 31), salary = 75000
    };
}

[Database("fixture_check"), CacheCleanup(CacheCleanupType.Minutes, 1)]
public sealed partial class FixtureCheckDb : IDatabaseModel
{
    internal static AsyncLocal<Action<DataSourceAccess>?> Observe { get; } = new();
    public DbRead<FixtureAmount> Amounts { get; }
    public FixtureCheckDb(DataSourceAccess source)
    {
        Observe.Value?.Invoke(source);
        Amounts = new(source);
    }
}

[Table("fixture_amounts"), Check("ck_amount", "amount > 0")]
public abstract partial class FixtureAmount(IRowData rowData, IDataSourceAccess source)
    : Immutable<FixtureAmount, FixtureCheckDb>(rowData, source), ITableModel<FixtureCheckDb>
{
    [PrimaryKey, Column("amount")] public abstract int Amount { get; }
}

[Database("fixture_invalid_view")]
public sealed partial class FixtureViewDb : IDatabaseModel
{
    internal static AsyncLocal<Action<string>?> Observe { get; } = new();
    public DbRead<FixtureView> Rows { get; }
    public FixtureViewDb(DataSourceAccess source)
    {
        Observe.Value?.Invoke(source.Provider.ConnectionString);
        Rows = new(source);
    }
}

[View("fixture_view"), Definition("SELECT id FROM missing_source")]
public abstract partial class FixtureView(IRowData rowData, IDataSourceAccess source)
    : Immutable<FixtureView, FixtureViewDb>(rowData, source), IViewModel<FixtureViewDb>
{
    [Column("id")] public abstract int Id { get; }
}
