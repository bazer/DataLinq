using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RelationKey_FactoryCaptureCannotChangePredicateOrMembershipIdentity(bool keyless)
    {
        var scenario = new ScriptedMutationScenario();
        using DatabaseProvider provider = keyless
            ? new CapturedReadProvider<RelationViewDb>(scenario)
            : new CapturedReadProvider<RelationKeyDb>(scenario);
        var property = keyless
            ? provider.Metadata.GetTableModel(typeof(RelationViewChild)).Model.RelationProperties[nameof(RelationViewChild.Parent)]
            : provider.Metadata.GetTableModel(typeof(RelationKeyParent)).Model.RelationProperties[nameof(RelationKeyParent.Children)];
        var cache = provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);
        provider.State.Cache.CleanupScheduler?.Stop();
        var key = new MutableRelationProviderKey { Value = 1 };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(), FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = new CapturingRelationReaderFactory(factory, () => key.Value = 2);
        await Assert.That(await cache.GetRelationRowsAsyncCore(key, property, provider.ReadOnlyAccess)).IsEmpty();
        await Assert.That(key.Value).IsEqualTo(2);
        await Assert.That(factory.Inputs[0].ToSql().Parameters.Single().Value).IsEqualTo(1);
        key.Value = 1;
        scenario.AsyncSqlReaders = factory;
        await Assert.That(await cache.GetRelationRowsAsyncCore(key, property, provider.ReadOnlyAccess)).IsEmpty();
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(keyless ? 2 : 1);
        await Assert.That(cache.IndicesCount.Sum(index => index.count)).IsEqualTo(keyless ? 0 : 1);
    }

    private sealed class CapturingRelationReaderFactory(IAsyncSqlReaderFactory inner, Action capture) : IAsyncSqlReaderFactory
    {
        public IAsyncSqlReaderFactory CaptureInvocation() { capture(); return inner.CaptureInvocation(); }
        public IAsyncReaderSource BindReader(CapturedSql sql) => inner.BindReader(sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RelationKey_ConvertedRelationUsesCanonicalInputsAndWarmRowIdentity(bool transactional)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<RelationKeyDb>(scenario);
        using var transaction = provider.StartTransaction();
        using var conversions = new TransactionMutationGuardReferenceIdConverter.Observation();
        var source = transactional ? (DataSourceAccess)transaction : provider.ReadOnlyAccess;
        var parentTable = provider.Metadata.GetTableModel(typeof(RelationKeyParent)).Table;
        var childTable = provider.Metadata.GetTableModel(typeof(RelationKeyChild)).Table;
        provider.GetTableCache(childTable);
        provider.State.Cache.CleanupScheduler?.Stop();
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1]), FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = factory;
        var parent = (RelationKeyParent)(await provider.GetTableCache(parentTable).GetProviderRowAsyncCore(DataLinqKey.FromValue(1), source))!;
        var rows = new ControlledRowDataReader([10, 1], [11, 1]) { Cleanup = new(paused: true) };
        factory.CreateAccess = _ => new() { ReaderOverride = rows, FailureEvidence = TrustedScalarRead };
        await Assert.That(conversions.ToProviderValues).IsEmpty();
        var holder = (ImmutableRelation<RelationKeyChild>)parent.Children;
        // Generated navigation normalizes its model-valued key once when creating the holder.
        await Assert.That(conversions.ToProviderValues).IsEquivalentTo(new[] { 1 });
        var pending = holder.GetValuesAsyncCore();
        await rows.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(conversions.FromProviderCalls).IsEqualTo(1); // Only the parent is materialized so far.
            await Assert.That(pending.IsCompleted).IsFalse();
        }
        finally { rows.Cleanup.Release(); }
        var children = await pending;
        await Assert.That(children.Select(child => child.Id.Value).ToArray()).IsEquivalentTo(new[] { 10, 11 });
        await Assert.That(children.Select(child => child.ParentId.Value).ToArray()).IsEquivalentTo(new[] { 1, 1 });
        var property = childTable.Model.RelationProperties[nameof(RelationKeyChild.Parent)];
        var reference = new ImmutableForeignKey<RelationKeyParent>(DataLinqKey.FromValue(1), source, property);
        await Assert.That(await reference.GetRequiredValueAsyncCore()).IsSameReferenceAs(parent);
        var keyed = await holder.GetInstancesAsyncCore();
        await Assert.That(keyed[DataLinqKey.FromValue(10)]).IsSameReferenceAs(children[0]);
        await Assert.That(conversions.ToProviderValues).IsEquivalentTo(new[] { 1 });
        await Assert.That(conversions.FromProviderCalls).IsEqualTo(5);
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(2);
        await Assert.That(factory.Inputs[1].ToSql().Parameters.Single().Value).IsEqualTo(1);
        await Assert.That(scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RelationKey_ConversionFailureKeepsValidRowsButNeverPublishesPartialMembership(bool cancel)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<RelationKeyDb>(scenario);
        using var conversions = new TransactionMutationGuardReferenceIdConverter.Observation();
        using var cancellation = new CancellationTokenSource();
        var parentTable = provider.Metadata.GetTableModel(typeof(RelationKeyParent)).Table;
        var childTable = provider.Metadata.GetTableModel(typeof(RelationKeyChild)).Table;
        var cache = provider.GetTableCache(childTable);
        provider.State.Cache.CleanupScheduler?.Stop();
        var expected = new InvalidOperationException("second model conversion");
        conversions.Materializing = () =>
        {
            if (conversions.FromProviderCalls == 3)
            {
                if (cancel) cancellation.Cancel(); else throw expected;
            }
        };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([10, 1], [11, 1]), FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = factory;
        var property = parentTable.Model.RelationProperties[nameof(RelationKeyParent.Children)];
        var holder = new ImmutableRelation<RelationKeyChild>(DataLinqKey.FromValue(1), provider.ReadOnlyAccess, property);
        var failure = await AsyncEnumerationFailureOf(() => holder.GetValuesAsyncCore(cancellation.Token));
        if (cancel) await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure.InnerException).IsSameReferenceAs(expected);
        await Assert.That(cache.TryGetMaterializedRow(DataLinqKey.FromValue(10), provider.ReadOnlyAccess, out _)).IsTrue();
        await Assert.That(cache.IndicesCount.Sum(index => index.count)).IsEqualTo(0);
        conversions.Materializing = null;
        factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(), FailureEvidence = TrustedScalarRead };
        await Assert.That((await holder.GetValuesAsyncCore()).Length).IsEqualTo(0);
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RelationKey_ConstructorInvalidationCannotPublishRowsIndexOrHolder(bool transactional)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<RelationKeyDb>(scenario);
        using var transaction = provider.StartTransaction();
        using var conversions = new TransactionMutationGuardReferenceIdConverter.Observation();
        var source = transactional ? (DataSourceAccess)transaction : provider.ReadOnlyAccess;
        var parentTable = provider.Metadata.GetTableModel(typeof(RelationKeyParent)).Table;
        var cache = provider.GetTableCache(provider.Metadata.GetTableModel(typeof(RelationKeyChild)).Table);
        provider.State.Cache.CleanupScheduler?.Stop();
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([10, 1], [11, 1]), FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = factory;
        var property = parentTable.Model.RelationProperties[nameof(RelationKeyParent.Children)];
        var holder = new ImmutableRelation<RelationKeyChild>(DataLinqKey.FromValue(1), source, property);
        RelationKeyChild.Creating.Value = () => cache.ClearCache();
        try { await Assert.That((await holder.GetValuesAsyncCore()).Length).IsEqualTo(2); }
        finally { RelationKeyChild.Creating.Value = null; }
        await Assert.That(cache.RowCount).IsEqualTo(0);
        await Assert.That(cache.GetTransactionRows(transaction).Count()).IsEqualTo(0);
        await Assert.That(cache.IndicesCount.Sum(index => index.count)).IsEqualTo(0);
        factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(), FailureEvidence = TrustedScalarRead };
        await Assert.That((await holder.GetValuesAsyncCore()).Length).IsEqualTo(0);
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RelationKey_BinaryInputsAndRowsHaveIndependentOwnership(bool reference)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<RelationKeyDb>(scenario);
        var keyBytes = new byte[] { 1, 2 };
        var returnedKey = new byte[] { 1, 2 };
        var childId = new byte[] { 7, 8 };
        var reader = new ControlledRowDataReader(reference ? [[returnedKey]] : [[childId, returnedKey]]) { Cleanup = new(paused: true) };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead } };
        scenario.AsyncSqlReaders = factory;
        var parent = provider.Metadata.GetTableModel(typeof(RelationBinaryParent)).Table;
        var child = provider.Metadata.GetTableModel(typeof(RelationBinaryChild)).Table;
        var cache = provider.GetTableCache(reference ? parent : child);
        provider.State.Cache.CleanupScheduler?.Stop();
        Task<byte[]> pending;
        if (reference)
        {
            var holder = new ImmutableForeignKey<RelationBinaryParent>(DataLinqKey.FromValue(keyBytes), provider.ReadOnlyAccess,
                child.Model.RelationProperties[nameof(RelationBinaryChild.Parent)]);
            pending = LoadReference();
            async Task<byte[]> LoadReference() => (await holder.GetRequiredValueAsyncCore()).Id;
        }
        else
        {
            var holder = new ImmutableRelation<RelationBinaryChild>(DataLinqKey.FromValue(keyBytes), provider.ReadOnlyAccess,
                parent.Model.RelationProperties[nameof(RelationBinaryParent.Children)]);
            pending = LoadCollection();
            async Task<byte[]> LoadCollection() => (await holder.GetValuesAsyncCore()).Single().ParentId;
        }
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        keyBytes[0] = 99; returnedKey[0] = 88; childId[0] = 77;
        reader.Cleanup.Release();
        await Assert.That((await pending).SequenceEqual(new byte[] { 1, 2 })).IsTrue();
        await Assert.That(((byte[])factory.Inputs[0].ToSql().Parameters.Single().Value!).SequenceEqual(new byte[] { 1, 2 })).IsTrue();
        var actualKey = DataLinqKey.FromValue(reference ? new byte[] { 1, 2 } : new byte[] { 7, 8 });
        await Assert.That(cache.TryGetMaterializedRow(actualKey, provider.ReadOnlyAccess, out var cached)).IsTrue();
        await Assert.That(cached!.PrimaryKeys()).IsEqualTo(actualKey);
    }

    [Test]
    [Arguments(DatabaseType.SQLite, false)]
    [Arguments(DatabaseType.SQLite, true)]
    [Arguments(DatabaseType.MySQL, false)]
    [Arguments(DatabaseType.MySQL, true)]
    [Arguments(DatabaseType.MariaDB, false)]
    [Arguments(DatabaseType.MariaDB, true)]
    public async Task RelationKey_GuidCaptureUsesProviderStorageAndCanonicalCacheIdentity(DatabaseType databaseType, bool reference)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new RelationWriterProvider(scenario, databaseType);
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var reader = new ControlledRowDataReader(reference ? [[id]] : [[10, id]]) { Cleanup = new(paused: true) };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead } };
        scenario.AsyncSqlReaders = factory;
        var parent = provider.Metadata.GetTableModel(typeof(RelationGuidParent)).Table;
        var child = provider.Metadata.GetTableModel(typeof(RelationGuidChild)).Table;
        var cache = provider.GetTableCache(reference ? parent : child);
        provider.State.Cache.CleanupScheduler?.Stop();
        Func<Task<Guid>> load;
        if (reference)
        {
            var holder = new ImmutableForeignKey<RelationGuidParent, Guid>(id, provider.ReadOnlyAccess,
                child.Model.RelationProperties[nameof(RelationGuidChild.Parent)]);
            load = async () => (await holder.GetRequiredValueAsyncCore()).Id;
        }
        else
        {
            var holder = new ImmutableRelation<RelationGuidChild, Guid>(id, provider.ReadOnlyAccess,
                parent.Model.RelationProperties[nameof(RelationGuidParent.Children)]);
            load = async () => (await holder.GetValuesAsyncCore()).Single().ParentId;
        }
        var pending = load();
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            var parameter = factory.Inputs[0].ToSql().Parameters.Single().Value;
            if (databaseType == DatabaseType.MySQL)
                await Assert.That(((byte[])parameter!).SequenceEqual(id.ToByteArray(bigEndian: true))).IsTrue();
            else
                await Assert.That(parameter).IsEqualTo(id.ToString(databaseType == DatabaseType.SQLite ? "N" : "D"));
        }
        finally { reader.Cleanup.Release(); }
        await Assert.That(await pending).IsEqualTo(id);
        await Assert.That(await load()).IsEqualTo(id);
        var actualKey = reference ? DataLinqKey.FromValue(id) : DataLinqKey.FromValue(10);
        await Assert.That(cache.TryGetMaterializedRow(actualKey, provider.ReadOnlyAccess, out _)).IsTrue();
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RelationKey_CompositePrimaryKeyOrderingAndSourceOwnership(bool reference, bool transactional)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<RelationKeyDb>(scenario);
        using var transaction = provider.StartTransaction();
        var source = transactional ? (DataSourceAccess)transaction : provider.ReadOnlyAccess;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(reference ? [[7L, 42L]] : [[10, 7L, 42L]]), FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = factory;
        var parent = provider.Metadata.GetTableModel(typeof(RelationCompositeParent)).Table;
        var child = provider.Metadata.GetTableModel(typeof(RelationCompositeChild)).Table;
        var key = DataLinqKey.FromValues([7L, 42L]);
        var cache = provider.GetTableCache(reference ? parent : child);
        provider.State.Cache.CleanupScheduler?.Stop();
        IImmutableInstance result;
        if (reference)
        {
            var holder = new ImmutableForeignKey<RelationCompositeParent>(key, source, child.Model.RelationProperties[nameof(RelationCompositeChild.Parent)]);
            var row = await holder.GetRequiredValueAsyncCore();
            await Assert.That(row.Tenant).IsEqualTo(7L);
            await Assert.That(row.Id).IsEqualTo(42L);
            result = row;
        }
        else
        {
            var holder = new ImmutableRelation<RelationCompositeChild>(key, source, parent.Model.RelationProperties[nameof(RelationCompositeParent.Children)]);
            var row = (await holder.GetValuesAsyncCore()).Single();
            await Assert.That(row.Tenant).IsEqualTo(7L);
            await Assert.That(row.ParentId).IsEqualTo(42L);
            result = row;
        }
        await Assert.That(factory.Inputs[0].ToSql().Parameters.Select(parameter => parameter.Value)
            .SequenceEqual(new object?[] { 7L, 42L })).IsTrue();
        await Assert.That(cache.TryGetMaterializedRow(reference ? key : DataLinqKey.FromValue(10), source, out var cached)).IsTrue();
        await Assert.That(cached).IsSameReferenceAs(result);
        await Assert.That(cache.RowCount).IsEqualTo(transactional ? 0 : 1);
        if (transactional) await Assert.That(cache.IndicesCount.Sum(index => index.count)).IsEqualTo(0);
    }

    private sealed class RelationWriterProvider(ScriptedMutationScenario scenario, DatabaseType databaseType)
        : CapturedReadProvider<RelationKeyDb>(scenario, databaseType)
    {
        private readonly IDataLinqDataWriter writer = databaseType == DatabaseType.SQLite
            ? new global::DataLinq.SQLite.SQLiteDataLinqDataWriter(new())
            : new global::DataLinq.MySql.SqlDataLinqDataWriter(global::DataLinq.MySql.SqlFromMetadataFactory.GetFactoryFromDatabaseType(databaseType));
        public override IDataLinqDataWriter GetWriter() => writer;
    }
}

[Database("relation_keys"), UseCache, IndexCache(IndexCacheType.All)]
public sealed partial class RelationKeyDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<RelationKeyParent> Parents { get; } = new(source);
    public DbRead<RelationKeyChild> Children { get; } = new(source);
    public DbRead<RelationBinaryParent> BinaryParents { get; } = new(source);
    public DbRead<RelationBinaryChild> BinaryChildren { get; } = new(source);
    public DbRead<RelationGuidParent> GuidParents { get; } = new(source);
    public DbRead<RelationGuidChild> GuidChildren { get; } = new(source);
    public DbRead<RelationCompositeParent> CompositeParents { get; } = new(source);
    public DbRead<RelationCompositeChild> CompositeChildren { get; } = new(source);
}

[Table("relation_key_parents")]
public abstract partial class RelationKeyParent(IRowData row, IDataSourceAccess source)
    : Immutable<RelationKeyParent, RelationKeyDb>(row, source), ITableModel<RelationKeyDb>
{
    [PrimaryKey, Column("id"), ScalarConverter(typeof(TransactionMutationGuardReferenceIdConverter))]
    public abstract TransactionMutationGuardReferenceId Id { get; }
    [Relation("relation_key_children", "parent_id", "FK_relation_key")] public abstract IImmutableRelation<RelationKeyChild> Children { get; }
}

[Table("relation_key_children")]
public abstract partial class RelationKeyChild : Immutable<RelationKeyChild, RelationKeyDb>, ITableModel<RelationKeyDb>
{
    protected RelationKeyChild(IRowData row, IDataSourceAccess source) : base(row, source) => Creating.Value?.Invoke();
    internal static AsyncLocal<Action?> Creating { get; } = new();
    [PrimaryKey, Column("id"), ScalarConverter(typeof(TransactionMutationGuardReferenceIdConverter))]
    public abstract TransactionMutationGuardReferenceId Id { get; }
    [ForeignKey("relation_key_parents", "id", "FK_relation_key"), Column("parent_id"), ScalarConverter(typeof(TransactionMutationGuardReferenceIdConverter))]
    public abstract TransactionMutationGuardReferenceId ParentId { get; }
    [Relation("relation_key_parents", "id", "FK_relation_key")] public abstract RelationKeyParent Parent { get; }
}

[Table("relation_binary_parents")]
public abstract partial class RelationBinaryParent(IRowData row, IDataSourceAccess source)
    : Immutable<RelationBinaryParent, RelationKeyDb>(row, source), ITableModel<RelationKeyDb>
{
    [PrimaryKey, Column("id")] public abstract byte[] Id { get; }
    [Relation("relation_binary_children", "parent_id", "FK_relation_binary")] public abstract IImmutableRelation<RelationBinaryChild> Children { get; }
}

[Table("relation_binary_children")]
public abstract partial class RelationBinaryChild(IRowData row, IDataSourceAccess source)
    : Immutable<RelationBinaryChild, RelationKeyDb>(row, source), ITableModel<RelationKeyDb>
{
    [PrimaryKey, Column("id")] public abstract byte[] Id { get; }
    [ForeignKey("relation_binary_parents", "id", "FK_relation_binary"), Column("parent_id")] public abstract byte[] ParentId { get; }
    [Relation("relation_binary_parents", "id", "FK_relation_binary")] public abstract RelationBinaryParent Parent { get; }
}

[Table("relation_guid_parents")]
public abstract partial class RelationGuidParent(IRowData row, IDataSourceAccess source)
    : Immutable<RelationGuidParent, RelationKeyDb>(row, source), ITableModel<RelationKeyDb>
{
    [PrimaryKey, Column("id"), GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text32),
     GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122), GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]
    public abstract Guid Id { get; }
    [Relation("relation_guid_children", "parent_id", "FK_relation_guid")] public abstract IImmutableRelation<RelationGuidChild> Children { get; }
}

[Table("relation_guid_children")]
public abstract partial class RelationGuidChild(IRowData row, IDataSourceAccess source)
    : Immutable<RelationGuidChild, RelationKeyDb>(row, source), ITableModel<RelationKeyDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [ForeignKey("relation_guid_parents", "id", "FK_relation_guid"), Column("parent_id"),
     GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text32), GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122),
     GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)] public abstract Guid ParentId { get; }
    [Relation("relation_guid_parents", "id", "FK_relation_guid")] public abstract RelationGuidParent Parent { get; }
}

[Table("relation_composite_parents")]
public abstract partial class RelationCompositeParent(IRowData row, IDataSourceAccess source)
    : Immutable<RelationCompositeParent, RelationKeyDb>(row, source), ITableModel<RelationKeyDb>
{
    [PrimaryKey, Column("tenant")] public abstract long Tenant { get; }
    [PrimaryKey, Column("id")] public abstract long Id { get; }
    [Relation("relation_composite_children", new[] { "tenant", "parent_id" }, "FK_relation_composite")]
    public abstract IImmutableRelation<RelationCompositeChild> Children { get; }
}

[Table("relation_composite_children")]
public abstract partial class RelationCompositeChild(IRowData row, IDataSourceAccess source)
    : Immutable<RelationCompositeChild, RelationKeyDb>(row, source), ITableModel<RelationKeyDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [ForeignKey("relation_composite_parents", "tenant", "FK_relation_composite"), Column("tenant")] public abstract long Tenant { get; }
    [ForeignKey("relation_composite_parents", "id", "FK_relation_composite"), Column("parent_id")] public abstract long ParentId { get; }
    [Relation("relation_composite_parents", new[] { "tenant", "id" }, "FK_relation_composite")] public abstract RelationCompositeParent Parent { get; }
}
