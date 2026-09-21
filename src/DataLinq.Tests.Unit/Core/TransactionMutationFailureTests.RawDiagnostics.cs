using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("non-query", false, false, false)]
    [Arguments("non-query", false, false, true)]
    [Arguments("non-query", false, true, false)]
    [Arguments("non-query", false, true, true)]
    [Arguments("non-query", true, false, false)]
    [Arguments("non-query", true, false, true)]
    [Arguments("non-query", true, true, false)]
    [Arguments("non-query", true, true, true)]
    [Arguments("scalar", false, false, false)]
    [Arguments("scalar", false, false, true)]
    [Arguments("scalar", false, true, false)]
    [Arguments("scalar", false, true, true)]
    [Arguments("scalar", true, false, false)]
    [Arguments("scalar", true, false, true)]
    [Arguments("scalar", true, true, false)]
    [Arguments("scalar", true, true, true)]
    [Arguments("typed", false, false, false)]
    [Arguments("typed", false, false, true)]
    [Arguments("typed", false, true, false)]
    [Arguments("typed", false, true, true)]
    [Arguments("typed", true, false, false)]
    [Arguments("typed", true, false, true)]
    [Arguments("typed", true, true, false)]
    [Arguments("typed", true, true, true)]
    [Arguments("reader", false, false, false)]
    [Arguments("reader", false, false, true)]
    [Arguments("reader", false, true, false)]
    [Arguments("reader", false, true, true)]
    [Arguments("reader", true, false, false)]
    [Arguments("reader", true, false, true)]
    [Arguments("reader", true, true, false)]
    [Arguments("reader", true, true, true)]
    [Arguments("rows", false, false, false)]
    [Arguments("rows", false, false, true)]
    [Arguments("rows", false, true, false)]
    [Arguments("rows", false, true, true)]
    [Arguments("rows", true, false, false)]
    [Arguments("rows", true, false, true)]
    [Arguments("rows", true, true, false)]
    [Arguments("rows", true, true, true)]
    public async Task RawDiagnostics_StandaloneBoundaryOwnsItsProviderIdentity(string kind, bool asynchronous, bool withProvider, bool borrowed)
    {
        using var fixture = new ScriptedFixture();
        var expected = new Exception("raw execution");
        ExecutionFailureContext? nested = null;
        void Report(Exception failure)
        {
            nested = new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [],
                operation: ExecutionOperationKind.Commit, providerInstanceId: "foreign-provider",
                activeOperation: ExecutionOperationKind.Save);
            ExecutionFailureContexts.Attach(failure, nested);
        }
        var checkpoint = new AsyncCheckpoint(paused: true) { ReportingFailure = Report };
        checkpoint.Fail(expected);
        var native = new ControlledAsyncDatabaseAccess(checkpoint) { FailureEvidence = TrustedScalarRead };
        var eager = new ControlledEagerCommandFactory { Access = native };
        var readers = new ControlledSqlReaderFactory { CreateAccess = _ => native, CreateBorrowedAccess = _ => native };
        fixture.Scenario.AsyncCommands = eager;
        fixture.Scenario.AsyncSqlReaders = readers;
        var sync = new SyncRawTestFactory { ExecutionFailure = expected, Executing = () => Report(expected) };
        DatabaseAccess access = asynchronous
            ? new ScriptedDatabaseAccess(withProvider ? fixture.Provider : null, fixture.Scenario)
            : new SyncRawStandaloneTestAccess(sync, withProvider ? fixture.Provider : null);
        using var command = new ControlledCommand { CommandText = "SELECT value" };
        var failure = await AsyncEnumerationFailureOf(() => RunStandaloneRaw(access, kind, asynchronous, borrowed ? command : null));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.ProviderInstanceId).IsEqualTo(withProvider ? fixture.Provider.TelemetryInstanceId : null);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.ActiveOperation).IsNull();
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(nested!.ProviderInstanceId).IsEqualTo("foreign-provider");
        await Assert.That(nested.Operation).IsEqualTo(ExecutionOperationKind.Commit);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        var disposals = asynchronous
            ? eager.Commands.Sum(x => x.Resource.AsyncDisposals) + readers.Commands.Sum(x => x.Resource.AsyncDisposals)
            : sync.CommandDisposals;
        await Assert.That(disposals).IsEqualTo(borrowed ? 0 : 1);
        await Assert.That(asynchronous ? native.Calls.Count(x => x.StartsWith("dispatch:", StringComparison.Ordinal)) : sync.Executions.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RawDiagnostics_CommandTelemetryOwnsAnExplicitlyAbsentProvider(bool asynchronous, bool withProvider)
    {
        using var fixture = new ScriptedFixture();
        var access = new ScriptedDatabaseAccess(withProvider ? fixture.Provider : null, fixture.Scenario);
        using var command = new ControlledCommand();
        var expected = new Exception("native");
        void Execute()
        {
            ExecutionFailureContexts.Attach(expected, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [],
                operation: ExecutionOperationKind.Commit, providerInstanceId: "foreign-provider"));
            throw expected;
        }
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            if (asynchronous)
                await access.ExecuteCommandWithTelemetryAsync(command, "non_query", false, null, default,
                    () => { Execute(); return Task.FromResult(7); });
            else access.RunStandaloneCommandTelemetry(command, Execute);
        });
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.ProviderInstanceId).IsEqualTo(withProvider ? fixture.Provider.TelemetryInstanceId : null);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Unknown);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.CommandDispatch!.Dispatched).IsTrue();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    private static async Task RunStandaloneRaw(DatabaseAccess access, string kind, bool asynchronous, IDbCommand? command)
    {
        const string sql = "SELECT value";
        if (asynchronous)
        {
            switch (kind)
            {
                case "non-query": _ = await (command is null ? access.ExecuteNonQueryAsyncCore(sql) : access.ExecuteNonQueryAsyncCore(command)); break;
                case "scalar": _ = await (command is null ? access.ExecuteScalarAsyncCore(sql) : access.ExecuteScalarAsyncCore(command)); break;
                case "typed": _ = await (command is null ? access.ExecuteScalarAsyncCore<int>(sql) : access.ExecuteScalarAsyncCore<int>(command)); break;
                case "reader": await using (var reader = await (command is null ? access.ExecuteReaderAsyncCore(sql) : access.ExecuteReaderAsyncCore(command))) { } break;
                default: await foreach (var _ in command is null ? access.ReadReaderAsyncCore(sql) : access.ReadReaderAsyncCore(command)) { } break;
            }
        }
        else
        {
            switch (kind)
            {
                case "non-query": _ = command is null ? access.ExecuteNonQuery(sql) : access.ExecuteNonQuery(command); break;
                case "scalar": _ = command is null ? access.ExecuteScalar(sql) : access.ExecuteScalar(command); break;
                case "typed": _ = command is null ? access.ExecuteScalar<int>(sql) : access.ExecuteScalar<int>(command); break;
                case "reader": using (var reader = command is null ? access.ExecuteReader(sql) : access.ExecuteReader(command)) { } break;
                default: foreach (var _ in command is null ? access.ReadReaderSyncCore(sql) : access.ReadReaderSyncCore(command)) { } break;
            }
        }
    }

    private sealed partial class ScriptedDatabaseAccess
    {
        internal int RunStandaloneCommandTelemetry(IDbCommand command, Action execute) =>
            ExecuteCommandWithTelemetry(command, "non_query", false, null, () => { execute(); return 7; });
    }
}
