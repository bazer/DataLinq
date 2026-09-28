using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("insert")]
    [Arguments("save-new")]
    [Arguments("update")]
    [Arguments("save-existing")]
    public async Task GeneratedMutation_ReservesTypedInputThroughCommitAndCleanup(string kind)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new()
        {
            Commit = new(paused: true), TransactionCleanup = new(paused: true), ConnectionCleanup = new(paused: true)
        };
        var mutable = kind is "insert" or "save-new"
            ? new MutableTransactionMutationGuardRow(1, "initial")
            : fixture.CreateImmutable(1, "initial").Mutate();
        EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var edits = 0;
        Action<MutableTransactionMutationGuardRow> changes = row => { edits++; row.Value = "submitted"; };
        Task<TransactionMutationGuardRow> work = kind switch
        {
            "insert" => mutable.InsertAsync(changes, fixture.Database),
            "update" => fixture.Database.UpdateAsync(mutable.GetImmutableInstance()!, row =>
            {
                mutable = row;
                changes(row);
            }),
            _ => mutable.SaveAsync(changes, fixture.Database)
        };
        try
        {
            await Assert.That(edits).IsEqualTo(1);
            await completion.Commit.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            AssertReserved();
            completion.Commit.Release();
            await completion.TransactionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            AssertReserved();
            completion.TransactionCleanup.Release();
            await completion.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            AssertReserved();
            using var competing = fixture.Database.Transaction();
            _ = Capture<InvalidOperationException>(() => competing.Delete(mutable));
        }
        finally
        {
            completion.Commit.Release(); completion.TransactionCleanup.Release(); completion.ConnectionCleanup.Release();
            await work;
        }
        await Assert.That((await work).Value).IsEqualTo("stored");
        mutable.Value = "released";
        await Assert.That(completion.Calls.SequenceEqual(["commit", "dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);

        void AssertReserved()
        {
            if (work.IsCompleted) throw new Exception("Generated helper escaped its cleanup lifetime.");
            _ = Capture<InvalidOperationException>(() => mutable.Value = "conflict");
            _ = Capture<InvalidOperationException>(mutable.Reset);
            _ = Capture<InvalidOperationException>(() => mutable.Reset(fixture.CreateImmutable(1, "replacement")));
        }
    }

    [Test]
    [Arguments("canceled")]
    [Arguments("deleted")]
    [Arguments("throwing-edit")]
    public async Task GeneratedMutation_ValidatesBeforeEditingAndCleansUpEditingFailure(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new();
        var factory = EnableAsyncMutations(fixture);
        var mutable = fixture.CreateImmutable(1, "old").Mutate();
        if (phase == "deleted") mutable.SetDeleted();
        var edits = 0;
        var expected = new FormatException("editing failure");
        var failure = await AsyncEnumerationFailureOf(() => mutable.SaveAsync(row =>
        {
            edits++;
            throw expected;
        }, fixture.Database, new(phase != "throwing-edit")));
        await Assert.That(edits).IsEqualTo(phase == "throwing-edit" ? 1 : 0);
        if (phase == "throwing-edit") await Assert.That(failure).IsSameReferenceAs(expected);
        else if (phase == "canceled") await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure is OperationCanceledException).IsFalse();
        await Assert.That(factory.Commands.Count).IsEqualTo(0);
        await Assert.That(completion.Calls.Contains("commit")).IsFalse();
        await Assert.That(completion.Calls.Contains("dispose-connection")).IsTrue();
    }

    [Test]
    public async Task GeneratedMutation_RejectsInvalidBridgeOperationBeforeStartingTransaction()
    {
        using var fixture = new ScriptedFixture(); // Deliberately has no async capability.
        var failure = Capture<ArgumentOutOfRangeException>(() => MutationBridgeProbe.Invalid(fixture.Provider));
        await Assert.That(failure.ParamName).IsEqualTo("changeType");
    }

    private sealed class MutationBridgeProbe : Mutable<TransactionMutationGuardRow>
    {
        internal static Task<TransactionMutationGuardRow> Invalid(DataLinq.Interfaces.IDatabaseProvider provider)
            => ExecuteGeneratedMutationAsync(provider, new MutationBridgeProbe(), _ => { }, TransactionChangeType.Delete, default);
    }
}
