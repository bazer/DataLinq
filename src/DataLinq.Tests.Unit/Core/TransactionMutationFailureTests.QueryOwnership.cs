using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task WarmEntitySequence_RetainsTransactionAdmissionBetweenRows()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        var cached = transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(401));
        var query = transaction.Query().Rows.Where(row => row.Id == 401);
        using var rows = query.GetEnumerator();
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current).IsSameReferenceAs(cached);
        _ = Capture<InvalidOperationException>(transaction.Commit);
        _ = Capture<InvalidOperationException>(transaction.Dispose);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        rows.Dispose();
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    [Arguments("linq")]
    [Arguments("fluent")]
    [Arguments("raw")]
    public async Task QueryEnumerator_ReentrantMoveAndDisposeDoNotDisturbActiveRead(string route)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        fixture.Scenario.ReaderFactory = () => reader;
        IEnumerable<TransactionMutationGuardRow> query = route switch
        {
            "linq" => transaction.Query().Rows.Where(row => row.Id == 401),
            "fluent" => transaction.From<TransactionMutationGuardRow>().Where("id").EqualTo(401).SelectQuery().ExecuteAs<TransactionMutationGuardRow>(),
            _ => transaction.GetFromQuery<TransactionMutationGuardRow>("SELECT rows")
        };
        using var rows = query.GetEnumerator();
        var callbacks = 0;
        reader.OnRead = () =>
        {
            callbacks++;
            _ = Capture<InvalidOperationException>(() => rows.MoveNext());
            _ = Capture<InvalidOperationException>(rows.Dispose);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        };
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current.Id).IsEqualTo(401);
        reader.OnRead = null;
        rows.Dispose();
        await Assert.That(callbacks > 0).IsTrue();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    public async Task BufferedPreparedQuery_HonorsCancellationAndReleasesOwnership()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        _ = transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(401));
        var prepared = fixture.Database.PrepareSequenceQuery(401,
            id => fixture.Database.Query().Rows.Where(row => row.Id == id));
        using var cancellation = new CancellationTokenSource();
        var sequence = prepared.Execute(transaction, 401, cancellation.Token);
        using var rows = sequence.GetEnumerator();
        await Assert.That(rows.MoveNext()).IsTrue();
        cancellation.Cancel();
        var canceled = Capture<OperationCanceledException>(() => rows.MoveNext());
        await Assert.That(canceled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    public async Task QueryEnumerators_AreColdRepeatableAndDoNotSwitchSourcesAfterCompletion()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        var query = transaction.Query().Rows.Where(row => row.Id == 401);
        using var first = query.GetEnumerator();
        using var second = query.GetEnumerator();
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
        await Assert.That(first.MoveNext()).IsTrue();
        var row = first.Current;
        _ = Capture<InvalidOperationException>(() => second.MoveNext());
        await Assert.That(first.Current).IsSameReferenceAs(row);
        first.Dispose();
        await Assert.That(query.Single()).IsSameReferenceAs(row);
        using var beforeCommit = query.GetEnumerator();
        transaction.Commit();
        _ = Capture<InvalidOperationException>(() => beforeCommit.MoveNext());
        await Assert.That(row.GetReadSource()).IsSameReferenceAs(fixture.Provider.ReadOnlyAccess);
    }

    [Test]
    public async Task ConcurrentQueryMoveAndDispose_AreRejectedWithoutReleasingOwner()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        var firstRead = 0;
        reader.OnRead = () =>
        {
            if (Interlocked.Increment(ref firstRead) != 1) return;
            started.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
        };
        fixture.Scenario.ReaderFactory = () => reader;
        using var rows = transaction.Query().Rows.Where(row => row.Id == 401).GetEnumerator();
        var move = Task.Run(rows.MoveNext);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(() => rows.MoveNext());
            _ = Capture<InvalidOperationException>(rows.Dispose);
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            await Assert.That(reader.Disposals).IsEqualTo(0);
        }
        finally
        {
            release.Set();
            await move;
        }
        await Assert.That(await move).IsTrue();
        await Assert.That(rows.Current.Id).IsEqualTo(401);
        rows.Dispose();
        _ = transaction.Query();
    }

    [Test]
    public async Task LocalProjectionConstructor_CannotReenterOrDisposeTheOwningQuery()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, " stored "));
        using var rows = transaction.Query().Rows.Where(row => row.Id == 401)
            .Select(row => new OwnershipCallbackBox(row.Value.Trim())).GetEnumerator();
        var calls = 0;
        OwnershipCallbackBox.Creating = () =>
        {
            calls++;
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => rows.MoveNext());
            _ = Capture<InvalidOperationException>(rows.Dispose);
        };
        try
        {
            await Assert.That(rows.MoveNext()).IsTrue();
            await Assert.That(rows.Current.Value).IsEqualTo("stored");
            await Assert.That(calls).IsEqualTo(1);
            rows.Dispose();
            _ = transaction.Query();
        }
        finally { OwnershipCallbackBox.Creating = null; }
    }

    [Test]
    [Arguments("empty")]
    [Arguments("pre-cancel")]
    [Arguments("provider-failure")]
    public async Task QueryBeforeFirstResult_ReleasesAdmissionOnEveryExit(string exit)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var prepared = fixture.Database.PrepareSequenceQuery(401,
            id => fixture.Database.Query().Rows.Where(row => row.Id == id));
        var sequence = prepared.Execute(transaction, 401, cancellation.Token);
        using var rows = sequence.GetEnumerator();
        if (exit == "pre-cancel")
        {
            cancellation.Cancel();
            _ = Capture<OperationCanceledException>(() => rows.MoveNext());
            await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        }
        else if (exit == "provider-failure")
        {
            var expected = new InjectedMutationException("query command construction");
            fixture.Scenario.CommandFailure = expected;
            await Assert.That(Capture<InjectedMutationException>(() => rows.MoveNext())).IsSameReferenceAs(expected);
        }
        else
            await Assert.That(rows.MoveNext()).IsFalse();
        await Assert.That(rows.MoveNext()).IsFalse();
        _ = transaction.Query();
    }

    public sealed class OwnershipCallbackBox
    {
        private static readonly AsyncLocal<Action?> creating = new();
        internal static Action? Creating { get => creating.Value; set => creating.Value = value; }
        public string Value { get; }
        public OwnershipCallbackBox(string value) { Creating?.Invoke(); Value = value; }
    }
}
