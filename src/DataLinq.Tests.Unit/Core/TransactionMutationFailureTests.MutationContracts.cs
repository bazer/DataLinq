using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("base-insert")]
    [Arguments("typed-insert")]
    [Arguments("base-update")]
    [Arguments("typed-update")]
    [Arguments("immutable-update")]
    [Arguments("base-save-null")]
    [Arguments("immutable-save-null")]
    [Arguments("base-save-existing")]
    [Arguments("immutable-save-existing")]
    [Arguments("typed-save-new")]
    [Arguments("typed-save-existing")]
    public async Task MutationContract_EditFamiliesCaptureOnceBeforeSuspension(string family)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var inserting = family.Contains("insert") || family.EndsWith("null") || family.EndsWith("new");
        var immutable = fixture.CreateImmutable(1, "old");
        var typed = new MutableTransactionMutationGuardRow { Id = 1, Value = "new" };
        if (!inserting) typed.Reset(immutable);
        else typed.Id = 1;
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 1 };
        EnableAsyncMutations(fixture, access, [1, "stored"]);
        var edits = 0;
        Mutable<TransactionMutationGuardRow>? edited = null;
        void Edit(Mutable<TransactionMutationGuardRow> value)
        {
            edits++;
            edited = value;
            if (value.IsNew()) value["Id"] = 1;
            value["Value"] = "edited";
        }
        var work = family switch
        {
            "base-insert" => transaction.InsertAsyncCore((Mutable<TransactionMutationGuardRow>)typed, Edit),
            "typed-insert" => transaction.InsertAsyncCore<TransactionMutationGuardRow, MutableTransactionMutationGuardRow>(typed, value => { Edit(value); value.Value = "typed"; }),
            "base-update" => transaction.UpdateAsyncCore((Mutable<TransactionMutationGuardRow>)typed, Edit),
            "typed-update" => transaction.UpdateAsyncCore<TransactionMutationGuardRow, MutableTransactionMutationGuardRow>(typed, value => { Edit(value); value.Value = "typed"; }),
            "immutable-update" => transaction.UpdateAsyncCore(immutable, Edit),
            "base-save-null" => transaction.SaveAsyncCore((Mutable<TransactionMutationGuardRow>?)null, Edit),
            "immutable-save-null" => transaction.SaveAsyncCore((TransactionMutationGuardRow?)null, Edit),
            "base-save-existing" => transaction.SaveAsyncCore((Mutable<TransactionMutationGuardRow>)typed, Edit),
            "immutable-save-existing" => transaction.SaveAsyncCore(immutable, Edit),
            _ => transaction.SaveAsyncCore<TransactionMutationGuardRow, MutableTransactionMutationGuardRow>(typed, value => { Edit(value); value.Value = "typed"; })
        };
        try
        {
            // No caller await is needed to run edits, capture and start dispatch.
            await Assert.That(edits).IsEqualTo(1);
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            _ = Capture<InvalidOperationException>(() => edited!["Value"] = "conflict");
            if (ReferenceEquals(typed, edited)) _ = Capture<InvalidOperationException>(() => typed.Value = "conflict");
        }
        finally { access.Dispatch.Release(); }
        await Assert.That((await work).Value).IsEqualTo("stored");
        await Assert.That(transaction.Changes.Single().Type).IsEqualTo(inserting ? TransactionChangeType.Insert : TransactionChangeType.Update);
        await Assert.That(edited!.HasChanges()).IsFalse();
        edited["Value"] = "released";
    }

    [Test]
    [Arguments("direct-null")]
    [Arguments("typed-null")]
    [Arguments("null-edits")]
    [Arguments("readonly")]
    [Arguments("new-update")]
    [Arguments("invalid-after-edits")]
    public async Task MutationContract_InvalidEditsPrecedeCancellationWithoutPoisoning(string invalid)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction(invalid == "readonly" ? TransactionType.ReadOnly : TransactionType.ReadAndWrite);
        var mutable = invalid == "new-update" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
        using var cancellation = new CancellationTokenSource();
        if (invalid != "invalid-after-edits") cancellation.Cancel();
        var edits = 0;
        var factory = EnableAsyncMutations(fixture);
        void Edit(Mutable<TransactionMutationGuardRow> value)
        {
            edits++;
            value["Id"] = 2;
            cancellation.Cancel();
        }
        var error = await AsyncEnumerationFailureOf(() => invalid switch
        {
            "direct-null" => transaction.SaveAsyncCore((Mutable<TransactionMutationGuardRow>)null!, cancellation.Token),
            "typed-null" => transaction.SaveAsyncCore<TransactionMutationGuardRow, MutableTransactionMutationGuardRow>(null!, _ => edits++, cancellation.Token),
            "null-edits" => transaction.SaveAsyncCore(mutable, (Action<Mutable<TransactionMutationGuardRow>>)null!, cancellation.Token),
            _ => transaction.UpdateAsyncCore(mutable, Edit, cancellation.Token)
        });
        await Assert.That(error is OperationCanceledException).IsFalse();
        await Assert.That(edits).IsEqualTo(invalid == "invalid-after-edits" ? 1 : 0);
        await Assert.That(factory.Inputs).IsEmpty();
        await Assert.That(transaction.IsPoisoned).IsFalse();
        if (invalid == "invalid-after-edits") await Assert.That(mutable["Id"]).IsEqualTo(2);
    }

    [Test]
    public async Task MutationContract_SaveSelectsResultingLifecycleAfterLocalEdits()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = new Mutable<TransactionMutationGuardRow>();
        EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var result = await transaction.SaveAsyncCore(mutable, value =>
        {
            value.Reset(fixture.CreateImmutable(1, "old"));
            value["Value"] = "edited existing row";
        });
        await Assert.That(result.Value).IsEqualTo("stored");
        await Assert.That(transaction.Changes.Single().Type).IsEqualTo(TransactionChangeType.Update);
    }

    [Test]
    [Arguments("insert")]
    [Arguments("update")]
    [Arguments("delete")]
    [Arguments("generated")]
    public async Task MutationContract_LegacyStateChangeKeepsNarrowLifecycleAndSingleAttempt(string kind)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        IMutableInstance underlying;
        if (kind == "generated") underlying = fixture.CreateNewAutoMutable("submitted");
        else
        {
            var row = kind == "insert" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
            if (row.IsNew()) row["Id"] = 1;
            row["Value"] = "submitted";
            underlying = row;
        }
        var legacy = new LegacyAsyncMutable(underlying);
        var change = new StateChange(legacy, legacy.Metadata().Table,
            kind is "insert" or "generated" ? TransactionChangeType.Insert : kind == "delete" ? TransactionChangeType.Delete : TransactionChangeType.Update);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 1, ScalarResult = 42L };
        var factory = EnableAsyncMutations(fixture, access);
        fixture.Scenario.AsyncSqlReaders = null; // Legacy StateChange never promises managed hydration.
        var work = change.ExecuteQueryAsyncCore(transaction);
        try
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            using var competing = fixture.Database.Transaction();
            var candidate = new StateChange(legacy, change.Table, change.Type);
            await Assert.That(await AsyncEnumerationFailureOf(() => candidate.ExecuteQueryAsyncCore(competing))).IsTypeOf<InvalidOperationException>();
        }
        finally { access.Dispatch.Release(); }
        await work;
        await Assert.That(transaction.Changes.Single()).IsSameReferenceAs(change);
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        if (kind == "generated")
        {
            await Assert.That(legacy["Id"]).IsEqualTo(42);
            await Assert.That(change.PrimaryKeys).IsEqualTo(DataLinqKey.FromValue(42));
            await Assert.That(legacy.IsNew()).IsTrue();
        }
        await Assert.That(legacy.IsDeleted()).IsEqualTo(kind == "delete");
        await Assert.That(await AsyncEnumerationFailureOf(() => change.ExecuteQueryAsyncCore(transaction))).IsTypeOf<MutationGuardException>();
        await Assert.That(factory.Commands.Count).IsEqualTo(1);
        legacy["Value"] = "released";
        transaction.Commit();
    }

    [Test]
    [Arguments("before-call")]
    [Arguments("binding")]
    [Arguments("after-dispatch")]
    public async Task MutationContract_LegacyDriftSeparatesPreExecutionFromPostWriteFailure(string when)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var legacy = new LegacyAsyncMutable(fixture.CreateExistingMutable(1, "old"));
        legacy["Value"] = "captured";
        var change = new StateChange(legacy, fixture.RowTable, TransactionChangeType.Update);
        var access = new ControlledAsyncDatabaseAccess(new(paused: when == "after-dispatch"))
        {
            NonQueryResult = 1, FailureEvidence = TrustedScalarRead
        };
        var factory = EnableAsyncMutations(fixture, access);
        if (when == "before-call") legacy["Value"] = "drift";
        if (when == "binding") factory.ConfigureCommand = _ => legacy["Value"] = "drift";
        var work = change.ExecuteQueryAsyncCore(transaction);
        if (when == "after-dispatch")
        {
            try
            {
                await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                legacy["Value"] = "drift"; // Arbitrary custom setters cannot be intercepted.
                await Assert.That(factory.Inputs.Single().ToSql().Parameters.Any(x => Equals(x.Value, "captured"))).IsTrue();
            }
            finally { access.Dispatch.Release(); }
        }
        _ = await AsyncEnumerationFailureOf(() => work);
        await Assert.That(transaction.IsPoisoned).IsEqualTo(when == "after-dispatch");
        await Assert.That(access.Calls.Any(x => x.StartsWith("dispatch:"))).IsEqualTo(when == "after-dispatch");
        await Assert.That(transaction.Changes).IsEmpty();
        MutationInputReservation.EnsureAvailable(legacy);
        if (when == "after-dispatch") await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsFalse();
    }

    [Test]
    [Arguments("pending-cache")]
    [Arguments("guarded-setter")]
    [Arguments("custom-delete")]
    [Arguments("missing-row")]
    [Arguments("wrong-key")]
    [Arguments("duplicate-row")]
    public async Task MutationContract_FinalizationFailuresPoisonWithoutRecordingSuccess(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var prior = fixture.CreateExistingMutable(9, "prior");
        transaction.Delete(prior);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var expected = new Exception(phase);
        EnableAsyncMutations(fixture, rows: phase switch
        {
            "wrong-key" => [[2, "stored"]],
            "duplicate-row" => [[1, "stored"], [1, "stored"]],
            _ => []
        });
        if (phase is "pending-cache" or "guarded-setter")
            fixture.RowCache.SubscribeToChanges(new MutatingNotification(() =>
            {
                if (phase == "guarded-setter") mutable["Value"] = "forbidden";
                else throw expected;
            }), transaction);
        var legacy = new LegacyAsyncMutable(mutable) { Deleting = () => throw expected };
        var error = await AsyncEnumerationFailureOf(() => phase == "custom-delete"
            ? transaction.DeleteAsyncCore(legacy) : transaction.UpdateAsyncCore(mutable));
        if (phase is "pending-cache" or "custom-delete") await Assert.That(error).IsSameReferenceAs(expected);
        if (phase == "missing-row") await Assert.That(error).IsTypeOf<ModelLoadFailureException>();
        await Assert.That(transaction.Failure!.Stage).IsEqualTo(phase switch
        {
            "custom-delete" => TransactionFailureStage.LifecycleFinalization,
            "missing-row" or "wrong-key" or "duplicate-row" => TransactionFailureStage.Hydration,
            _ => TransactionFailureStage.PendingCacheApplication
        });
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(prior.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        if (phase != "custom-delete") await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable["Value"]).IsEqualTo("submitted");
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsFalse();
        MutationInputReservation.EnsureAvailable(legacy);
        mutable["Value"] = "released";
    }

    [Test]
    [Arguments("success")]
    [Arguments("generated-conversion")]
    [Arguments("hydration-conversion")]
    public async Task MutationContract_ConvertedGeneratedKeyKeepsProviderIdentityAndFailureBoundary(string phase)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncMutationContractDb>(scenario);
        using var transaction = provider.StartTransaction();
        using var observation = new TransactionMutationGuardReferenceIdConverter.Observation();
        var expected = new Exception(phase);
        observation.Materializing = () =>
        {
            if ((phase == "generated-conversion" && observation.FromProviderCalls == 1) ||
                (phase == "hydration-conversion" && observation.FromProviderCalls == 2)) throw expected;
        };
        var mutable = new MutableAsyncMutationConvertedRow { Value = "submitted" };
        var access = new ControlledAsyncDatabaseAccess { ScalarResult = 42L };
        scenario.AsyncMutations = new ControlledMutationCommandFactory { CreateAccess = _ => access };
        var reads = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([42, "stored"]) { ColumnNames = ["id", "value"] }, FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = reads;
        var work = transaction.InsertAsyncCore((Mutable<AsyncMutationConvertedRow>)mutable);
        if (phase == "success")
        {
            var result = await work;
            await Assert.That(result.Id!.Value).IsEqualTo(42);
            await Assert.That(mutable.Id!.Value).IsEqualTo(42);
            await Assert.That(observation.FromProviderCalls).IsEqualTo(2);
            await Assert.That(transaction.Changes.Single().PrimaryKeys).IsEqualTo(DataLinqKey.FromValue(42));
            await Assert.That(reads.Inputs.Single().ToSql().Parameters.Single().Value).IsEqualTo(42);
            await Assert.That(mutable.HasChanges()).IsFalse();
        }
        else
        {
            var error = await AsyncEnumerationFailureOf(() => work);
            await Assert.That(error.ToString()).Contains(expected.Message);
            await Assert.That(transaction.IsPoisoned).IsTrue();
            await Assert.That(transaction.Failure!.Stage).IsEqualTo(TransactionFailureStage.Hydration);
            await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
            await Assert.That(transaction.Changes).IsEmpty();
        }
        mutable.Value = "released";
        await Assert.That(scenario.ScalarExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task MutationContract_CompositeKeyUsesAuthoritativeHydration()
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        using var transaction = provider.StartTransaction();
        var mutable = new MutableAsyncModelCompositeRow { First = 1, Second = 2, Value = "submitted" };
        scenario.AsyncMutations = new ControlledMutationCommandFactory();
        var reads = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, 2, "stored"]) { ColumnNames = ["first", "second", "value"] } }
        };
        scenario.AsyncSqlReaders = reads;
        var result = await transaction.InsertAsyncCore((Mutable<AsyncModelCompositeRow>)mutable);
        await Assert.That(result.Value).IsEqualTo("stored");
        await Assert.That(mutable.Value).IsEqualTo("stored");
        await Assert.That(transaction.Changes.Single().PrimaryKeys).IsEqualTo(DataLinqKey.FromValues([1, 2]));
        await Assert.That(reads.Inputs.Single().ToSql().Parameters.Select(x => x.Value).SequenceEqual([1, 2])).IsTrue();
        await Assert.That(mutable.HasChanges()).IsFalse();
    }

    [Test]
    [Arguments(TransactionType.ReadAndWrite)]
    [Arguments(TransactionType.WriteOnly)]
    public async Task MutationContract_FinalRelationImpactFreezesAuthoritativeValues(TransactionType type)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction(type);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var change = new StateChange(mutable, fixture.RowTable, TransactionChangeType.Update);
        await change.ExecuteQueryAsyncCore(transaction);
        var index = fixture.RowTable.ColumnIndices.Single(value => value.Name == "idx_transaction_mutation_guard_value");
        mutable["Value"] = "later edits";
        await Assert.That(change.GetCurrentRelationKey(index)).IsEqualTo(DataLinqKey.FromValue("stored"));
        await Assert.That(change.TryGetOriginalValue(fixture.RowTable.GetColumnByDbName("value"), out var before)).IsTrue();
        await Assert.That(before).IsEqualTo("old");
        await Assert.That(transaction.Changes.Single()).IsSameReferenceAs(change);
    }

    [Test]
    [Arguments("empty")]
    [Arguments("canceled")]
    [Arguments("readonly")]
    public async Task MutationContract_EmptyBatchValidatesWithoutBindingOrInitializing(string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction(mode == "readonly" ? TransactionType.ReadOnly : TransactionType.ReadAndWrite);
        using var cancellation = new CancellationTokenSource();
        if (mode != "empty") cancellation.Cancel();
        // No async capabilities have been configured: empty work must not require them.
        var pending = transaction.InsertAsyncCore(Array.Empty<Mutable<TransactionMutationGuardRow>>(), cancellation.Token);
        if (mode == "empty") await Assert.That(await pending).IsEmpty();
        else
        {
            var error = await AsyncEnumerationFailureOf(() => pending);
            if (mode == "readonly") await Assert.That(error).IsTypeOf<MutationGuardException>();
            else await Assert.That(error).IsTypeOf<OperationCanceledException>();
        }
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task MutationContract_FiniteBatchReturnsCompleteInputOrderAndFinalizedModels()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var inputs = new[] { 3, 1, 2 }.Select(id => new MutableTransactionMutationGuardRow { Id = id, Value = "submitted" }).ToArray();
        var enumerations = 0;
        IEnumerable<Mutable<TransactionMutationGuardRow>> Models()
        {
            enumerations++;
            foreach (var value in inputs) yield return value;
        }
        fixture.Scenario.AsyncMutations = new ControlledMutationCommandFactory();
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
        {
            CreateAccess = sql => new()
            {
                ReaderOverride = new ControlledRowDataReader([sql.ToSql().Parameters.Single().Value, "stored"]) { ColumnNames = ["id", "value"] }
            }
        };
        var rows = await transaction.InsertAsyncCore(Models());
        await Assert.That(enumerations).IsEqualTo(1);
        await Assert.That(rows.Select(row => row.Id).SequenceEqual([3, 1, 2])).IsTrue();
        await Assert.That(transaction.Changes.Select(change => (int)change.PrimaryKeys.GetValue(0)!).SequenceEqual([3, 1, 2])).IsTrue();
        await Assert.That(inputs.All(input => input.Value == "stored" && !input.HasChanges())).IsTrue();
        foreach (var input in inputs) input.Value = "released";
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MutationContract_UnstartedStateChangeCanRetryAfterCancellationOrCapabilityFailure(bool invalidCapability)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var change = new StateChange(mutable, fixture.RowTable, TransactionChangeType.Update);
        var factory = EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var expected = new NotSupportedException("explicit async capability");
        if (invalidCapability) factory.ConfigureCommand = command => command.ValidationFailure = expected;
        var error = await AsyncEnumerationFailureOf(() => change.ExecuteQueryAsyncCore(transaction, cancellation.Token));
        if (invalidCapability) await Assert.That(error).IsSameReferenceAs(expected);
        else await Assert.That(error).IsTypeOf<OperationCanceledException>();
        await Assert.That(change.HasExecutionAttempted).IsFalse();
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(0);
        MutationInputReservation.EnsureAvailable(mutable);
        factory.ConfigureCommand = null;
        await change.ExecuteQueryAsyncCore(transaction);
        await Assert.That(transaction.Changes.Single()).IsSameReferenceAs(change);
        await Assert.That(mutable["Value"]).IsEqualTo("stored");
        await Assert.That(change.HasExecutionAttempted).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MutationContract_InvalidGeneratedValuePoisonsBeforeHydration(bool overflow)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateNewAutoMutable("submitted");
        var access = new ControlledAsyncDatabaseAccess { ScalarResult = overflow ? long.MaxValue : null };
        EnableAsyncMutations(fixture, access);
        var reads = new ControlledSqlReaderFactory { CreateAccess = _ => throw new Exception("Generated-value failure must precede hydration.") };
        fixture.Scenario.AsyncSqlReaders = reads;
        _ = await AsyncEnumerationFailureOf(() => transaction.InsertAsyncCore(mutable));
        await Assert.That(reads.Inputs).IsEmpty();
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(transaction.Failure!.Stage).IsEqualTo(TransactionFailureStage.Hydration);
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(transaction.Changes).IsEmpty();
        mutable["Value"] = "released";
    }

    private sealed class LegacyAsyncMutable(IMutableInstance inner) : IMutableInstance
    {
        internal Action? Deleting { get; init; }
        public object? this[string propertyName] { get => inner[propertyName]; set => inner[propertyName] = value; }
        public object? this[ColumnDefinition column] { get => inner[column]; set => inner[column] = value; }
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => inner.GetValues();
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => inner.GetValues(columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges() => inner.GetChanges();
        public bool HasPrimaryKeysSet() => inner.HasPrimaryKeysSet();
        public ModelDefinition Metadata() => inner.Metadata();
        public DataLinqKey PrimaryKeys() => inner.PrimaryKeys();
        public MutableRowData GetRowData() => inner.GetRowData();
        IRowData IModelInstance.GetRowData() => ((IModelInstance)inner).GetRowData();
        public bool IsNew() => inner.IsNew();
        public bool IsDeleted() => inner.IsDeleted();
        public void SetDeleted() { Deleting?.Invoke(); inner.SetDeleted(); }
        public void Reset() => inner.Reset();
        public void ClearLazy() => inner.ClearLazy();
        public V? GetLazy<V>(string name, Func<V> fetchCode) => inner.GetLazy(name, fetchCode);
        public void SetLazy<V>(string name, V value) => inner.SetLazy(name, value);
    }
}

[Database("async_mutation_contract")]
public sealed partial class AsyncMutationContractDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<AsyncMutationConvertedRow> Rows { get; } = new(source);
}

[Table("async_mutation_converted")]
public abstract partial class AsyncMutationConvertedRow(IRowData rowData, IDataSourceAccess source)
    : Immutable<AsyncMutationConvertedRow, AsyncMutationContractDb>(rowData, source), ITableModel<AsyncMutationContractDb>
{
    [PrimaryKey, AutoIncrement, Nullable, Column("id"), Type(DatabaseType.SQLite, "integer")]
    [ScalarConverter(typeof(TransactionMutationGuardReferenceIdConverter))]
    public abstract TransactionMutationGuardReferenceId? Id { get; }
    [Column("value"), Type(DatabaseType.SQLite, "text")]
    public abstract string Value { get; }
}
