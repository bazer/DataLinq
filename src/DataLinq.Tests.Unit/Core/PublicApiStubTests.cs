using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Query;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Unit.Core;

public sealed class PublicApiStubTests
{
    static PublicApiStubTests() => EmployeesGeneratedMetadataFixture.EnsureInitialized();

    [Test]
    public async Task RelationMockSupportsTheFullCollectionContractAndReloadsAfterClear()
    {
        var first = new MutableDepartment { DeptNo = "d001", Name = "First" };
        var second = new MutableDepartment { DeptNo = "d002", Name = "Second" };
        var list = new List<MutableDepartment> { second, first };
        IImmutableRelation<MutableDepartment> relation = new ImmutableRelationMock<MutableDepartment>(list);
        await Assert.That(relation.Values).IsEquivalentTo(new[] { second, first });
        await Assert.That(relation.Count).IsEqualTo(2);
        await Assert.That(relation.Any()).IsTrue();
        await Assert.That(relation.First()).IsSameReferenceAs(second);
        await Assert.That(relation.Last()).IsSameReferenceAs(first);
        await Assert.That(relation[first.PrimaryKeys()]).IsSameReferenceAs(first);
        await Assert.That(relation.Get(second.PrimaryKeys())).IsSameReferenceAs(second);
        await Assert.That(relation.ContainsKey(first.PrimaryKeys())).IsTrue();
        await Assert.That(relation.Keys.OrderBy(key => key.ToString()).ToArray()).IsEquivalentTo(
            new[] { first.PrimaryKeys(), second.PrimaryKeys() }.OrderBy(key => key.ToString()).ToArray());
        await Assert.That(relation.AsEnumerable().Count()).IsEqualTo(2);
        await Assert.That(relation.ToFrozenDictionary()[first.PrimaryKeys()]).IsSameReferenceAs(first);
        await Assert.That(((IEnumerable)relation).Cast<MutableDepartment>().ToArray()).IsEquivalentTo(new[] { second, first });
        var missing = new MutableDepartment { DeptNo = "d999", Name = "Missing" }.PrimaryKeys();
        await Assert.That(relation.Get(missing)).IsNull();
        await Assert.That(relation.ContainsKey(missing)).IsFalse();

        list.Clear();
        await Assert.That(relation.Count).IsEqualTo(2);
        relation.Clear();
        await Assert.That(relation.Values.IsDefault).IsFalse();
        await Assert.That(relation.Count).IsEqualTo(0);
        await Assert.That(relation.Any()).IsFalse();
        await Assert.That(relation.FirstOrDefault()).IsNull();
        await Assert.That(relation.LastOrDefault()).IsNull();
        await Assert.That(relation.SingleOrDefault()).IsNull();
        await Assert.That(() => relation.First()).Throws<InvalidOperationException>();
        list.Add(first);
        relation.Clear();
        await Assert.That(relation.Single()).IsSameReferenceAs(first);
    }

    [Test]
    public async Task RelationMockRejectsNullSourcesAndDuplicateKeysAtKeyedLookup()
    {
        await Assert.That(() => new ImmutableRelationMock<MutableDepartment>(null!)).Throws<ArgumentNullException>();
        var first = new MutableDepartment { DeptNo = "d001", Name = "First" };
        var second = new MutableDepartment { DeptNo = "d001", Name = "Duplicate" };
        var relation = new ImmutableRelationMock<MutableDepartment>([first, second]);
        await Assert.That(relation.Count).IsEqualTo(2);
        await Assert.That(() => relation.ToFrozenDictionary()).Throws<ArgumentException>();
    }

    [Test]
    public async Task ClearDuringInitialMockLoadCannotPublishTheOldSequenceForLaterReads()
    {
        var first = new MutableDepartment { DeptNo = "d001", Name = "First" };
        var second = new MutableDepartment { DeptNo = "d002", Name = "Second" };
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var loads = 0;
        IEnumerable<MutableDepartment> Source()
        {
            if (Interlocked.Increment(ref loads) == 1)
            {
                entered.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                yield return first;
            }
            else yield return second;
        }
        var relation = new ImmutableRelationMock<MutableDepartment>(Source());
        var oldRead = Task.Run(() => relation.Values);
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            relation.Clear();
            await Assert.That(relation.Values[0]).IsSameReferenceAs(second);
        }
        finally { resume.Set(); }
        await Assert.That((await oldRead)[0]).IsSameReferenceAs(first);
        await Assert.That(relation.Values[0]).IsSameReferenceAs(second);
        await Assert.That(loads).IsEqualTo(2);
    }

    [Test]
    public async Task NeverImplementedMutationEntryPointsFailAtCompileTimeAndExplainTheAlternativeToOldBinaries()
    {
        var methods = new[]
        {
            (typeof(SqlQuery<object>), "Insert"), (typeof(SqlQuery<object>), "Update"), (typeof(SqlQuery<object>), "Delete"),
            (typeof(WhereGroup<object>), "Insert"), (typeof(WhereGroup<object>), "Update"), (typeof(WhereGroup<object>), "Delete"),
            (typeof(Insert<object>), "Execute"), (typeof(Update<object>), "Execute"), (typeof(Delete<object>), "Execute")
        };
        foreach (var (type, name) in methods)
        {
            var method = type.GetMethod(name, Type.EmptyTypes)!;
            var obsolete = method.GetCustomAttribute<ObsoleteAttribute>()!;
            await Assert.That(obsolete.IsError).IsTrue();
            await Assert.That(obsolete.Message!).Contains("Transaction.");
            await Assert.That(method.GetCustomAttribute<EditorBrowsableAttribute>()!.State).IsEqualTo(EditorBrowsableState.Never);
            Exception? failure = null;
            try { method.Invoke(RuntimeHelpers.GetUninitializedObject(type), null); }
            catch (TargetInvocationException exception) { failure = exception.InnerException; }
            await Assert.That(failure).IsTypeOf<NotSupportedException>();
            await Assert.That(failure!.Message).Contains("Transaction.");
        }
    }
}
