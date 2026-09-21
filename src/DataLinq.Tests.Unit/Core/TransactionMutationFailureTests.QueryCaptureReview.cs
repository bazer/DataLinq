using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("scalar", false)]
    [Arguments("scalar", true)]
    [Arguments("contains", false)]
    [Arguments("contains", true)]
    [Arguments("any", false)]
    [Arguments("any", true)]
    public async Task QueryCaptureReview_SelectedBinaryArgumentsAreOwnedAtTheAcceptedCaptureBoundary(string selection, bool prepared)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var arguments = new[] { new QueryCaptureBinaryArgument([1]) };
        var root = fixture.Database.Query().BinaryRows;
        var query = selection switch
        {
            "contains" => root.Where(row => arguments.Select(value => value.Bytes).Contains(row.Id)),
            "any" => root.Where(row => arguments.Any(value => value.Bytes == row.Id)),
            _ => root.Where(row => arguments[0].Bytes == row.Id)
        };
        var plan = selection switch
        {
            "contains" => fixture.Database.PrepareSequenceQuery(arguments, values => fixture.Database.Query().BinaryRows
                .Where(row => values.Select(value => value.Bytes).Contains(row.Id)).Select(row => row.Id)),
            "any" => fixture.Database.PrepareSequenceQuery(arguments, values => fixture.Database.Query().BinaryRows
                .Where(row => values.Any(value => value.Bytes == row.Id)).Select(row => row.Id)),
            _ => fixture.Database.PrepareSequenceQuery(arguments, values => fixture.Database.Query().BinaryRows
                .Where(row => values[0].Bytes == row.Id).Select(row => row.Id))
        };
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([new byte[] { 7 }]) { ColumnNames = ["value"] } } };
        fixture.Scenario.AsyncSqlReaders = factory;
        arguments[0].Bytes[0] = 2; // Preparing a template must not capture this invocation.
        var sequence = prepared ? plan.ExecuteAsyncCore(fixture.Database, arguments) : AsyncPlan(query.Select(row => row.Id));
        arguments[0].Bytes[0] = 3;
        await Assert.That(factory.Inputs).IsEmpty();
        await using (var first = sequence.GetAsyncEnumerator())
        {
            await Assert.That(factory.Commands.Single().Creates).IsEqualTo(0);
            await Assert.That(factory.Accesses.Single().Calls).IsEmpty();
            await Assert.That(((byte[])factory.Inputs.Single().ToSql().Parameters.Single().Value!)[0])
                .IsEqualTo((byte)(prepared ? 2 : 3));
            arguments[0].Bytes[0] = 4;
            await Assert.That(await first.MoveNextAsync()).IsTrue();
            await Assert.That(first.Current).IsEquivalentTo(new byte[] { 7 });
            await Assert.That(((byte[])factory.Inputs.Single().ToSql().Parameters.Single().Value!)[0])
                .IsEqualTo((byte)(prepared ? 2 : 3));
        }
        factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([new byte[] { 8 }]) { ColumnNames = ["value"] } };
        await Assert.That((await PlanRows(sequence)).Single()).IsEquivalentTo(new byte[] { 8 });
        await Assert.That(((byte[])factory.Inputs[1].ToSql().Parameters.Single().Value!)[0])
            .IsEqualTo((byte)(prepared ? 2 : 4));
        if (prepared)
        {
            _ = await PlanRows(plan.ExecuteAsyncCore(fixture.Database, arguments));
            await Assert.That(((byte[])factory.Inputs[2].ToSql().Parameters.Single().Value!)[0]).IsEqualTo((byte)4);
        }
        await Assert.That(factory.Commands.All(command => command.Resource.AsyncDisposals == 1)).IsTrue();
    }

    [Test]
    [Arguments("scalar")]
    [Arguments("contains")]
    [Arguments("any")]
    public async Task QueryCaptureReview_PublicSynchronousPreparedExecutionAlsoOwnsSelectedArrays(string selection)
    {
        using var provider = new QueryCaptureSyncProvider(new());
        using var database = new ScriptedDatabase(provider);
        var arguments = new[] { new QueryCaptureBinaryArgument([1]) };
        var plan = selection switch
        {
            "contains" => database.PrepareSequenceQuery(arguments, values => database.Query().BinaryRows
                .Where(row => values.Select(value => value.Bytes).Contains(row.Id)).Select(row => row.Id)),
            "any" => database.PrepareSequenceQuery(arguments, values => database.Query().BinaryRows
                .Where(row => values.Any(value => value.Bytes == row.Id)).Select(row => row.Id)),
            _ => database.PrepareSequenceQuery(arguments, values => database.Query().BinaryRows
                .Where(row => values[0].Bytes == row.Id).Select(row => row.Id))
        };
        arguments[0].Bytes[0] = 2;
        var sequence = plan.Execute(database, arguments);
        await Assert.That(provider.Captured).IsNull();
        arguments[0].Bytes[0] = 3;
        // Stop at native command creation after the real public Execute/parser/render
        // path. This tests binding, not a database's binary-comparison semantics.
        await Assert.That(Capture<InvalidOperationException>(() => sequence.ToArray())).IsSameReferenceAs(provider.Stop);
        await Assert.That(((byte[])provider.Captured!.Parameters.Single().Value!)[0]).IsEqualTo((byte)2);
        arguments[0].Bytes[0] = 4;
        await Assert.That(Capture<InvalidOperationException>(() => sequence.ToArray())).IsSameReferenceAs(provider.Stop);
        await Assert.That(((byte[])provider.Captured!.Parameters.Single().Value!)[0]).IsEqualTo((byte)2);
        await Assert.That(Capture<InvalidOperationException>(() => plan.Execute(database, arguments).ToArray())).IsSameReferenceAs(provider.Stop);
        await Assert.That(((byte[])provider.Captured!.Parameters.Single().Value!)[0]).IsEqualTo((byte)4);
    }

    private sealed class QueryCaptureSyncProvider(ScriptedMutationScenario scenario)
        : CapturedReadProvider<TransactionMutationGuardDb>(scenario)
    {
        internal Sql? Captured { get; private set; }
        internal InvalidOperationException Stop { get; } = new("stop after observing command bindings");

        public override IDbCommand ToDbCommand(IQuery query)
        {
            Captured = query.ToSql();
            throw Stop;
        }
    }

    private sealed record QueryCaptureBinaryArgument(byte[] Bytes);
}
