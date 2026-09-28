using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Unit.Core;

public sealed class PublicAsyncRelationTests
{
    static PublicAsyncRelationTests() => EmployeesGeneratedMetadataFixture.EnsureInitialized();

    private static MutableDepartment[] Rows() =>
        [new() { DeptNo = "d001", Name = "A" }, new() { DeptNo = "d002", Name = "BBBB" }];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OneAsyncPrimitiveSuppliesEveryAccessorWithoutSynchronousReads(bool mock)
    {
        var rows = Rows();
        IImmutableRelation<MutableDepartment> relation = mock
            ? new ImmutableRelationMock<MutableDepartment>(rows) : new PrimitiveRelation(rows);
        await Assert.That(await relation.ValuesAsync()).IsEquivalentTo(rows);
        await Assert.That(await relation.ToListAsync()).IsEquivalentTo(rows);
        await Assert.That(await relation.ToArrayAsync()).IsEquivalentTo(rows);
        await Assert.That(await relation.KeysAsync()).IsEquivalentTo(rows.Select(row => row.PrimaryKeys()).ToArray());
        await Assert.That(await relation.GetAsync(rows[1].PrimaryKeys())).IsSameReferenceAs(rows[1]);
        await Assert.That(await relation.ContainsKeyAsync(rows[0].PrimaryKeys())).IsTrue();
        await Assert.That(await relation.GetAsync(DataLinqKey.FromValue("other"))).IsNull();
        await Assert.That(await relation.ContainsKeyAsync(DataLinqKey.FromValue("other"))).IsFalse();
        await Assert.That((await relation.ToFrozenDictionaryAsync())[rows[0].PrimaryKeys()]).IsSameReferenceAs(rows[0]);
        await Assert.That(await relation.FirstAsync()).IsSameReferenceAs(rows[0]);
        await Assert.That(await relation.LastAsync()).IsSameReferenceAs(rows[1]);
        await Assert.That(await relation.FirstOrDefaultAsync()).IsSameReferenceAs(rows[0]);
        await Assert.That(await relation.LastOrDefaultAsync()).IsSameReferenceAs(rows[1]);
        await Assert.That(await relation.SingleAsync(row => row.Name.Length == 1)).IsSameReferenceAs(rows[0]);
        await Assert.That(await relation.SingleOrDefaultAsync(row => row.Name.Length == 1)).IsSameReferenceAs(rows[0]);
        await Assert.That(await relation.FirstAsync(row => row.Name.Length > 1)).IsSameReferenceAs(rows[1]);
        await Assert.That(await relation.LastAsync(row => row.Name.Length == 1)).IsSameReferenceAs(rows[0]);
        await Assert.That(await relation.FirstOrDefaultAsync(row => row.Name == "absent")).IsNull();
        await Assert.That(await relation.LastOrDefaultAsync(row => row.Name == "absent")).IsNull();
        await Assert.That(await relation.AnyAsync()).IsTrue();
        await Assert.That(await relation.AnyAsync(row => row.Name == "absent")).IsFalse();
        await Assert.That(await relation.CountAsync()).IsEqualTo(2);
        await Assert.That(await relation.CountAsync(row => row.Name.Length == 1)).IsEqualTo(1);
        await Assert.That(async () => { await relation.SingleAsync(); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await relation.SingleOrDefaultAsync(); }).Throws<InvalidOperationException>();
        if (relation is PrimitiveRelation primitive)
            await Assert.That(primitive.Starts).IsEqualTo(primitive.Disposals);
    }

    [Test]
    public async Task NumericAndNullableDefaultsUseLocalModelValuesAndStandardResultTypes()
    {
        IImmutableRelation<MutableDepartment> relation = new PrimitiveRelation(Rows());
        int sumint = await relation.SumAsync(row => (int)row.Name.Length);
        await Assert.That(sumint).IsEqualTo((int)5);
        int? sumintNullable = await relation.SumAsync(row => (int?)row.Name.Length);
        await Assert.That(sumintNullable).IsEqualTo((int?)5);
        long sumlong = await relation.SumAsync(row => (long)row.Name.Length);
        await Assert.That(sumlong).IsEqualTo((long)5);
        long? sumlongNullable = await relation.SumAsync(row => (long?)row.Name.Length);
        await Assert.That(sumlongNullable).IsEqualTo((long?)5);
        float sumfloat = await relation.SumAsync(row => (float)row.Name.Length);
        await Assert.That(sumfloat).IsEqualTo((float)5);
        float? sumfloatNullable = await relation.SumAsync(row => (float?)row.Name.Length);
        await Assert.That(sumfloatNullable).IsEqualTo((float?)5);
        double sumdouble = await relation.SumAsync(row => (double)row.Name.Length);
        await Assert.That(sumdouble).IsEqualTo((double)5);
        double? sumdoubleNullable = await relation.SumAsync(row => (double?)row.Name.Length);
        await Assert.That(sumdoubleNullable).IsEqualTo((double?)5);
        decimal sumdecimal = await relation.SumAsync(row => (decimal)row.Name.Length);
        await Assert.That(sumdecimal).IsEqualTo((decimal)5);
        decimal? sumdecimalNullable = await relation.SumAsync(row => (decimal?)row.Name.Length);
        await Assert.That(sumdecimalNullable).IsEqualTo((decimal?)5);
        double averageint = await relation.AverageAsync(row => (int)row.Name.Length);
        await Assert.That(averageint).IsEqualTo((double)2.5);
        double? averageintNullable = await relation.AverageAsync(row => (int?)row.Name.Length);
        await Assert.That(averageintNullable).IsEqualTo((double?)2.5);
        double averagelong = await relation.AverageAsync(row => (long)row.Name.Length);
        await Assert.That(averagelong).IsEqualTo((double)2.5);
        double? averagelongNullable = await relation.AverageAsync(row => (long?)row.Name.Length);
        await Assert.That(averagelongNullable).IsEqualTo((double?)2.5);
        float averagefloat = await relation.AverageAsync(row => (float)row.Name.Length);
        await Assert.That(averagefloat).IsEqualTo((float)2.5);
        float? averagefloatNullable = await relation.AverageAsync(row => (float?)row.Name.Length);
        await Assert.That(averagefloatNullable).IsEqualTo((float?)2.5);
        double averagedouble = await relation.AverageAsync(row => (double)row.Name.Length);
        await Assert.That(averagedouble).IsEqualTo((double)2.5);
        double? averagedoubleNullable = await relation.AverageAsync(row => (double?)row.Name.Length);
        await Assert.That(averagedoubleNullable).IsEqualTo((double?)2.5);
        decimal averagedecimal = await relation.AverageAsync(row => (decimal)row.Name.Length);
        await Assert.That(averagedecimal).IsEqualTo((decimal)2.5);
        decimal? averagedecimalNullable = await relation.AverageAsync(row => (decimal?)row.Name.Length);
        await Assert.That(averagedecimalNullable).IsEqualTo((decimal?)2.5);
        int min = await relation.MinAsync(row => row.Name.Length);
        int max = await relation.MaxAsync(row => row.Name.Length);
        string? text = await relation.MinAsync(row => row.Name);
        await Assert.That(min).IsEqualTo(1);
        await Assert.That(max).IsEqualTo(4);
        await Assert.That(text).IsEqualTo("A");
        await Assert.That(await relation.SumAsync(row => (int?)null)).IsEqualTo(0);
        await Assert.That(await relation.AverageAsync(row => (int?)null)).IsNull();
        await Assert.That(await relation.MinAsync(row => (int?)null)).IsNull();
        await Assert.That(await relation.MaxAsync(row => (int?)null)).IsNull();
        await Assert.That(async () => { await relation.SumAsync(row => int.MaxValue); }).Throws<OverflowException>();
        var selectorError = new ApplicationException("selector");
        var observed = await Assert.That(async () => { await relation.SumAsync((Func<MutableDepartment, int>)(_ => throw selectorError)); }).Throws<ApplicationException>();
        await Assert.That(observed).IsSameReferenceAs(selectorError);
    }

    [Test]
    public async Task EmptyDefaultsNullPredicatesAndDuplicateKeysFailWithoutSyncFallback()
    {
        IImmutableRelation<MutableDepartment> empty = new PrimitiveRelation([]);
        await Assert.That(await empty.FirstOrDefaultAsync()).IsNull();
        await Assert.That(await empty.LastOrDefaultAsync()).IsNull();
        await Assert.That(await empty.SingleOrDefaultAsync()).IsNull();
        await Assert.That(await empty.CountAsync()).IsEqualTo(0);
        await Assert.That(await empty.SumAsync(row => row.Name.Length)).IsEqualTo(0);
        await Assert.That(async () => { await empty.FirstAsync(); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await empty.LastAsync(); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await empty.SingleAsync(); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await empty.AverageAsync(row => row.Name.Length); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await empty.MinAsync(row => row.Name.Length); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await empty.MaxAsync(row => row.Name.Length); }).Throws<InvalidOperationException>();
        await Assert.That(async () => { await empty.FirstAsync(null!); }).Throws<ArgumentNullException>();
        var row = Rows()[0];
        IImmutableRelation<MutableDepartment> duplicates = new PrimitiveRelation([row, row]);
        await Assert.That(await duplicates.CountAsync()).IsEqualTo(2);
        await Assert.That(async () => { await duplicates.ToFrozenDictionaryAsync(); }).Throws<ArgumentException>();
        var concrete = new ImmutableRelationMock<MutableDepartment>([row, row]);
        await Assert.That(async () => { await concrete.GetAsync(row.PrimaryKeys()); }).Throws<ArgumentException>();
        IImmutableRelation<MutableDepartment> syncOnly = new SynchronousOnlyRelation();
        await Assert.That(async () => { await syncOnly.CountAsync(); }).Throws<NotSupportedException>();
        await Assert.That(async () => { await syncOnly.GetAsync(row.PrimaryKeys()); }).Throws<NotSupportedException>();
    }

    [Test]
    public async Task OverridesDispatchThroughDefaultsAndConcreteMethodsWithoutRecursion()
    {
        var row = Rows()[0];
        IImmutableRelation<MutableDepartment> overridden = new ValuesOverrideRelation(row);
        await Assert.That(await overridden.GetAsync(row.PrimaryKeys())).IsSameReferenceAs(row);
        await Assert.That(await overridden.ContainsKeyAsync(row.PrimaryKeys())).IsTrue();
        await Assert.That((await overridden.KeysAsync()).Length).IsEqualTo(1);
        var concrete = new CountOverrideMock();
        await Assert.That(await concrete.CountAsync()).IsEqualTo(42);
        await Assert.That(await ((IImmutableRelation<MutableDepartment>)concrete).CountAsync()).IsEqualTo(42);
        var ordinary = new ImmutableRelationMock<MutableDepartment>([row]);
        await Assert.That(await ordinary.SingleAsync()).IsSameReferenceAs(row);
        await Assert.That(await ordinary.SumAsync(value => value.Name.Length)).IsEqualTo(1);
        await Assert.That(await ordinary.MinAsync(value => value.Name)).IsEqualTo("A");
    }

    [Test]
    public async Task BufferedMockHonorsMethodAndEnumeratorTokensAndDefersLoading()
    {
        var loads = 0;
        IEnumerable<MutableDepartment> Load()
        {
            loads++;
            foreach (var row in Rows()) yield return row;
        }
        var relation = new ImmutableRelationMock<MutableDepartment>(Load());
        using var method = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var view = relation.AsAsyncEnumerable(method.Token);
        await using (var iterator = view.GetAsyncEnumerator(enumeration.Token))
        {
            await Assert.That(loads).IsEqualTo(0);
            await Assert.That(await iterator.MoveNextAsync()).IsTrue();
            enumeration.Cancel();
            await Assert.That(async () => { await iterator.MoveNextAsync(); }).Throws<OperationCanceledException>();
        }
        await using (var iterator = view.GetAsyncEnumerator())
        {
            await Assert.That(await iterator.MoveNextAsync()).IsTrue();
            method.Cancel();
            await Assert.That(async () => { await iterator.MoveNextAsync(); }).Throws<OperationCanceledException>();
        }
        await Assert.That(loads).IsEqualTo(1);
        relation.Clear();
        await Assert.That((await relation.ValuesAsync()).Length).IsEqualTo(2);
        await Assert.That(loads).IsEqualTo(2);
        await Assert.That(async () => { await relation.ValuesAsync(new(true)); }).Throws<OperationCanceledException>();
    }

    private class SynchronousOnlyRelation : IImmutableRelation<MutableDepartment>
    {
        public MutableDepartment? this[DataLinqKey key] => throw new Exception("sync");
        public int Count => throw new Exception("sync");
        public ImmutableArray<DataLinqKey> Keys => throw new Exception("sync");
        public ImmutableArray<MutableDepartment> Values => throw new Exception("sync");
        public void Clear() { }
        public bool ContainsKey(DataLinqKey key) => throw new Exception("sync");
        public MutableDepartment? Get(DataLinqKey key) => throw new Exception("sync");
        public FrozenDictionary<DataLinqKey, MutableDepartment> ToFrozenDictionary() => throw new Exception("sync");
        public IEnumerable<KeyValuePair<DataLinqKey, MutableDepartment>> AsKeyValuePairs() => throw new Exception("sync");
        public IEnumerator<MutableDepartment> GetEnumerator() => throw new Exception("sync");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private class PrimitiveRelation(MutableDepartment[] rows) : SynchronousOnlyRelation, IImmutableRelation<MutableDepartment>
    {
        internal int Starts;
        internal int Disposals;
        public async IAsyncEnumerable<MutableDepartment> AsAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Starts++;
            try
            {
                await Task.CompletedTask;
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return row;
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally { Disposals++; }
        }
    }

    private sealed class ValuesOverrideRelation(MutableDepartment row) : SynchronousOnlyRelation, IImmutableRelation<MutableDepartment>
    {
        public ValueTask<ImmutableArray<MutableDepartment>> ValuesAsync(CancellationToken cancellationToken = default) =>
            new(ImmutableArray.Create(row));
    }

    private sealed class CountOverrideMock() : ImmutableRelationMock<MutableDepartment>([])
    {
        public override ValueTask<int> CountAsync(CancellationToken cancellationToken = default) => new(42);
    }
}
