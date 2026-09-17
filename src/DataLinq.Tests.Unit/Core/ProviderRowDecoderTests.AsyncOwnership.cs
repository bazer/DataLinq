using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class ProviderRowDecoderTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncOwnedReader_PreservesBinaryCapabilityAndRealCanonicalMaterialization(bool transfersOwnership)
    {
        var table = CreateSimplePrimaryKeyTable(typeof(byte[]), "key");
        var bytes = new byte[] { 1, 2, 3 };
        AsyncRecordingReader native = transfersOwnership ? new AsyncOwnedBinaryReader([bytes]) : new AsyncRecordingReader([bytes]);
        var access = new ControlledAsyncDatabaseAccess { ReaderOverride = native };
        var factory = new ControlledOwnedCommandFactory();
        var source = new OwnedCommandExecution(access, factory);
        await using var reader = await source.OpenReaderAsync(default);
        await Assert.That(reader is IDataLinqOwnedBinaryBufferReader).IsEqualTo(transfersOwnership);
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        var canonical = ProviderRowDecoder.DecodeFullRow(reader, table, "async:owned");
        await Assert.That(ReferenceEquals(canonical.GetBorrowedValue(0), bytes)).IsEqualTo(transfersOwnership);
        var model = ProviderRowMaterializer.Materialize(canonical, "async:owned");
        await reader.DisposeAsync();
        var exposed = (byte[])model[0]!;
        exposed[0] = 9;
        await Assert.That(((byte[])model[0]!)[0]).IsEqualTo((byte)1);
        await Assert.That(((byte[])canonical.GetBorrowedValue(0)!)[0]).IsEqualTo((byte)1);
        if (native is AsyncOwnedBinaryReader owned) await Assert.That(owned.OwnedReads).IsEqualTo(1);
        await Assert.That(native.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncOwnedSequence_UsesCanonicalDecoderAndScalarConverter_AfterAwaitedAdvancement()
    {
        var converter = new RecordingIdConverter();
        var table = CreateTable(converter);
        var native = new AsyncRecordingReader([42, "Ada"]) { Advance = new(paused: true) };
        var access = new ControlledAsyncDatabaseAccess { ReaderOverride = native };
        var factory = new ControlledOwnedCommandFactory();
        var sequence = new AsyncReaderEnumerable<RowData>(() => new OwnedCommandExecution(access, factory),
            reader => ProviderRowMaterializer.Materialize(ProviderRowDecoder.DecodeFullRow(reader, table, "async:owned"), "async:owned"));
        await using var rows = sequence.GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await native.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try { await Assert.That(converter.FromProviderCalls).IsEqualTo(0); }
        finally { native.Advance.Release(); }
        await Assert.That(await pending).IsTrue();
        var row = rows.Current;
        await Assert.That(row[table.GetColumnByDbName("id")]).IsEqualTo(new ModelId(42));
        await Assert.That(row[table.GetColumnByDbName("name")]).IsEqualTo("Ada");
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
        await Assert.That(row[table.GetColumnByDbName("id")]).IsEqualTo(new ModelId(42));
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncOwnedSequence_DecodingFailureRetainsTypedErrorAndBothCleanupFailures()
    {
        var converter = new RecordingIdConverter();
        var table = CreateTable(converter);
        var physical = new FormatException("invalid int");
        var native = new AsyncRecordingReader([42, "Ada"]) { Int32Failure = physical, Cleanup = new(paused: true) };
        var readerCleanup = new Exception("reader cleanup");
        native.Cleanup.Fail(readerCleanup);
        var factory = new ControlledOwnedCommandFactory();
        var commandCleanup = new Exception("command cleanup");
        factory.Resource.Cleanup = new(paused: true);
        factory.Resource.Cleanup.Fail(commandCleanup);
        var access = new ControlledAsyncDatabaseAccess { ReaderOverride = native };
        var sequence = new AsyncReaderEnumerable<RowData>(() => new OwnedCommandExecution(access, factory),
            reader => ProviderRowMaterializer.Materialize(ProviderRowDecoder.DecodeFullRow(reader, table, "async:owned"), "async:owned"));
        await using var rows = sequence.GetAsyncEnumerator();
        var failure = await OwnedAsyncCommandTests.Fails(() => rows.MoveNextAsync().AsTask());
        await Assert.That(failure).IsTypeOf<ProviderValueDecodingException>();
        await Assert.That(failure.InnerException).IsSameReferenceAs(physical);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(2);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(readerCleanup);
        await Assert.That(context.SecondaryFailures[1].Exception).IsSameReferenceAs(commandCleanup);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    [Test]
    public async Task OwnedReader_SynchronousDisposalRemainsDirect_AndSharesDisposedStateWithAsync()
    {
        var native = new AsyncRecordingReader([1]);
        var factory = new ControlledOwnedCommandFactory();
        var reader = await new OwnedCommandExecution(new ControlledAsyncDatabaseAccess { ReaderOverride = native }, factory).OpenReaderAsync(default);
        reader.Dispose();
        await reader.DisposeAsync();
        reader.Dispose();
        await Assert.That(native.AsyncDisposals).IsEqualTo(0);
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
    }

    private class AsyncRecordingReader(object?[] values) : RecordingReader(values), IAsyncDataReader
    {
        private int rows;
        internal AsyncCheckpoint Advance { get; set; } = new();
        internal AsyncCheckpoint Cleanup { get; set; } = new();
        internal int AsyncDisposals { get; private set; }
        public async Task<bool> ReadNextRowAsync(CancellationToken token)
        {
            await Advance.ReachAsync(token);
            return rows++ == 0;
        }
        public async ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            await Cleanup.ReachAsync(CancellationToken.None);
        }
    }

    private sealed class AsyncOwnedBinaryReader(object?[] values) : AsyncRecordingReader(values), IDataLinqOwnedBinaryBufferReader
    {
        internal int OwnedReads { get; private set; }
        public byte[]? TakeOwnedBytes(int ordinal) { OwnedReads++; return (byte[]?)values[ordinal]; }
    }
}
