using System;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task Participation_ValidationDoesNotDispatchAndActiveReadersBlockNewServiceEntry()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        IDataLinqSession<TransactionMutationGuardDb> session = transaction;
        var adaptersBeforeValidation = fixture.Scenario.CreatedTransactionTypes.Count;
        session.ValidateParticipation();
        await Assert.That(fixture.Scenario.CreatedTransactionTypes.Count).IsEqualTo(adaptersBeforeValidation);
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        var access = new ControlledAsyncDatabaseAccess();
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => access);
        await using (var reader = await transaction.DatabaseAccess.ExecuteReaderAsync("SELECT value"))
        {
            await Assert.That(session.ValidateParticipation).Throws<InvalidOperationException>();
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            await Assert.That(session.ValidateParticipation).Throws<InvalidOperationException>();
        }
        session.ValidateParticipation();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        transaction.Rollback();
    }

    [Test]
    public async Task Participation_ClosedCallbackRejectsEntryBeforeTheWrapperIsDisposed()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var checkedClosure = false;
        var resource = new ControlledHelperTransaction
        {
            DisposingTransaction = () =>
            {
                Capture<InvalidOperationException>(transaction.ValidateParticipation);
                checkedClosure = true;
                transaction.DatabaseAccess.Dispose();
            }
        };
        await TransactionCallbackRunner.RunAsync(transaction.ExecutionGate, resource, new(), transaction.TransactionID,
            _ => { transaction.ValidateParticipation(); return Task.FromResult(1); });
        await Assert.That(checkedClosure).IsTrue();
    }
}
