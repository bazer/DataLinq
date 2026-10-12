using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task UnitOfWorkFactory_OrdinaryResultsCommitAndExplicitVetoThrowsAfterCleanup(bool asynchronous, bool veto)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb> factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        Transaction? captured = null;
        var calls = 0;
        TransactionResult<int> Callback(IDataLinqSession<TransactionMutationGuardDb> session)
        {
            captured = (Transaction)session;
            calls++;
            session.Delete(fixture.CreateImmutable(811, "deleted"));
            if (veto) session.RequestRollback("original");
            try { _ = default(TransactionResult<int>).IsSuccess; } catch (InvalidOperationException) { }
            return new Rollback<string>("result data");
        }
        TransactionResult<int> result = default;
        Exception? actual = null;
        try
        {
            result = asynchronous ? await factory.ExecuteAsync((session, _) => Task.FromResult(Callback(session))) : factory.Execute(Callback);
        }
        catch (Exception failure) { actual = failure; }
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(captured!.IsDisposed).IsTrue();
        if (veto)
        {
            await Assert.That(actual).IsTypeOf<DataLinqRollbackRequestedException>();
            await Assert.That(((DataLinqRollbackRequestedException)actual!).Reason).IsEqualTo("original");
            await Assert.That(DataLinqFailure.GetContext(actual)!.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.None);
            await Assert.That(DataLinqFailure.GetContext(actual)!.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.RolledBack);
        }
        else
        {
            await Assert.That(actual).IsNull();
            await Assert.That(result.Reason).IsEqualTo("result data");
        }
        await Assert.That(asynchronous ? fixture.Scenario.AsyncCompletion.Calls.Count(call => call == "commit") : fixture.Scenario.Commits).IsEqualTo(veto ? 0 : 1);
        _ = Capture<InvalidOperationException>(() => captured.RequestRollback("escaped"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnitOfWorkFactory_CallbackCannotCompleteOrDisposeItsBorrowedOwner(bool asynchronous)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        using var request = new CancellationTokenSource();
        var rejected = 0;
        void Verify(IDataLinqSession<TransactionMutationGuardDb> session)
        {
            var owner = (IDataLinqUnitOfWork<TransactionMutationGuardDb>)session;
            foreach (var completion in new Action[] { owner.Commit, owner.Rollback, owner.Dispose })
            {
                _ = Capture<InvalidOperationException>(completion);
                rejected++;
            }
            foreach (var completion in new Func<Task>[] { () => owner.CommitAsync(), () => owner.RollbackAsync(), () => owner.DisposeAsync().AsTask() })
            {
                _ = Capture<InvalidOperationException>(() => completion().GetAwaiter().GetResult());
                rejected++;
            }
            session.Delete(fixture.CreateImmutable(812, "committable"));
        }
        if (asynchronous)
            await factory.ExecuteAsync((session, token) =>
            {
                if (token != request.Token) throw new Exception("The callback token changed.");
                Verify(session);
                return Task.CompletedTask;
            }, cancellationToken: request.Token);
        else factory.Execute(Verify);
        await Assert.That(rejected).IsEqualTo(6);
        await Assert.That(asynchronous ? fixture.Scenario.AsyncCompletion.Calls.Count(call => call == "commit") : fixture.Scenario.Commits).IsEqualTo(1);
    }

    [Test]
    public async Task UnitOfWorkFactory_SynchronousFailureRetainsOriginalAndReportsCleanupFailure()
    {
        using var fixture = new ScriptedFixture();
        var expected = new Exception("business callback");
        var cleanup = fixture.Scenario.DisposeRollbackFailure = new Exception("rollback during cleanup");
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        Transaction? captured = null;
        var actual = Capture<Exception>(() => factory.Execute<int>(session =>
        {
            captured = (Transaction)session;
            session.Delete(fixture.CreateImmutable(813, "failed"));
            throw expected;
        }));
        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(captured!.IsDisposed).IsTrue();
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        var context = DataLinqFailure.GetContext(expected)!;
        await Assert.That(context.Cause).IsEqualTo(DataLinqFailureCause.ApplicationError);
        await Assert.That(context.Stage).IsEqualTo(DataLinqFailureStage.Callback);
        await Assert.That(context.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.None);
        await Assert.That(context.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.Unknown);
        await Assert.That(context.SecondaryFailures.Any(failure => ReferenceEquals(failure.Exception, cleanup))).IsTrue();
    }

    [Test]
    public async Task UnitOfWorkFactory_SynchronousHelperDrainsEscapedSynchronousReader()
    {
        using var fixture = new ScriptedFixture();
        var commands = EnableSyncRaw(fixture);
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 814, "stored"));
        commands.Reader = () => reader;
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        IDataLinqDataReader? escaped = null;
        _ = Capture<InvalidOperationException>(() => factory.Execute(session =>
        {
            escaped = ((Transaction)session).DatabaseAccess.ExecuteReader("SELECT rows");
            escaped.ReadNextRow();
        }));
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(commands.CommandDisposals).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => escaped!.ReadNextRow());
        escaped!.Dispose();
        await Assert.That(reader.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task UnitOfWorkFactory_PreCancellationSkipsCallbackAndCreateRetainsCallerOwnership()
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        await Assert.That(fixture.Scenario.CreatedTransactionTypes).IsEmpty();
        var called = false;
        await Assert.That(async () => await factory.ExecuteAsync(_ => { called = true; return Task.CompletedTask; }, cancellationToken: new(true))).Throws<OperationCanceledException>();
        await Assert.That(called).IsFalse();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        using var owner = factory.Create(TransactionType.ReadOnly);
        await Assert.That(((Transaction)owner).IsDisposed).IsFalse();
        await Assert.That(((Transaction)owner).Type).IsEqualTo(TransactionType.ReadOnly);
        owner.Rollback();
    }

    [Test]
    public async Task UnitOfWorkFactory_SynchronousHelperWaitsForEscapedAsyncWorkBeforeCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 1L };
        fixture.Scenario.AsyncSqlScalars = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        var prepared = fixture.Database.PrepareQuery(0, min => fixture.Database.Query().Rows.Count(row => row.Id > min));
        var factory = new DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>(fixture.Database);
        Task<int>? work = null;
        IDataLinqSession<TransactionMutationGuardDb>? captured = null;
        // The worker is test orchestration only. The factory never schedules a worker.
        var pending = Task.Run(() => Capture<InvalidOperationException>(() => factory.Execute(session =>
        {
            captured = session;
            work = session.ExecuteAsync(prepared, 0);
            session.RequestRollback("veto with unfinished work");
        })));
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var closed = SpinWait.SpinUntil(() =>
            {
                try { captured!.RequestRollback("veto with unfinished work"); return false; }
                catch (InvalidOperationException) { return true; }
            }, TimeSpan.FromSeconds(10));
            await Assert.That(closed).IsTrue();
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(fixture.Scenario.Disposals).IsEqualTo(0);
        }
        finally { access.Dispatch.Release(); }
        var failure = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(await work!).IsEqualTo(1);
        await Assert.That(failure.Message.Contains("unfinished", StringComparison.Ordinal)).IsTrue();
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(1);
    }
}
