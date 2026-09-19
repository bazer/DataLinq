using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Query;

namespace DataLinq.Execution;

/// <summary>One metadata session's sequential, fully buffered command lifetimes.</summary>
internal sealed class MetadataReadContext
{
    private readonly object gate = new();
    private readonly IAsyncDatabaseAccess access;
    private readonly IAsyncMetadataCommands commands;
    private readonly int? timeout;
    private readonly CancellationToken token;
    private readonly ReadExecutionIdentity identity;
    private readonly List<(ObservedExecutionFailure Failure, ExecutionFailureStage Stage, ExecutionFailureCause Cause)> failures = [];
    private TaskCompletionSource? active;
    private bool closed;

    internal MetadataReadContext(IAsyncDatabaseAccess access, IAsyncMetadataCommands commands, int? timeout, CancellationToken token,
        ReadExecutionIdentity identity = default)
    {
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this.timeout = timeout;
        this.token = token;
        this.identity = identity;
        commands.Validate(timeout);
    }

    internal Task<IReadOnlyList<T>> ReadAsync<T>(Sql sql, Func<IAsyncDataReader, T> materialize)
    {
        ArgumentNullException.ThrowIfNull(materialize);
        return RunAsync<IReadOnlyList<T>>(sql, async execution =>
        {
            var rows = new List<T>();
            // The shared enumerator owns command/reader cleanup and reports read,
            // materialization and cleanup failures with their original identity.
            await foreach (var row in new AsyncReaderEnumerable<T>(() => execution, materialize,
                cancellationToken: token, identity: identity).ConfigureAwait(false))
                rows.Add(row);
            return rows.AsReadOnly();
        });
    }

    internal Task<object?> ExecuteScalarAsync(Sql sql) => RunAsync(sql, execution => execution.ExecuteScalarAsync(token));

    private Task<T> RunAsync<T>(Sql sql, Func<OwnedCommandExecution, Task<T>> execute)
    {
        ArgumentNullException.ThrowIfNull(sql);
        TaskCompletionSource call;
        lock (gate)
        {
            if (closed) throw new InvalidOperationException("This metadata read has ended.");
            if (active is not null)
            {
                var failure = new InvalidOperationException("A metadata command is still active.");
                failures.Add((new(failure, null), ExecutionFailureStage.Validation, ExecutionFailureCause.Unknown));
                throw failure;
            }
            if (failures.Count != 0) throw new InvalidOperationException("This metadata read has failed.");
            active = call = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        // Capture synchronously before the first await; failure is still retained
        // by ExecuteCoreAsync so a custom parser cannot publish a partial success.
        var task = ExecuteCoreAsync(sql, execute, call);
        // A malformed parser can abandon the returned task. CloseAsync still
        // waits for its resources and reports the retained failure; observe the
        // task too, without scheduling any provider execution in this callback.
        _ = task.ContinueWith(static failed => { _ = failed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return task;
    }

    private async Task<T> ExecuteCoreAsync<T>(Sql sql, Func<OwnedCommandExecution, Task<T>> execute, TaskCompletionSource call)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var stage = ExecutionFailureStage.Validation;
        try
        {
            var captured = CapturedSql.Capture(sql);
            var factory = commands.Capture(captured) ?? throw new InvalidOperationException("Metadata capture returned no command factory.");
            if (timeout is { } seconds) factory = new TimedFactory(factory, seconds);
            var execution = new OwnedCommandExecution(access, factory);
            stage = ExecutionFailureStage.CommandExecution;
            var result = await execute(execution).ConfigureAwait(false);
            // Metadata reads must not publish results canceled during cleanup,
            // including scalar reads that have no reader enumerator of their own.
            stage = ExecutionFailureStage.Materialization;
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception failure)
        {
            var cause = failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown;
            lock (gate) failures.Add((ObservedExecutionFailure.Capture(failure), stage, cause));
            throw;
        }
        finally
        {
            lock (gate)
            {
                active = null;
                call.TrySetResult();
            }
        }
    }

    internal void CopyFailuresTo(ExecutionFailures destination)
    {
        lock (gate)
            foreach (var failure in failures) destination.AddObserved(failure.Failure, failure.Stage, failure.Cause, identity.Operation);
    }

    internal async ValueTask CloseAsync(ExecutionFailures destination)
    {
        Task? pending;
        lock (gate)
        {
            closed = true;
            CopyFailuresTo(destination);
            pending = active?.Task;
            if (pending is not null && destination.Primary is null)
                destination.Add(new InvalidOperationException("The metadata parser returned with an unfinished command."),
                    ExecutionFailureCause.Unknown, ExecutionFailureStage.Materialization, identity.Operation);
        }
        if (pending is not null) await pending.ConfigureAwait(false);
        CopyFailuresTo(destination);
    }

    private sealed class TimedFactory(IAsyncOwnedCommandFactory inner, int seconds) : IAsyncOwnedCommandFactory
    {
        public void Validate(AsyncCommandKind kind) => inner.Validate(kind);
        public IAsyncOwnedCommand Create() => new TimedCommand(inner.Create()
            ?? throw new InvalidOperationException("Metadata command factory returned no resource."), seconds);
    }

    private sealed class TimedCommand(IAsyncOwnedCommand inner, int seconds) : IAsyncOwnedCommand
    {
        private bool applied;
        public IDbCommand Command
        {
            get
            {
                var command = inner.Command ?? throw new InvalidOperationException("Metadata command resource returned no command.");
                // Setter failures occur after ownership transfer, so the shared
                // command coordinator can still await cleanup of this resource.
                if (!applied) { command.CommandTimeout = seconds; applied = true; }
                return command;
            }
        }
        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
