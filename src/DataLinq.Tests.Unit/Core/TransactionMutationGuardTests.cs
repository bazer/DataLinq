using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class TransactionMutationGuardTests
{
    [Test]
    public async Task ReadOnlyTransaction_AllDirectWriteApisRejectBeforeCommandCreation()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction(TransactionType.ReadOnly);
        var newMutable = fixture.CreateNewMutable(101, "new-secret");
        var updateMutable = fixture.CreateExistingMutable(102, "original");
        updateMutable["Value"] = "update-secret";
        var saveMutable = fixture.CreateExistingMutable(103, "original");
        saveMutable["Value"] = "save-secret";
        var immutable = fixture.CreateImmutable(104, "delete-secret");

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Insert(newMutable),
            "read-only");
        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Update(updateMutable),
            "read-only");
        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Save(saveMutable),
            "read-only");
        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Delete(immutable),
            "read-only");

        await Assert.That(transaction.Changes).IsEmpty();
    }

    [Test]
    public async Task ReadOnlyDatabaseConvenience_AllWriteApisRejectBeforeCommandCreation()
    {
        using var fixture = new ProbeFixture();
        var newMutable = fixture.CreateNewMutable(111, "new-secret");
        var updateMutable = fixture.CreateExistingMutable(112, "original");
        updateMutable["Value"] = "update-secret";
        var saveMutable = fixture.CreateExistingMutable(113, "original");
        saveMutable["Value"] = "save-secret";
        var immutable = fixture.CreateImmutable(114, "delete-secret");

        await AssertGuardRejected(
            fixture.Provider,
            transaction: null,
            () => fixture.Database.Insert(newMutable, TransactionType.ReadOnly),
            "read-only");
        await AssertGuardRejected(
            fixture.Provider,
            transaction: null,
            () => fixture.Database.Update(updateMutable, TransactionType.ReadOnly),
            "read-only");
        await AssertGuardRejected(
            fixture.Provider,
            transaction: null,
            () => fixture.Database.Save(saveMutable, TransactionType.ReadOnly),
            "read-only");
        await AssertGuardRejected(
            fixture.Provider,
            transaction: null,
            () => fixture.Database.Delete(immutable, TransactionType.ReadOnly),
            "read-only");
    }

    [Test]
    public async Task TerminalAndDisposedTransactions_RejectBeforeCommandCreation()
    {
        using var fixture = new ProbeFixture();

        using (var committed = fixture.Database.Transaction())
        {
            committed.Commit();
            await AssertGuardRejected(
                fixture.Provider,
                committed,
                () => committed.Insert(fixture.CreateNewMutable(121, "committed")),
                "already committed");
        }

        using (var rolledBack = fixture.Database.Transaction())
        {
            rolledBack.Rollback();
            await AssertGuardRejected(
                fixture.Provider,
                rolledBack,
                () => rolledBack.Insert(fixture.CreateNewMutable(122, "rolled-back")),
                "already rolled back");
        }

        var disposed = fixture.Database.Transaction();
        disposed.Dispose();

        await Assert.That(disposed.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
        await AssertGuardRejected(
            fixture.Provider,
            disposed,
            () => disposed.Insert(fixture.CreateNewMutable(123, "disposed")),
            "has been disposed");
    }

    [Test]
    public async Task InvalidAndDeletedMutables_AllWriteApisRejectWithoutChangingState()
    {
        using var fixture = new ProbeFixture();

        foreach (var operation in Enum.GetValues<GuardOperation>())
        {
            using var invalidTransaction = fixture.Database.Transaction();
            var invalid = fixture.CreateExistingMutable(130 + (int)operation, "original");
            invalid["Value"] = $"invalid-{operation}";
            invalid.Invalidate(MutableInvalidationReason.MutationFailed);
            var invalidBefore = CaptureMutable(invalid);

            await AssertGuardRejected(
                fixture.Provider,
                invalidTransaction,
                () => Invoke(invalidTransaction, invalid, operation),
                "mutation failed");
            await AssertMutableUnchanged(invalid, invalidBefore);

            using var deletedTransaction = fixture.Database.Transaction();
            var deleted = fixture.CreateExistingMutable(140 + (int)operation, "original");
            deleted["Value"] = $"deleted-{operation}";
            deleted.SetDeleted();
            var deletedBefore = CaptureMutable(deleted);

            await AssertGuardRejected(
                fixture.Provider,
                deletedTransaction,
                () => Invoke(deletedTransaction, deleted, operation),
                "deleted row");
            await AssertMutableUnchanged(deleted, deletedBefore);
        }
    }

    [Test]
    public async Task InsertExistingAndUpdateNew_RejectBeforeCommandCreation()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var existing = fixture.CreateExistingMutable(151, "existing");
        var newMutable = fixture.CreateNewMutable(152, "new");
        var counts = fixture.Provider.Counts;

        var insertException = Capture<ArgumentException>(() => transaction.Insert(existing));
        var updateException = Capture<ArgumentException>(() => transaction.Update(newMutable));

        await Assert.That(insertException.Message).Contains("not a new row");
        await Assert.That(updateException.Message).Contains("new row");
        await Assert.That(fixture.Provider.Counts).IsEqualTo(counts);
        await Assert.That(transaction.Changes).IsEmpty();
    }

    [Test]
    public async Task CleanCommittedMutable_DifferentProviderStillRejectsBeforeNoChangeFastPath()
    {
        using var origin = new ProbeFixture("origin-provider");
        using var target = new ProbeFixture("target-provider");
        using var transaction = target.Database.Transaction();
        var mutable = origin.CreateExistingMutable(161, "clean");
        var before = CaptureMutable(mutable);

        await Assert.That(mutable.GetChanges()).IsEmpty();
        var exception = await AssertGuardRejected(
            target.Provider,
            transaction,
            () => transaction.Update(mutable),
            "different provider instance");

        await Assert.That(exception.Message).DoesNotContain(origin.Provider.ConnectionString);
        await AssertMutableUnchanged(mutable, before);
    }

    [Test]
    public async Task CleanTransactionLocalMutable_DifferentTransactionRejectsBeforeNoChangeFastPath()
    {
        using var fixture = new ProbeFixture();
        using var owner = fixture.Database.Transaction();
        using var other = fixture.Database.Transaction();
        var immutable = fixture.CreateImmutable(171, "clean");
        var mutable = new Mutable<TransactionMutationGuardRow>(immutable);
        mutable.AdvanceBaseline(immutable, owner.MutableOwnership);
        var before = CaptureMutable(mutable);

        await Assert.That(mutable.GetChanges()).IsEmpty();
        await AssertGuardRejected(
            fixture.Provider,
            other,
            () => other.Update(mutable),
            "belongs to unresolved transaction");

        await AssertMutableUnchanged(mutable, before);
        owner.EnsureMutationPreflight(mutable, TransactionChangeType.Update);
        await Assert.That(fixture.Provider.Counts).IsEqualTo(default(ProbeCounts));
    }

    [Test]
    public async Task TransactionLocalMutable_WrongTransactionAndImplicitSaveRejectAndPreserveOwner()
    {
        using var fixture = new ProbeFixture();
        using var owner = fixture.Database.Transaction();
        using var other = fixture.Database.Transaction();
        var immutable = fixture.CreateImmutable(181, "baseline");
        var mutable = new Mutable<TransactionMutationGuardRow>(immutable);
        mutable.AdvanceBaseline(immutable, owner.MutableOwnership);
        mutable["Value"] = "pending-secret";
        var before = CaptureMutable(mutable);

        await AssertGuardRejected(
            fixture.Provider,
            other,
            () => other.Save(mutable),
            "belongs to unresolved transaction");
        await AssertMutableUnchanged(mutable, before);

        await AssertGuardRejected(
            fixture.Provider,
            transaction: null,
            () => fixture.Database.Save(mutable),
            "belongs to unresolved transaction");
        await AssertMutableUnchanged(mutable, before);

        owner.EnsureMutationPreflight(mutable, TransactionChangeType.Update);
        await Assert.That(mutable.Lifecycle.TransactionOwner).IsSameReferenceAs(owner.MutableOwnership);
        await Assert.That(other.Changes).IsEmpty();
    }

    [Test]
    public async Task PrimaryKeyMutation_UpdateAndSaveRejectBeforeCommandCreation()
    {
        using var fixture = new ProbeFixture();

        foreach (var operation in new[] { GuardOperation.Update, GuardOperation.Save })
        {
            using var transaction = fixture.Database.Transaction();
            var mutable = fixture.CreateExistingMutable(191 + (int)operation, "baseline");
            mutable["Id"] = 9000 + (int)operation;
            mutable["Value"] = $"do-not-leak-{operation}";
            var before = CaptureMutable(mutable);

            var exception = await AssertGuardRejected(
                fixture.Provider,
                transaction,
                () => Invoke(transaction, mutable, operation),
                "Primary-key column(s) 'id'");

            await Assert.That(exception.Message).DoesNotContain($"do-not-leak-{operation}");
            await AssertMutableUnchanged(mutable, before);
        }
    }

    [Test]
    public async Task ImmutableDelete_DifferentProviderRejectsWhenOriginIsAvailable()
    {
        using var origin = new ProbeFixture("immutable-origin");
        using var target = new ProbeFixture("immutable-target");
        using var transaction = target.Database.Transaction();
        var immutable = origin.CreateImmutable(201, "immutable-secret");

        var exception = await AssertGuardRejected(
            target.Provider,
            transaction,
            () => transaction.Delete(immutable),
            "different provider instance");

        await Assert.That(exception.Message).DoesNotContain(origin.Provider.ConnectionString);
        await Assert.That(exception.Message).DoesNotContain("immutable-secret");
    }

    [Test]
    public async Task CallbackOverloads_RunPreflightBeforeInvokingCallerCode()
    {
        using var fixture = new ProbeFixture();
        using var readOnly = fixture.Database.Transaction(TransactionType.ReadOnly);
        var insert = fixture.CreateNewMutable(211, "insert");
        var update = fixture.CreateExistingMutable(212, "update");
        var save = fixture.CreateExistingMutable(213, "save");
        var callbackCalls = 0;

        await AssertGuardRejected(
            fixture.Provider,
            readOnly,
            () => readOnly.Insert<TransactionMutationGuardRow>(insert, _ => callbackCalls++),
            "read-only");
        await AssertGuardRejected(
            fixture.Provider,
            readOnly,
            () => readOnly.Update<TransactionMutationGuardRow>(update, _ => callbackCalls++),
            "read-only");
        await AssertGuardRejected(
            fixture.Provider,
            readOnly,
            () => readOnly.Save<TransactionMutationGuardRow>(save, _ => callbackCalls++),
            "read-only");

        using var target = new ProbeFixture("callback-target");
        using var targetTransaction = target.Database.Transaction();
        var originImmutable = fixture.CreateImmutable(214, "immutable");

        await AssertGuardRejected(
            target.Provider,
            targetTransaction,
            () => targetTransaction.Update(originImmutable, _ => callbackCalls++),
            "different provider instance");

        await Assert.That(callbackCalls).IsEqualTo(0);
        await Assert.That(insert["Value"]).IsEqualTo("insert");
        await Assert.That(update["Value"]).IsEqualTo("update");
        await Assert.That(save["Value"]).IsEqualTo("save");
    }

    [Test]
    public async Task GeneratedExtensionCallback_RunsPreflightBeforeInvokingCallerCode()
    {
        using var fixture = new ProbeFixture();
        using var readOnly = fixture.Database.Transaction(TransactionType.ReadOnly);
        var mutable = new MutableTransactionMutationGuardRow
        {
            Id = 215,
            Value = "generated-callback"
        };
        var callbackCalls = 0;

        await AssertGuardRejected(
            fixture.Provider,
            readOnly,
            () => mutable.Insert(_ => callbackCalls++, readOnly),
            "read-only");

        await Assert.That(callbackCalls).IsEqualTo(0);
        await Assert.That(mutable.Value).IsEqualTo("generated-callback");
        await Assert.That(readOnly.Changes).IsEmpty();
    }

    [Test]
    public async Task TargetProviderWithoutExactModelMetadata_RejectsNewAndLegacyStateChangeBeforeProviderBoundary()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var foreignMetadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel(typeof(TransactionMutationGuardDb))
            .ValueOrException();
        var foreignModel = foreignMetadata.TableModels
            .Single(tableModel => tableModel.Model.CsType.Type == typeof(TransactionMutationGuardRow))
            .Model;
        var foreignNew = new ForeignMetadataMutable(foreignModel);
        foreignNew["Id"] = 216;
        foreignNew["Value"] = "foreign-new";
        var valueColumn = foreignModel.Table.GetColumnByDbName("value");
        var foreignLegacy = new LegacyGuardMutable(
            foreignModel.Table,
            isNew: false,
            id: 217,
            value: "foreign-legacy",
            _ => [new(valueColumn, "changed")]);
        var legacyChange = new StateChange(
            foreignLegacy,
            foreignModel.Table,
            TransactionChangeType.Update);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Insert(foreignNew),
            "exact table metadata is not mapped by the target provider");
        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => legacyChange.GetDbCommand(transaction),
            "exact table metadata is not mapped by the target provider");
        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => legacyChange.ExecuteQuery(transaction),
            "exact table metadata is not mapped by the target provider");

        await Assert.That(transaction.Changes).IsEmpty();
    }

    [Test]
    public async Task AutoIncrementInsertStateChange_PrimaryKeyAssignedAfterCaptureRejectsBeforeProviderBoundary()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = new Mutable<TransactionMutationGuardAutoRow>();
        mutable["Value"] = "captured-insert";
        var stateChange = new StateChange(
            mutable,
            fixture.AutoTable,
            TransactionChangeType.Insert);

        await Assert.That(stateChange.PrimaryKeys.IsNull).IsTrue();
        mutable["Id"] = 218;

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.GetDbCommand(transaction),
            "primary-key identity changed after this state change was captured");
        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.ExecuteQuery(transaction),
            "primary-key identity changed after this state change was captured");

        await Assert.That(transaction.Changes).IsEmpty();
    }

    [Test]
    [NotInParallel]
    public async Task ImmutableReferenceTypedIdDrift_DeleteRejectsAgainstAuthoritativeIdentity()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var id = new TransactionMutationGuardReferenceId(219);
        var immutable = fixture.CreateReferenceIdImmutable(id, "immutable-baseline");
        var authoritativeIdentity = DataLinqKey.FromValue(219);
        var converter = fixture.ReferenceIdConverter;
        converter.Reset();

        id.Value = 9219;

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Delete(immutable),
            "no longer match its authoritative identity");

        await Assert.That(immutable.PrimaryKeys()).IsEqualTo(authoritativeIdentity);
        await Assert.That(immutable.Id).IsSameReferenceAs(id);
        await Assert.That(immutable.Id.Value).IsEqualTo(9219);
        await Assert.That(converter.ToProviderValues).Contains(9219);
        await Assert.That(transaction.Changes).IsEmpty();
    }

    [Test]
    public async Task LegacyStateChanges_InsertExistingAndUpdateNewRejectBeforeProviderBoundary()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var valueColumn = fixture.Table.GetColumnByDbName("value");
        var existing = new LegacyGuardMutable(
            fixture.Table,
            isNew: false,
            id: 220,
            value: "existing",
            _ => [new(valueColumn, "changed")]);
        var newModel = new LegacyGuardMutable(
            fixture.Table,
            isNew: true,
            id: 221,
            value: "new",
            _ => [new(valueColumn, "changed")]);
        var insertExisting = new StateChange(
            existing,
            fixture.Table,
            TransactionChangeType.Insert);
        var updateNew = new StateChange(
            newModel,
            fixture.Table,
            TransactionChangeType.Update);
        var counts = fixture.Provider.Counts;

        foreach (var action in new Action[]
        {
            () => insertExisting.GetDbCommand(transaction),
            () => insertExisting.ExecuteQuery(transaction)
        })
        {
            var exception = Capture<ArgumentException>(action);
            await Assert.That(exception.Message).Contains("not a new row");
        }

        foreach (var action in new Action[]
        {
            () => updateNew.GetDbCommand(transaction),
            () => updateNew.ExecuteQuery(transaction)
        })
        {
            var exception = Capture<ArgumentException>(action);
            await Assert.That(exception.Message).Contains("new row");
        }

        await Assert.That(fixture.Provider.Counts).IsEqualTo(counts);
        await Assert.That(transaction.Changes).IsEmpty();
    }

    [Test]
    public async Task CandidateBackstop_RejectionBeforeAddRangeLeavesTransactionChangesEmpty()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var ownValue = fixture.Table.GetColumnByDbName("value");
        var foreignId = fixture.OtherTable.GetColumnByDbName("id");
        var model = new LegacyGuardMutable(
            fixture.Table,
            isNew: false,
            id: 222,
            value: "baseline",
            read => read == 2
                ? [new(foreignId, 9222)]
                : [new(ownValue, "valid")]);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Delete(model),
            "not an exact mapped column");

        await Assert.That(model.ChangeReads).IsEqualTo(3);
        await Assert.That(transaction.Changes).IsEmpty();
    }

    [Test]
    public async Task StateChangePublicExecutionBackstops_RecheckInvalidLifecycleBeforeCommandCreation()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(221, "baseline");
        mutable["Value"] = "captured-change";
        var stateChange = new StateChange(
            mutable,
            mutable.Metadata().Table,
            TransactionChangeType.Update);
        mutable.Invalidate(MutableInvalidationReason.MutationFailed);
        var before = CaptureMutable(mutable);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.GetDbCommand(transaction),
            "mutation failed");
        await AssertMutableUnchanged(mutable, before);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.ExecuteQuery(transaction),
            "mutation failed");
        await AssertMutableUnchanged(mutable, before);
    }

    [Test]
    public async Task StateChangeCapturedPrimaryKeyAssignment_ResetCannotLaunderItPastExecutionGuard()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(231, "baseline");
        mutable["Id"] = 9231;
        mutable["Value"] = "captured-secret";
        var stateChange = new StateChange(
            mutable,
            mutable.Metadata().Table,
            TransactionChangeType.Update);

        mutable.Reset();
        var afterReset = CaptureMutable(mutable);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.GetDbCommand(transaction),
            "Primary-key column(s) 'id'");
        await AssertMutableUnchanged(mutable, afterReset);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.ExecuteQuery(transaction),
            "Primary-key column(s) 'id'");
        await AssertMutableUnchanged(mutable, afterReset);
    }

    [Test]
    public async Task StateChangeCapturedIdentity_ResetToAnotherRowCannotRetargetExecution()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var first = fixture.CreateImmutable(241, "first");
        var second = fixture.CreateImmutable(242, "second");
        var mutable = new Mutable<TransactionMutationGuardRow>(first);
        mutable["Value"] = "captured-secret";
        var stateChange = new StateChange(
            mutable,
            mutable.Metadata().Table,
            TransactionChangeType.Update);

        mutable.Reset(second);
        var afterReset = CaptureMutable(mutable);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.GetDbCommand(transaction),
            "primary-key identity changed after this state change was captured");
        await AssertMutableUnchanged(mutable, afterReset);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.ExecuteQuery(transaction),
            "primary-key identity changed after this state change was captured");
        await AssertMutableUnchanged(mutable, afterReset);
    }

    [Test]
    public async Task StateChangeConstructor_RejectsWrongMappedTableBeforeProviderBoundary()
    {
        using var fixture = new ProbeFixture();
        var mutable = fixture.CreateExistingMutable(251, "baseline");
        mutable["Value"] = "pending";
        var counts = fixture.Provider.Counts;

        var exception = Capture<ArgumentException>(() =>
            new StateChange(
                mutable,
                fixture.OtherTable,
                TransactionChangeType.Update));

        await Assert.That(exception.Message).Contains("exact mapped table definition");
        await Assert.That(fixture.Provider.Counts).IsEqualTo(counts);
    }

    [Test]
    public async Task OwnedMutableRowData_PublicMutationApisRejectWithoutChangingOwnerState()
    {
        using var fixture = new ProbeFixture();
        var mutable = fixture.CreateExistingMutable(261, "baseline");
        mutable["Value"] = "pending-secret";
        var replacement = fixture.CreateImmutable(262, "replacement");
        var rowData = mutable.GetRowData();
        var valueColumn = fixture.Table.GetColumnByDbName("value");
        var before = CaptureMutable(mutable);

        var resetAssignments = Capture<InvalidOperationException>(rowData.Reset);
        await Assert.That(resetAssignments.Message).Contains("Direct mutation of row data owned by a mutable model");
        await AssertMutableUnchanged(mutable, before);

        var resetBaseline = Capture<InvalidOperationException>(
            () => rowData.Reset(replacement.GetRowData()));
        await Assert.That(resetBaseline.Message).Contains("Direct mutation of row data owned by a mutable model");
        await AssertMutableUnchanged(mutable, before);

        var setValue = Capture<InvalidOperationException>(
            () => rowData.SetValue(valueColumn, "rogue-secret"));
        await Assert.That(setValue.Message).Contains("Direct mutation of row data owned by a mutable model");
        await AssertMutableUnchanged(mutable, before);
    }

    [Test]
    public async Task InPlaceBinaryPrimaryKeyDrift_RejectsAgainstAuthoritativeMutableBaseline()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var callerOwnedId = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        var immutable = fixture.CreateBinaryImmutable(callerOwnedId);
        var mutable = new Mutable<TransactionMutationGuardBinaryRow>(immutable);
        var lifecycleBefore = mutable.Lifecycle;
        var authoritativeBefore = ((IMutableLifecycle)mutable).BaselineCanonicalPrimaryKey;
        var cachedPrimaryKeyBefore = mutable.PrimaryKeys();
        var hashBefore = mutable.GetHashCode();

        callerOwnedId[0] = 0xFF;

        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(((byte[])mutable["Id"]!).SequenceEqual(callerOwnedId)).IsTrue();
        await Assert.That(((byte[])authoritativeBefore.GetValue(0)!).SequenceEqual(callerOwnedId)).IsFalse();

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Update(mutable),
            "no longer match its authoritative baseline");

        await Assert.That(mutable.Lifecycle).IsEqualTo(lifecycleBefore);
        await Assert.That(((IMutableLifecycle)mutable).BaselineCanonicalPrimaryKey)
            .IsEqualTo(authoritativeBefore);
        await Assert.That(mutable.PrimaryKeys()).IsEqualTo(cachedPrimaryKeyBefore);
        await Assert.That(mutable.GetHashCode()).IsEqualTo(hashBefore);
        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(((byte[])mutable["Id"]!).SequenceEqual(callerOwnedId)).IsTrue();
    }

    [Test]
    public async Task GeneratedMutableIndexer_RejectsForeignColumnDefinitionWithMatchingOrdinalAndName()
    {
        using var fixture = new ProbeFixture();
        var mutable = fixture.CreateExistingMutable(271, "baseline");
        mutable["Value"] = "pending-secret";
        var foreignId = fixture.OtherTable.GetColumnByDbName("id");
        var ownId = fixture.Table.GetColumnByDbName("id");
        var before = CaptureMutable(mutable);
        var counts = fixture.Provider.Counts;

        await Assert.That(foreignId.DbName).IsEqualTo(ownId.DbName);
        await Assert.That(foreignId.Index).IsEqualTo(ownId.Index);
        await Assert.That(foreignId).IsNotSameReferenceAs(ownId);

        var read = Capture<ArgumentException>(() => { _ = mutable[foreignId]; });
        await Assert.That(read.Message).Contains("exact mapped column definition");
        await AssertMutableUnchanged(mutable, before);

        var write = Capture<ArgumentException>(() => mutable[foreignId] = 9999);
        await Assert.That(write.Message).Contains("exact mapped column definition");
        await AssertMutableUnchanged(mutable, before);
        await Assert.That(fixture.Provider.Counts).IsEqualTo(counts);
    }

    [Test]
    public async Task LegacyStateChange_ForeignSameNameColumnRejectsAndDoesNotExposeCapturedArray()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var ownId = fixture.Table.GetColumnByDbName("id");
        var foreignId = fixture.OtherTable.GetColumnByDbName("id");
        var model = new LegacyGuardMutable(
            fixture.Table,
            isNew: false,
            id: 281,
            value: "baseline",
            _ => [new(foreignId, 9999)]);
        var stateChange = new StateChange(
            model,
            fixture.Table,
            TransactionChangeType.Update);

        await Assert.That(foreignId.DbName).IsEqualTo(ownId.DbName);
        await Assert.That(foreignId.Index).IsEqualTo(ownId.Index);
        await Assert.That(foreignId).IsNotSameReferenceAs(ownId);

        var leakedArray = stateChange.GetChanges()
            as KeyValuePair<ColumnDefinition, object?>[];
        await Assert.That(leakedArray).IsNull();

        var callerCopy = stateChange.GetChanges().ToArray();
        callerCopy[0] = new KeyValuePair<ColumnDefinition, object?>(ownId, 1234);
        var retainedChange = stateChange.GetChanges().Single();
        await Assert.That(retainedChange.Key).IsSameReferenceAs(foreignId);
        await Assert.That(retainedChange.Value).IsEqualTo(9999);

        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.GetDbCommand(transaction),
            "not an exact mapped column");
        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => stateChange.ExecuteQuery(transaction),
            "not an exact mapped column");
    }

    [Test]
    [NotInParallel]
    public async Task InPlaceReferenceTypedIdDrift_RejectsAgainstCanonicalProviderBaseline()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var id = new TransactionMutationGuardReferenceId(291);
        var immutable = fixture.CreateReferenceIdImmutable(id, "baseline");
        var mutable = new Mutable<TransactionMutationGuardReferenceIdRow>(immutable);
        var converter = fixture.ReferenceIdConverter;
        var lifecycleBefore = mutable.Lifecycle;
        converter.Reset();

        id.Value = 9291;

        await Assert.That(mutable.GetChanges()).IsEmpty();
        await AssertGuardRejected(
            fixture.Provider,
            transaction,
            () => transaction.Update(mutable),
            "no longer match its authoritative baseline");

        await Assert.That(converter.ToProviderValues).Contains(9291);
        await Assert.That(mutable.Lifecycle).IsEqualTo(lifecycleBefore);
        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(mutable["Id"]).IsSameReferenceAs(id);
        await Assert.That(((TransactionMutationGuardReferenceId)mutable["Id"]!).Value)
            .IsEqualTo(9291);
    }

    [Test]
    [NotInParallel]
    public async Task ConvertedStateChange_CapturesCanonicalKeyOnceAndRendersPhysicalValueWithoutReconversion()
    {
        using var fixture = new ProbeFixture();
        using var transaction = fixture.Database.Transaction();
        var id = new TransactionMutationGuardReferenceId(301);
        var immutable = fixture.CreateReferenceIdImmutable(id, "baseline");
        var mutable = new Mutable<TransactionMutationGuardReferenceIdRow>(immutable);
        mutable["Value"] = "updated";
        var converter = fixture.ReferenceIdConverter;
        converter.Reset();

        var stateChange = new StateChange(
            mutable,
            fixture.ReferenceIdTable,
            TransactionChangeType.Update);

        await Assert.That(converter.ToProviderValues).IsEquivalentTo([301]);

        converter.Reset();
        transaction.EnsureMutationPreflight(stateChange);
        var validationConversions = converter.ToProviderValues.ToArray();
        await Assert.That(validationConversions.Length).IsGreaterThan(0);

        converter.Reset();
        fixture.Provider.Writer.Reset();
        _ = stateChange.GetQuery(transaction);

        await Assert.That(converter.ToProviderValues).IsEquivalentTo(validationConversions);
        var keyPhysicalConversion = fixture.Provider.Writer.Calls
            .Single(call => ReferenceEquals(
                call.Column,
                fixture.ReferenceIdTable.GetColumnByDbName("id")));
        await Assert.That(keyPhysicalConversion.Value).IsEqualTo(301);
        await Assert.That(keyPhysicalConversion.Value?.GetType()).IsEqualTo(typeof(int));
        await Assert.That(fixture.Provider.Counts.CommandCreations).IsEqualTo(0);
        await Assert.That(fixture.Provider.Counts.Executions).IsEqualTo(0);
    }

    private static void Invoke(
        Transaction transaction,
        Mutable<TransactionMutationGuardRow> mutable,
        GuardOperation operation)
    {
        switch (operation)
        {
            case GuardOperation.Insert:
                transaction.Insert(mutable);
                break;
            case GuardOperation.Update:
                transaction.Update(mutable);
                break;
            case GuardOperation.Save:
                transaction.Save(mutable);
                break;
            case GuardOperation.Delete:
                transaction.Delete(mutable);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static async Task<MutationGuardException> AssertGuardRejected(
        ProbeProvider provider,
        Transaction? transaction,
        Action action,
        string expectedMessage)
    {
        var before = provider.Counts;
        var changeCount = transaction?.Changes.Count;
        var exception = Capture<MutationGuardException>(action);

        await Assert.That(exception.Message).Contains(expectedMessage);
        await Assert.That(exception.Message).Contains("before provider command execution");
        await Assert.That(exception.Message).DoesNotContain(provider.ConnectionString);
        await Assert.That(provider.Counts).IsEqualTo(before);

        if (transaction is not null)
            await Assert.That(transaction.Changes.Count).IsEqualTo(changeCount!.Value);

        return exception;
    }

    private static MutableState CaptureMutable(Mutable<TransactionMutationGuardRow> mutable) =>
        new(
            mutable.Lifecycle,
            mutable.GetImmutableInstance(),
            mutable.PrimaryKeys(),
            mutable.GetHashCode(),
            mutable["Id"],
            mutable["Value"],
            mutable.GetChanges().ToArray());

    private static async Task AssertMutableUnchanged(
        Mutable<TransactionMutationGuardRow> mutable,
        MutableState before)
    {
        var changes = mutable.GetChanges().ToArray();

        await Assert.That(mutable.Lifecycle).IsEqualTo(before.Lifecycle);
        await Assert.That(mutable.GetImmutableInstance()).IsSameReferenceAs(before.Immutable);
        await Assert.That(mutable.PrimaryKeys()).IsEqualTo(before.PrimaryKey);
        await Assert.That(mutable.GetHashCode()).IsEqualTo(before.HashCode);
        await Assert.That(mutable["Id"]).IsEqualTo(before.Id);
        await Assert.That(mutable["Value"]).IsEqualTo(before.Value);
        await Assert.That(changes.Length).IsEqualTo(before.Changes.Length);

        for (var index = 0; index < changes.Length; index++)
        {
            await Assert.That(changes[index].Key).IsSameReferenceAs(before.Changes[index].Key);
            await Assert.That(changes[index].Value).IsEqualTo(before.Changes[index].Value);
        }
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private enum GuardOperation
    {
        Insert,
        Update,
        Save,
        Delete
    }

    private readonly record struct MutableState(
        MutableLifecycleSnapshot Lifecycle,
        TransactionMutationGuardRow? Immutable,
        DataLinqKey PrimaryKey,
        int HashCode,
        object? Id,
        object? Value,
        KeyValuePair<ColumnDefinition, object?>[] Changes);

    private sealed class ProbeFixture : IDisposable
    {
        internal ProbeFixture(string? name = null)
        {
            Provider = new ProbeProvider(name ?? $"probe-{Guid.NewGuid():N}");
            Database = new ProbeDatabase(Provider);
            Table = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardRow)).Table;
            OtherTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardOtherRow)).Table;
            BinaryTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardBinaryRow)).Table;
            ReferenceIdTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardReferenceIdRow)).Table;
            AutoTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardAutoRow)).Table;
        }

        internal ProbeProvider Provider { get; }
        internal ProbeDatabase Database { get; }
        internal TableDefinition Table { get; }
        internal TableDefinition OtherTable { get; }
        internal TableDefinition BinaryTable { get; }
        internal TableDefinition ReferenceIdTable { get; }
        internal TableDefinition AutoTable { get; }
        internal TransactionMutationGuardReferenceIdConverter ReferenceIdConverter =>
            (TransactionMutationGuardReferenceIdConverter)(
                ReferenceIdTable.GetColumnByDbName("id").ScalarConverter
                ?? throw new InvalidOperationException("Reference-ID converter metadata was not resolved."));

        internal TransactionMutationGuardRow CreateImmutable(int id, string value) =>
            InstanceFactory.NewImmutableRow<TransactionMutationGuardRow>(
                new ProbeRowData(Table, id, value),
                Provider.ReadOnlyAccess);

        internal Mutable<TransactionMutationGuardRow> CreateExistingMutable(int id, string value) =>
            new(CreateImmutable(id, value));

        internal Mutable<TransactionMutationGuardRow> CreateNewMutable(int id, string value)
        {
            var mutable = new Mutable<TransactionMutationGuardRow>();
            mutable["Id"] = id;
            mutable["Value"] = value;
            return mutable;
        }

        internal TransactionMutationGuardBinaryRow CreateBinaryImmutable(byte[] id) =>
            InstanceFactory.NewImmutableRow<TransactionMutationGuardBinaryRow>(
                new ProbeBinaryRowData(BinaryTable, id),
                Provider.ReadOnlyAccess);

        internal TransactionMutationGuardReferenceIdRow CreateReferenceIdImmutable(
            TransactionMutationGuardReferenceId id,
            string value) =>
            InstanceFactory.NewImmutableRow<TransactionMutationGuardReferenceIdRow>(
                new ProbeReferenceIdRowData(ReferenceIdTable, id, value),
                Provider.ReadOnlyAccess);

        public void Dispose() => Database.Dispose();
    }

    private sealed class ProbeDatabase(ProbeProvider provider)
        : Database<TransactionMutationGuardDb>(provider);

    private readonly record struct ProbeCounts(
        int CommandCreations,
        int WriterRequests,
        int Executions);

    private sealed class ProbeProvider : DatabaseProvider<TransactionMutationGuardDb>
    {
        private readonly ProbeDatabaseAccess databaseAccess;
        private readonly ProbeWriter writer = new();
        private int commandCreations;
        private int writerRequests;
        private int executions;

        internal ProbeProvider(string name)
            : base(
                $"probe-connection-secret-{name}",
                DatabaseType.SQLite,
                DataLinqLoggingConfiguration.NullConfiguration,
                name)
        {
            databaseAccess = new ProbeDatabaseAccess(this);
        }

        internal ProbeCounts Counts => new(commandCreations, writerRequests, executions);
        internal ProbeWriter Writer => writer;
        internal void RecordExecution() => executions++;

        public override IDatabaseProviderConstants Constants { get; } = new ProbeConstants();
        public override DatabaseAccess DatabaseAccess => databaseAccess;

        public override IDbCommand ToDbCommand(IQuery query)
        {
            commandCreations++;
            throw new ProviderBoundaryReachedException("A provider command was created.");
        }

        public override IDataLinqDataWriter GetWriter()
        {
            writerRequests++;
            return writer;
        }

        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) =>
            new ProbeDatabaseTransaction(this, type);

        public override DatabaseTransaction AttachDatabaseTransaction(
            IDbTransaction dbTransaction,
            TransactionType type) =>
            throw new NotSupportedException();

        public override string GetLastIdQuery() => throw new NotSupportedException();
        public override string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public override string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public override Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public override Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public override string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public override Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new NotSupportedException();
        public override Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public override Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public override Sql GetCreateSql() => throw new NotSupportedException();
        public override bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public override bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public override bool FileOrServerExists() => throw new NotSupportedException();
        public override IDbConnection GetDbConnection() => throw new NotSupportedException();
    }

    private sealed class ProbeDatabaseAccess(ProbeProvider provider) : DatabaseAccess(provider)
    {
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => Reached<IDataLinqDataReader>();
        public override IDataLinqDataReader ExecuteReader(string query) => Reached<IDataLinqDataReader>();
        public override object? ExecuteScalar(IDbCommand command) => Reached<object?>();
        public override T ExecuteScalar<T>(IDbCommand command) => Reached<T>();
        public override object? ExecuteScalar(string query) => Reached<object?>();
        public override T ExecuteScalar<T>(string query) => Reached<T>();
        public override int ExecuteNonQuery(IDbCommand command) => Reached<int>();
        public override int ExecuteNonQuery(string query) => Reached<int>();

        private T Reached<T>()
        {
            provider.RecordExecution();
            throw new ProviderBoundaryReachedException("A provider command was executed.");
        }
    }

    private sealed class ProbeDatabaseTransaction(
        ProbeProvider provider,
        TransactionType type) : DatabaseTransaction(provider, type)
    {
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => Reached<IDataLinqDataReader>();
        public override IDataLinqDataReader ExecuteReader(string query) => Reached<IDataLinqDataReader>();
        public override object? ExecuteScalar(IDbCommand command) => Reached<object?>();
        public override T ExecuteScalar<T>(IDbCommand command) => Reached<T>();
        public override object? ExecuteScalar(string query) => Reached<object?>();
        public override T ExecuteScalar<T>(string query) => Reached<T>();
        public override int ExecuteNonQuery(IDbCommand command) => Reached<int>();
        public override int ExecuteNonQuery(string query) => Reached<int>();

        public override void Commit() => SetStatus(DatabaseTransactionStatus.Committed);
        public override void Rollback() => SetStatus(DatabaseTransactionStatus.RolledBack);
        public override void Dispose() { }

        private T Reached<T>()
        {
            provider.RecordExecution();
            throw new ProviderBoundaryReachedException("A transactional provider command was executed.");
        }
    }

    private sealed class ProbeConstants : IDatabaseProviderConstants
    {
        public string ParameterSign => "@";
        public string LastInsertCommand => string.Empty;
        public string EscapeCharacter => "\"";
        public bool SupportsMultipleDatabases => false;
    }

    private sealed class ProbeWriter : IDataLinqDataWriter
    {
        internal List<(ColumnDefinition Column, object? Value)> Calls { get; } = [];

        public object? ConvertValue(ColumnDefinition column, object? value)
        {
            Calls.Add((column, value));
            return value;
        }

        internal void Reset() => Calls.Clear();
    }

    private sealed class ProbeRowData : IRowData
    {
        private readonly object?[] values;

        internal ProbeRowData(TableDefinition table, int id, string value)
        {
            Table = table;
            values = new object?[table.ColumnCount];
            values[table.GetColumnByDbName("id").Index] = id;
            values[table.GetColumnByDbName("value").Index] = value;
        }

        public TableDefinition Table { get; }
        public object? this[ColumnDefinition column] => values[column.Index];
        public object? this[int columnIndex] => values[columnIndex];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, this[column]));
    }

    private sealed class ForeignMetadataMutable(ModelDefinition metadata)
        : Mutable<TransactionMutationGuardRow>(metadata);

    private sealed class LegacyGuardMutable : IMutableInstance
    {
        private readonly TableDefinition table;
        private readonly Dictionary<ColumnDefinition, object?> values;
        private readonly ProbeRowData rowData;
        private readonly bool isNew;
        private readonly Func<int, IReadOnlyList<KeyValuePair<ColumnDefinition, object?>>> changesFactory;
        private int changeReads;
        private bool deleted;

        internal LegacyGuardMutable(
            TableDefinition table,
            bool isNew,
            int id,
            string value,
            Func<int, IReadOnlyList<KeyValuePair<ColumnDefinition, object?>>> changesFactory)
        {
            this.table = table;
            this.isNew = isNew;
            this.changesFactory = changesFactory;
            values = new Dictionary<ColumnDefinition, object?>
            {
                [table.GetColumnByDbName("id")] = id,
                [table.GetColumnByDbName("value")] = value
            };
            rowData = new ProbeRowData(table, id, value);
        }

        internal int ChangeReads => changeReads;

        public object? this[string propertyName]
        {
            get => this[table.Model.ValueProperties[propertyName].Column];
            set => this[table.Model.ValueProperties[propertyName].Column] = value;
        }

        public object? this[ColumnDefinition column]
        {
            get => values[column];
            set => values[column] = value;
        }

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => values;
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, values[column]));
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges() =>
            changesFactory(++changeReads);
        public bool HasPrimaryKeysSet() => true;
        public ModelDefinition Metadata() => table.Model;
        public DataLinqKey PrimaryKeys() =>
            DataLinqKey.FromValue(values[table.GetColumnByDbName("id")]);
        public MutableRowData GetRowData() => throw new NotSupportedException();
        IRowData IModelInstance.GetRowData() => rowData;
        public bool IsNew() => isNew;
        public bool IsDeleted() => deleted;
        public void SetDeleted() => deleted = true;
        public void Reset() { }
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public void SetLazy<V>(string name, V value) { }
    }

    private sealed class ProbeBinaryRowData : IRowData
    {
        private readonly object?[] values;

        internal ProbeBinaryRowData(TableDefinition table, byte[] id)
        {
            Table = table;
            values = new object?[table.ColumnCount];
            values[table.GetColumnByDbName("id").Index] = id;
        }

        public TableDefinition Table { get; }
        public object? this[ColumnDefinition column] => values[column.Index];
        public object? this[int columnIndex] => values[columnIndex];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, this[column]));
    }

    private sealed class ProbeReferenceIdRowData : IRowData
    {
        private readonly object?[] values;

        internal ProbeReferenceIdRowData(
            TableDefinition table,
            TransactionMutationGuardReferenceId id,
            string value)
        {
            Table = table;
            values = new object?[table.ColumnCount];
            values[table.GetColumnByDbName("id").Index] = id;
            values[table.GetColumnByDbName("value").Index] = value;
        }

        public TableDefinition Table { get; }
        public object? this[ColumnDefinition column] => values[column.Index];
        public object? this[int columnIndex] => values[columnIndex];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, this[column]));
    }

    private sealed class ProviderBoundaryReachedException(string message) : Exception(message);
}

[Database("transaction_mutation_guard")]
public sealed partial class TransactionMutationGuardDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<TransactionMutationGuardRow> Rows { get; } = new(dataSource);
    public DbRead<TransactionMutationGuardOtherRow> OtherRows { get; } = new(dataSource);
    public DbRead<TransactionMutationGuardBinaryRow> BinaryRows { get; } = new(dataSource);
    public DbRead<TransactionMutationGuardReferenceIdRow> ReferenceIdRows { get; } = new(dataSource);
    public DbRead<TransactionMutationGuardAutoRow> AutoRows { get; } = new(dataSource);
}

[Table("transaction_mutation_guard_rows")]
public abstract partial class TransactionMutationGuardRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<TransactionMutationGuardRow, TransactionMutationGuardDb>(rowData, dataSource),
      ITableModel<TransactionMutationGuardDb>
{
    [PrimaryKey]
    [Column("id")]
    [Type(DatabaseType.SQLite, "integer")]
    public abstract int Id { get; }

    [Column("value")]
    [Type(DatabaseType.SQLite, "text")]
    public abstract string Value { get; }
}

[Table("transaction_mutation_guard_other_rows")]
public abstract partial class TransactionMutationGuardOtherRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<TransactionMutationGuardOtherRow, TransactionMutationGuardDb>(rowData, dataSource),
      ITableModel<TransactionMutationGuardDb>
{
    [PrimaryKey]
    [Column("id")]
    [Type(DatabaseType.SQLite, "integer")]
    public abstract int Id { get; }
}

[Table("transaction_mutation_guard_auto_rows")]
public abstract partial class TransactionMutationGuardAutoRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<TransactionMutationGuardAutoRow, TransactionMutationGuardDb>(rowData, dataSource),
      ITableModel<TransactionMutationGuardDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Column("id")]
    [Type(DatabaseType.SQLite, "integer")]
    public abstract int? Id { get; }

    [Column("value")]
    [Type(DatabaseType.SQLite, "text")]
    public abstract string Value { get; }
}

[Table("transaction_mutation_guard_binary_rows")]
public abstract partial class TransactionMutationGuardBinaryRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<TransactionMutationGuardBinaryRow, TransactionMutationGuardDb>(rowData, dataSource),
      ITableModel<TransactionMutationGuardDb>
{
    [PrimaryKey]
    [Column("id")]
    [Type(DatabaseType.SQLite, "blob")]
    public abstract byte[] Id { get; }
}

public sealed class TransactionMutationGuardReferenceId(int value)
{
    public int Value { get; set; } = value;
}

public sealed class TransactionMutationGuardReferenceIdConverter
    : DataLinqScalarConverter<TransactionMutationGuardReferenceId, int>
{
    public List<int> ToProviderValues { get; } = [];

    public override int ToProvider(
        TransactionMutationGuardReferenceId modelValue,
        in ScalarConversionContext context)
    {
        ToProviderValues.Add(modelValue.Value);
        return modelValue.Value;
    }

    public override TransactionMutationGuardReferenceId FromProvider(
        int providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);

    public void Reset() => ToProviderValues.Clear();
}

[Table("transaction_mutation_guard_reference_id_rows")]
public abstract partial class TransactionMutationGuardReferenceIdRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<TransactionMutationGuardReferenceIdRow, TransactionMutationGuardDb>(rowData, dataSource),
      ITableModel<TransactionMutationGuardDb>
{
    [PrimaryKey]
    [Column("id")]
    [Type(DatabaseType.SQLite, "integer")]
    [ScalarConverter(typeof(TransactionMutationGuardReferenceIdConverter))]
    public abstract TransactionMutationGuardReferenceId Id { get; }

    [Column("value")]
    [Type(DatabaseType.SQLite, "text")]
    public abstract string Value { get; }
}
