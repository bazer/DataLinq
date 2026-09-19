using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task CapturedRows_OrdinaryEnumeratorsOwnIndependentSqlAndMaterializerSnapshots()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        var query = transaction.From<TransactionMutationGuardRow>();
        var select = query.SelectQuery();
        var sequence = select.ReadRowsAsyncCore();
        await Assert.That(factory.Inputs).IsEmpty();
        select.What("value");
        factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(["first"]) };
        await using var first = sequence.GetAsyncEnumerator();
        var firstSql = factory.Inputs[0].Text;
        select.What("id");
        factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(["second", 42]) };
        await using var second = sequence.GetAsyncEnumerator();
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        select.What("99 AS ignored");
        await Assert.That(await first.MoveNextAsync()).IsTrue();
        await Assert.That(first.Current[fixture.RowTable.GetColumnByDbName("value")]).IsEqualTo("first");
        await Assert.That(first.Current.IsColumnPresent(fixture.RowTable.GetColumnByDbName("id").Index)).IsFalse();
        await Assert.That(factory.Accesses[0].ObservedCommand!.CommandText).IsEqualTo(firstSql);
        await first.DisposeAsync();
        await Assert.That(await second.MoveNextAsync()).IsTrue();
        await Assert.That(second.Current[fixture.RowTable.GetColumnByDbName("id")]).IsEqualTo(42);
        await Assert.That(second.Current[fixture.RowTable.GetColumnByDbName("value")]).IsEqualTo("second");
        await second.DisposeAsync();
        await Assert.That(query.WhatList!.Select(x => SqlIdentifier.Unquote(x, query.EscapeCharacter))
            .SequenceEqual(new[] { "value", "id", "99 AS ignored" })).IsTrue();
        await Assert.That(factory.Commands.All(x => x.Resource.AsyncDisposals == 1)).IsTrue();
    }

    [Test]
    public async Task CapturedRows_FreezeParameterArraysAndSelectedProviderBeforeSuspension()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ReaderOverride = new ControlledRowDataReader([new byte[] { 2 }]) };
        var original = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = original;
        var argument = new byte[] { 1, 2 };
        var query = transaction.From<TransactionMutationGuardBinaryRow>();
        query.Where("payload").EqualTo(argument);
        var select = query.SelectQuery().What("id");
        var sequence = select.ReadRowsAsyncCore();
        argument[0] = 3;
        await using var rows = sequence.GetAsyncEnumerator();
        argument[0] = 4;
        var replacement = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = replacement;
        var pending = rows.MoveNextAsync().AsTask();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            select.What("payload");
            query.Where("id").EqualTo(new byte[] { 8 });
            argument[0] = 9;
            await Assert.That(((byte[])original.Inputs[0].ToSql().Parameters[0].Value!)[0]).IsEqualTo((byte)3);
            await Assert.That(original.Inputs[0].ToSql().Parameters.Count).IsEqualTo(1);
            await Assert.That(replacement.Inputs).IsEmpty();
        }
        finally { access.Dispatch.Release(); }
        await Assert.That(await pending).IsTrue();
        await Assert.That(((byte[])rows.Current[fixture.BinaryTable.GetColumnByDbName("id")]!)[0]).IsEqualTo((byte)2);
        await rows.DisposeAsync();
    }

    [Test]
    public async Task CapturedRows_FirstRowCapturesAtCall_AndWaitsForCommandCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader([7]);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ReaderOverride = reader };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => access,
            ConfigureCommand = command => command.Resource.Cleanup = new(paused: true)
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery().What("id");
        var pending = select.ReadFirstRowAsyncCore();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        select.What("value");
        access.Dispatch.Release();
        var cleanup = factory.Commands[0].Resource.Cleanup;
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(reader.Moves).IsEqualTo(1);
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        }
        finally { cleanup.Release(); await pending; }
        var row = await pending;
        await Assert.That(row![fixture.RowTable.GetColumnByDbName("id")]).IsEqualTo(7);
        await Assert.That(reader.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task CapturedRows_BufferedResultIsCompleteAndIndependentAfterCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var reader = new ControlledRowDataReader([3, "a"], [4, "b"]);
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = fixture.Database.From<TransactionMutationGuardRow>().SelectQuery();
        var result = await select.ReadRowsBufferedAsyncCore();
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0][fixture.RowTable.GetColumnByDbName("value")]).IsEqualTo("a");
        await Assert.That(result[1][fixture.RowTable.GetColumnByDbName("value")]).IsEqualTo("b");
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(reader.Moves).IsEqualTo(3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CapturedRows_BufferedFailureNeverReturnsItsCompletedPrefix(bool cancel)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var expected = new Exception("second row");
        var reader = new ControlledRowDataReader([1], [2])
        {
            Advancing = call => { if (call == 2) { if (cancel) cancellation.Cancel(); else throw expected; } }
        };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery().What("id");
        var error = await AsyncEnumerationFailureOf(() => select.ReadRowsBufferedAsyncCore(cancellation.Token));
        if (cancel) await Assert.That(error is OperationCanceledException).IsTrue();
        else await Assert.That(error).IsSameReferenceAs(expected);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task CapturedRows_UnmappedProjectionExpressionsDoNotShiftMappedReaderOrdinals()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([5, 99, "last"]) }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = fixture.Database.From<TransactionMutationGuardRow>().SelectQuery().What("id", "99 AS ignored", "value");
        var row = await select.ReadFirstRowAsyncCore();
        await Assert.That(row![fixture.RowTable.GetColumnByDbName("id")]).IsEqualTo(5);
        await Assert.That(row[fixture.RowTable.GetColumnByDbName("value")]).IsEqualTo("last");
        await Assert.That(factory.Inputs[0].Text).Contains("99 AS ignored");
    }

    [Test]
    public async Task CapturedRows_RawReaderResultsRemainBorrowedCursorViews()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1], [2]) } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = fixture.Database.From<TransactionMutationGuardRow>().SelectQuery().What("id");
        await using var rows = select.ReadReaderAsyncCore().GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        var first = rows.Current;
        await Assert.That(first.GetInt32(0)).IsEqualTo(1);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(rows.Current).IsSameReferenceAs(first);
        await Assert.That(first.GetInt32(0)).IsEqualTo(2);
        await rows.DisposeAsync();
        _ = Capture<ObjectDisposedException>(() => first.GetInt32(0));
    }

    [Test]
    public async Task CapturedRows_PreCanceledAndUnusedEnumeratorsDoNotCreateCommandsOrPoison()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        await using (var unused = select.ReadRowsAsyncCore().GetAsyncEnumerator()) { }
        await using var canceled = select.ReadRowsAsyncCore(new(true)).GetAsyncEnumerator();
        await Assert.That(await AsyncEnumerationFailureOf(() => canceled.MoveNextAsync().AsTask()) is OperationCanceledException).IsTrue();
        await Assert.That(factory.Commands.All(x => x.Creates == 0)).IsTrue();
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    public async Task CapturedRows_CompletedSourceWinsOverCancellationWithoutDispatch()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        await using var rows = select.ReadRowsAsyncCore(new(true)).GetAsyncEnumerator();
        transaction.Commit();
        await Assert.That(await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask())).IsTypeOf<InvalidOperationException>();
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
    }

    [Test]
    public async Task CapturedRows_BinaryValuesDoNotAliasProviderBuffersAfterCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var bytes = new byte[] { 1, 2 };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([bytes]) },
            ConfigureCommand = command => command.Resource.Disposing = () => bytes[0] = 9
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var row = await fixture.Database.From<TransactionMutationGuardBinaryRow>().SelectQuery().What("id").ReadFirstRowAsyncCore();
        var value = (byte[])row![fixture.BinaryTable.GetColumnByDbName("id")]!;
        await Assert.That(value).IsNotSameReferenceAs(bytes);
        await Assert.That(value[0]).IsEqualTo((byte)1);
        await Assert.That(bytes[0]).IsEqualTo((byte)9);
    }

    [Test]
    public async Task CapturedRows_EmptyFirstRowClosesAllOwnedResources()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var reader = new ControlledRowDataReader();
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var row = await fixture.Database.From<TransactionMutationGuardRow>().SelectQuery().ReadFirstRowAsyncCore();
        await Assert.That(row).IsNull();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CapturedRows_CleanupFailurePreventsTerminalSuccess(bool buffered)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("command cleanup");
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1]) },
            ConfigureCommand = command => command.Resource.Disposing = () => throw expected
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery().What("id");
        var error = await AsyncEnumerationFailureOf(() => buffered ? select.ReadRowsBufferedAsyncCore() : (Task)select.ReadFirstRowAsyncCore());
        await Assert.That(error).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task CapturedRows_UnsupportedFactoryWinsOverCancellationWithoutIo()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        var sequence = select.ReadRowsAsyncCore(new(true));
        _ = Capture<NotSupportedException>(() => sequence.GetAsyncEnumerator());
        _ = Capture<NotSupportedException>(() => select.ReadFirstRowAsyncCore(new(true)));
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    public async Task CapturedRows_NullPresenceAndDuplicateProjectionUseCapturedOrdinals()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([new byte[] { 1 }, DBNull.Value, new byte[] { 2 }]) }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var row = await fixture.Database.From<TransactionMutationGuardBinaryRow>().SelectQuery()
            .What("id", "payload", "id").ReadFirstRowAsyncCore();
        await Assert.That(((byte[])row![fixture.BinaryTable.GetColumnByDbName("id")]!)[0]).IsEqualTo((byte)2);
        await Assert.That(row[fixture.BinaryTable.GetColumnByDbName("payload")]).IsNull();
        await Assert.That(row.IsColumnPresent(fixture.BinaryTable.GetColumnByDbName("payload").Index)).IsTrue();
    }

    [Test]
    public async Task CapturedRows_InvalidSqlNullDoesNotPublishRowAndCleansUp()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader([DBNull.Value]);
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery().What("id");
        await using var rows = select.ReadRowsAsyncCore().GetAsyncEnumerator();
        var error = await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask());
        await Assert.That(ExecutionFailureContexts.Get(error)!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        _ = Capture<InvalidOperationException>(() => _ = rows.Current);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    private sealed class CapturedReadProvider(ScriptedMutationScenario scenario) : CapturedReadProvider<TransactionMutationGuardDb>(scenario);

    private class CapturedReadProvider<TModel>(ScriptedMutationScenario scenario, DatabaseType databaseType = DatabaseType.SQLite)
        : ScriptedMutationProvider<TModel>(scenario, databaseType)
        where TModel : class, IDatabaseModel<TModel>
    {
        public override string GetLastIdQuery() => "SELECT last_insert_rowid()";
        public override string GetOperatorSql(Operator operation) => operation switch
        {
            Operator.Equal => "=", Operator.NotEqual => "<>", Operator.In => "IN", Operator.NotIn => "NOT IN",
            Operator.GreaterThan => ">", Operator.GreaterThanOrEqual => ">=", Operator.LessThan => "<", Operator.LessThanOrEqual => "<=",
            Operator.EqualNull => "IS", Operator.NotEqualNull => "IS NOT", _ => throw new NotSupportedException()
        };
        public override Sql GetParameter(Sql sql, string key, object? value) => sql.AddParameter("@" + key, value);
        public override Sql GetParameterValue(Sql sql, string key) => sql.AddText("@" + key);
        public override string GetParameterName(Operator relation, string[] key)
        {
            var names = string.Join(", ", key.Select(x => "@" + x));
            return key.Length > 1 || relation is Operator.In or Operator.NotIn ? "(" + names + ")" : names;
        }
        public override Sql GetParameterComparison(Sql sql, string field, Operator operation, string[] prefix) =>
            sql.AddText(field + " " + GetOperatorSql(operation) + " " + GetParameterName(operation, prefix));
        public override Sql GetLimitOffset(Sql sql, int? limit, int? offset)
        {
            if (limit.HasValue || offset.HasValue) sql.AddText(" LIMIT " + (limit ?? -1));
            if (offset.HasValue) sql.AddText(" OFFSET " + offset);
            return sql;
        }
        public override Sql GetTableName(Sql sql, string tableName, string? alias = null)
        {
            SqlIdentifier.Append(sql, tableName, Constants.EscapeCharacter);
            if (alias is not null) { sql.AddText(" "); SqlIdentifier.Append(sql, alias, Constants.EscapeCharacter); }
            return sql;
        }
    }
}
