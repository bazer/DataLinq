using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class RequiredReferenceTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarReferences_NoKeyAndMissingTarget_EnforceNavigationOnly(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<RequiredReferenceDb>.Create(provider, "required_reference_missing");
        var database = scope.Database;
        // Warm-load counts require a stable cache; cleanup overlap is tested separately.
        database.Provider.State.Cache.CleanupScheduler?.Stop();
        foreach (var key in new int?[] { null, 71001 })
        {
            var child = ScalarChild(database, key);
            var loads = ReferenceLoads(database);
            for (var pass = 0; pass < 2; pass++)
            {
                var failure = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
                await Assert.That(failure.Message).Contains("RequiredReferenceChild.RequiredParent");
                await Assert.That(failure.Message).DoesNotContain("71001");
                await Assert.That(child.OptionalParent).IsNull();
            }

            // Each navigation resolves once; the warm absence is not successful required navigation.
            await Assert.That(ReferenceLoads(database) - loads).IsEqualTo(key.HasValue ? 2L : 0L);
            var relation = database.Provider.Metadata.GetTableModel(typeof(RequiredReferenceChild)).Model
                .RelationProperties[nameof(RequiredReferenceChild.RequiredParent)];
            var holder = new ImmutableForeignKey<RequiredReferenceParent>(
                key.HasValue ? DataLinqKey.FromValue(key.Value) : DataLinqKey.Null,
                database.Provider.ReadOnlyAccess, relation);
            await Assert.That(holder.Value).IsNull();
            await Assert.That(RequiredReferenceParent.Get(key ?? 71001, database)).IsNull();
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarReferences_InvalidationRechecksMissingAndPreviouslyResolvedTargets(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<RequiredReferenceDb>.Create(provider, "required_reference_invalidation");
        var database = scope.Database;
        // Only the explicit invalidations below should interrupt these identity assertions.
        database.Provider.State.Cache.CleanupScheduler?.Stop();
        var child = ScalarChild(database, 1);
        _ = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
        await Assert.That(child.OptionalParent).IsNull();
        database.Insert(new MutableRequiredReferenceParent { Id = 1 });
        var required = child.RequiredParent;
        await Assert.That(required.Id).IsEqualTo(1);
        await Assert.That(child.OptionalParent).IsSameReferenceAs(required);
        var loads = ReferenceLoads(database);
        await Assert.That(child.RequiredParent).IsSameReferenceAs(required);
        await Assert.That(child.OptionalParent).IsSameReferenceAs(required);
        await Assert.That(ReferenceLoads(database)).IsEqualTo(loads);

        database.Provider.DatabaseAccess.ExecuteNonQuery("DELETE FROM required_reference_parents WHERE id = 1");
        database.Provider.GetTableCache(database.Provider.Metadata.GetTableModel(typeof(RequiredReferenceParent)).Table).ClearCache();
        _ = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
        await Assert.That(child.OptionalParent).IsNull();
        await Assert.That(ReferenceLoads(database) - loads).IsEqualTo(2L);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CustomReferenceHolders_PreserveOptionalRequiredCardinalityAndOriginalFailures(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<RequiredReferenceDb>.Create(provider, "required_reference_custom");
        var database = scope.Database;
        var child = ScalarChild(database, 1);
        var parent = new ImmutableRequiredReferenceParent(new MutableRequiredReferenceParent { Id = 1 }.GetRowData(), database.Provider.ReadOnlyAccess);
        var holder = new CustomReference();
        typeof(ImmutableRequiredReferenceChild).GetField("_RequiredParent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(child, holder);
        typeof(ImmutableRequiredReferenceChild).GetField("_OptionalParent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(child, holder);
        _ = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
        await Assert.That(child.OptionalParent).IsNull();
        holder.Rows.Add(parent);
        await Assert.That(child.RequiredParent).IsSameReferenceAs(parent);
        await Assert.That(child.OptionalParent).IsSameReferenceAs(parent);
        await Assert.That(holder.Reads).IsEqualTo(4);
        holder.Rows.Add(parent);
        var duplicate = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
        await Assert.That(duplicate.Message).DoesNotContain("did not resolve");
        _ = Capture<InvalidOperationException>(() => _ = child.OptionalParent);

        foreach (var failure in new Exception[] { new InvalidOperationException("provider failure"), new OperationCanceledException(), new NotSupportedException() })
        {
            holder.Failure = failure;
            await Assert.That(Capture<Exception>(() => _ = child.RequiredParent)).IsSameReferenceAs(failure);
            await Assert.That(Capture<Exception>(() => _ = child.OptionalParent)).IsSameReferenceAs(failure);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ConvertedRequiredReference_UsesCanonicalKeyAndRevalidatesAfterInvalidation(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<TypedIdRelationKeyDb>.Create(provider, "required_reference_converted");
        var database = scope.Database;
        database.Provider.State.Cache.CleanupScheduler?.Stop();
        var child = new ImmutableTypedIdRelationKeyChild(new MutableTypedIdRelationKeyChild
        {
            Id = new QueryTypedId(201), ParentId = new QueryTypedId(101), Name = "child"
        }.GetRowData(), database.Provider.ReadOnlyAccess);
        var missing = Capture<InvalidOperationException>(() => _ = child.Parent);
        await Assert.That(missing.Message).Contains("TypedIdRelationKeyChild.Parent");
        database.Insert(new MutableTypedIdRelationKeyParent { Id = new QueryTypedId(101), Name = "parent" });
        var parent = child.Parent;
        await Assert.That(parent.Id).IsEqualTo(new QueryTypedId(101));
        await Assert.That(child.Parent).IsSameReferenceAs(parent);
        database.Provider.DatabaseAccess.ExecuteNonQuery("DELETE FROM typedidrelationkeyparents WHERE id = 101");
        database.Provider.GetTableCache(database.Provider.Metadata.GetTableModel(typeof(TypedIdRelationKeyParent)).Table).ClearCache();
        _ = Capture<InvalidOperationException>(() => _ = child.Parent);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RequiredFailure_ReleasesOwnershipAndDoesNotPoisonTheTransaction(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<RequiredReferenceDb>.Create(provider, "required_reference_transaction");
        using var transaction = scope.Database.Transaction();
        var child = ScalarChild(scope.Database, 1, transaction);
        using (transaction.ExecutionGate.Enter("active operation"))
        {
            var busy = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
            await Assert.That(busy.Message).DoesNotContain("did not resolve to a target row");
        }
        _ = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
        await Assert.That(transaction.Get<RequiredReferenceParent>(DataLinqKey.FromValue(1))).IsNull();
        var parent = transaction.Insert(new MutableRequiredReferenceParent { Id = 1 });
        await Assert.That(child.RequiredParent).IsSameReferenceAs(parent);
        await Assert.That(child.OptionalParent).IsSameReferenceAs(parent);
        transaction.Commit();
        await Assert.That(child.RequiredParent.Id).IsEqualTo(1);
    }

    private static ImmutableRequiredReferenceChild ScalarChild(Database<RequiredReferenceDb> database, int? key, IDataSourceAccess? source = null)
        => new(new MutableRequiredReferenceChild { Id = 1, RequiredParentId = key, OptionalParentId = key }.GetRowData(), source ?? database.Provider.ReadOnlyAccess);

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CompositeReferences_PartialKeysMissingTargetsAndInvalidation(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<RequiredReferenceDb>.Create(provider, "required_reference_composite");
        var database = scope.Database;
        database.Provider.State.Cache.CleanupScheduler?.Stop();
        foreach (var (tenant, id) in new (int?, int?)[] { (null, null), (1, null), (null, 2), (1, 2) })
        {
            var child = new ImmutableCompositeReferenceChild(new MutableCompositeReferenceChild
            {
                Id = 1, RequiredTenant = tenant, RequiredId = id, OptionalTenant = tenant, OptionalId = id
            }.GetRowData(), database.Provider.ReadOnlyAccess);
            var initialLoads = ReferenceLoads(database);
            var missing = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
            await Assert.That(missing.Message).Contains("CompositeReferenceChild.RequiredParent");
            await Assert.That(child.OptionalParent).IsNull();
            _ = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
            await Assert.That(ReferenceLoads(database) - initialLoads).IsEqualTo(tenant.HasValue && id.HasValue ? 2L : 0L);
            if (tenant.HasValue && id.HasValue)
            {
                database.Insert(new MutableCompositeReferenceParent { Tenant = tenant.Value, Id = id.Value });
                var parent = child.RequiredParent;
                await Assert.That(parent.Tenant).IsEqualTo(1);
                await Assert.That(parent.Id).IsEqualTo(2);
                await Assert.That(database.Get<CompositeReferenceParent>(parent.PrimaryKeys())).IsSameReferenceAs(parent);
                await Assert.That(child.OptionalParent).IsSameReferenceAs(parent);
                var loads = ReferenceLoads(database);
                await Assert.That(child.RequiredParent).IsSameReferenceAs(parent);
                await Assert.That(ReferenceLoads(database)).IsEqualTo(loads);
                database.Provider.DatabaseAccess.ExecuteNonQuery("DELETE FROM composite_reference_parents WHERE tenant = 1 AND id = 2");
                database.Provider.GetTableCache(database.Provider.Metadata.GetTableModel(typeof(CompositeReferenceParent)).Table).ClearCache();
                _ = Capture<InvalidOperationException>(() => _ = child.RequiredParent);
                await Assert.That(child.OptionalParent).IsNull();
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarReferences_CleanupDuringConstructionPreservesValuesWithoutStableIdentity(TestProviderDescriptor provider)
    {
        foreach (var forceCleanup in new[] { false, true })
        {
            using var scope = TemporaryModelTestDatabase<RequiredReferenceDb>.Create(provider, "required_reference_cleanup");
            var database = scope.Database;
            var scheduler = database.Provider.State.Cache.CleanupScheduler!;
            scheduler.Stop();
            database.Insert(new MutableRequiredReferenceParent { Id = 1 });
            database.Cache.Clear();
            var child = ScalarChild(database, 1);
            using var gate = CachePublicationGate.Install(database.Provider.TelemetryInstanceId);
            var read = Task.Factory.StartNew(() => child.RequiredParent,
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            try
            {
                gate.WaitUntilBlocked();
                if (forceCleanup)
                    await Assert.That(scheduler.RunScheduledCleanup().RowsRemoved).IsEqualTo(0);
                gate.Release();
                var first = await read.WaitAsync(TimeSpan.FromSeconds(20));
                var optional = child.OptionalParent!;
                await Assert.That(first.Id).IsEqualTo(1);
                await Assert.That(optional.Id).IsEqualTo(1);
                // A cleanup generation change can suppress both row and reference
                // publication even without eviction. The next load may be a new instance.
                await Assert.That(ReferenceEquals(first, optional)).IsEqualTo(!forceCleanup);
                await Assert.That(child.RequiredParent).IsSameReferenceAs(optional);
                await Assert.That(child.OptionalParent).IsSameReferenceAs(optional);
            }
            finally
            {
                gate.Release();
                await read.WaitAsync(TimeSpan.FromSeconds(20));
            }
        }
    }

    private static long ReferenceLoads(Database<RequiredReferenceDb> database)
        => DataLinqMetrics.Snapshot().Providers.Single(item => item.ProviderInstanceId == database.Provider.TelemetryInstanceId)
            .Tables.Sum(table => table.Relations.ReferenceLoads);

    private static T Capture<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T failure) { return failure; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class CustomReference : IImmutableForeignKey<RequiredReferenceParent>
    {
        internal readonly List<RequiredReferenceParent> Rows = [];
        internal Exception? Failure;
        internal int Reads;
        public RequiredReferenceParent? Value
        {
            get
            {
                Reads++;
                if (Failure is not null) throw Failure;
                return Rows.SingleOrDefault();
            }
        }
        public void Clear() => Rows.Clear();
    }
}

[Database("required_reference"), UseCache]
public sealed partial class RequiredReferenceDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<RequiredReferenceParent> Parents { get; } = new(source);
    public DbRead<RequiredReferenceChild> Children { get; } = new(source);
    public DbRead<CompositeReferenceParent> CompositeParents { get; } = new(source);
    public DbRead<CompositeReferenceChild> CompositeChildren { get; } = new(source);
}

[Table("required_reference_parents")]
public abstract partial class RequiredReferenceParent : Immutable<RequiredReferenceParent, RequiredReferenceDb>, ITableModel<RequiredReferenceDb>
{
    protected RequiredReferenceParent(IRowData row, IDataSourceAccess source) : base(row, source)
        => CachePublicationGate.OnConstruction(source);
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Relation("required_reference_children", "required_parent_id", "FK_required_reference")]
    public abstract IImmutableRelation<RequiredReferenceChild> RequiredChildren { get; }
    [Relation("required_reference_children", "optional_parent_id", "FK_optional_reference")]
    public abstract IImmutableRelation<RequiredReferenceChild> OptionalChildren { get; }
}

[Table("required_reference_children")]
public abstract partial class RequiredReferenceChild(IRowData row, IDataSourceAccess source)
    : Immutable<RequiredReferenceChild, RequiredReferenceDb>(row, source), ITableModel<RequiredReferenceDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Nullable, ForeignKey("required_reference_parents", "id", "FK_required_reference"), Column("required_parent_id")]
    public abstract int? RequiredParentId { get; }
    [Nullable, ForeignKey("required_reference_parents", "id", "FK_optional_reference"), Column("optional_parent_id")]
    public abstract int? OptionalParentId { get; }
    [Relation("required_reference_parents", "id", "FK_required_reference")]
    public abstract RequiredReferenceParent RequiredParent { get; }
    [Relation("required_reference_parents", "id", "FK_optional_reference")]
    public abstract RequiredReferenceParent? OptionalParent { get; }
}

[Table("composite_reference_parents")]
public abstract partial class CompositeReferenceParent(IRowData row, IDataSourceAccess source)
    : Immutable<CompositeReferenceParent, RequiredReferenceDb>(row, source), ITableModel<RequiredReferenceDb>
{
    [PrimaryKey, Column("tenant")]
    public abstract int Tenant { get; }
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Relation("composite_reference_children", new[] { "required_tenant", "required_id" }, "FK_composite_required")]
    public abstract IImmutableRelation<CompositeReferenceChild> RequiredChildren { get; }
    [Relation("composite_reference_children", new[] { "optional_tenant", "optional_id" }, "FK_composite_optional")]
    public abstract IImmutableRelation<CompositeReferenceChild> OptionalChildren { get; }
}

[Table("composite_reference_children")]
public abstract partial class CompositeReferenceChild(IRowData row, IDataSourceAccess source)
    : Immutable<CompositeReferenceChild, RequiredReferenceDb>(row, source), ITableModel<RequiredReferenceDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Nullable, ForeignKey("composite_reference_parents", "tenant", "FK_composite_required", 0), Column("required_tenant")]
    public abstract int? RequiredTenant { get; }
    [Nullable, ForeignKey("composite_reference_parents", "id", "FK_composite_required", 1), Column("required_id")]
    public abstract int? RequiredId { get; }
    [Nullable, ForeignKey("composite_reference_parents", "tenant", "FK_composite_optional", 0), Column("optional_tenant")]
    public abstract int? OptionalTenant { get; }
    [Nullable, ForeignKey("composite_reference_parents", "id", "FK_composite_optional", 1), Column("optional_id")]
    public abstract int? OptionalId { get; }
    [Relation("composite_reference_parents", new[] { "tenant", "id" }, "FK_composite_required")]
    public abstract CompositeReferenceParent RequiredParent { get; }
    [Relation("composite_reference_parents", new[] { "tenant", "id" }, "FK_composite_optional")]
    public abstract CompositeReferenceParent? OptionalParent { get; }
}
