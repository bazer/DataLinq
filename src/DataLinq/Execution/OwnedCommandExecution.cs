using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// One owned command invocation. Readers take command ownership on successful acquisition;
/// scalar/non-query calls release it before returning. The enclosing operation owns transaction
/// admission/initialization; standalone connection ownership stays with the provider access.
/// </summary>
internal sealed class OwnedCommandExecution : IAsyncReaderSource, IAsyncScalarSource, IAsyncReadFailureEvidence
{
    private readonly IAsyncDatabaseAccess access;
    private readonly IAsyncOwnedCommandFactory factory;
    private readonly uint? transactionId;
    private int started;
    private bool dispatched;

    internal OwnedCommandExecution(IAsyncDatabaseAccess access, IAsyncOwnedCommandFactory factory, uint? transactionId = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(factory);
        this.access = access;
        this.factory = factory;
        this.transactionId = transactionId;
    }

    public void Validate() => Validate(AsyncCommandKind.Reader);
    void IAsyncScalarSource.Validate() => Validate(AsyncCommandKind.Scalar);

    internal bool Dispatched => dispatched;

    internal void Validate(AsyncCommandKind kind)
    {
        factory.Validate(kind);
        if (Volatile.Read(ref started) != 0)
            throw new InvalidOperationException("This captured command invocation has already started.");
    }

    public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken)
    {
        Begin(AsyncCommandKind.Reader, cancellationToken);
        return OpenReaderCoreAsync(cancellationToken);
    }

    private async Task<IAsyncDataReader> OpenReaderCoreAsync(CancellationToken token)
    {
        IAsyncOwnedCommand? command = null;
        IAsyncDataReader? reader = null;
        var stage = ExecutionFailureStage.Validation;
        ExecutionFailures failures;
        try
        {
            command = Create();
            var borrowed = ValidateCommand(command, AsyncCommandKind.Reader, token);
            stage = ExecutionFailureStage.CommandExecution;
            dispatched = true;
            reader = await access.ExecuteReaderAsync(borrowed, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Reader acquisition returned no reader.");
            // Do not cancel successful acquisition retroactively. The enumerator owns the
            // next cancellation checkpoint and must first receive the acquired resources.
            return OwnedAsyncDataReader.Create(reader, command, transactionId);
        }
        catch (Exception failure) { failures = Capture(failure, stage, token); }
        await AsyncCommandCleanup.DisposeAsync(reader, command, transactionId, failures).ConfigureAwait(false);
        throw new InvalidOperationException("Failed reader acquisition did not report its failure.");
    }

    public Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        Begin(AsyncCommandKind.Scalar, cancellationToken);
        return ExecuteAsync(AsyncCommandKind.Scalar, cancellationToken, static (access, command, token) => access.ExecuteScalarAsync(command, token));
    }

    internal Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        Begin(AsyncCommandKind.NonQuery, cancellationToken);
        return ExecuteAsync(AsyncCommandKind.NonQuery, cancellationToken, static (access, command, token) => access.ExecuteNonQueryAsync(command, token));
    }

    private async Task<TResult> ExecuteAsync<TResult>(AsyncCommandKind kind, CancellationToken token,
        Func<IAsyncDatabaseAccess, IDbCommand, CancellationToken, Task<TResult>> execute)
    {
        IAsyncOwnedCommand? command = null;
        ExecutionFailures? failures = null;
        var result = default(TResult)!;
        var stage = ExecutionFailureStage.Validation;
        try
        {
            command = Create();
            var borrowed = ValidateCommand(command, kind, token);
            stage = ExecutionFailureStage.CommandExecution;
            dispatched = true;
            result = await execute(access, borrowed, token).ConfigureAwait(false);
        }
        catch (Exception failure) { failures = Capture(failure, stage, token); }
        // Cleanup is independent of the request token. It must settle and succeed before
        // returning a normal result, including when execution completed despite cancellation.
        await AsyncCommandCleanup.DisposeAsync(null, command, transactionId, failures).ConfigureAwait(false);
        return result;
    }

    private void Begin(AsyncCommandKind kind, CancellationToken token)
    {
        Validate(kind);
        token.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            throw new InvalidOperationException("This captured command invocation has already started.");
    }

    private IAsyncOwnedCommand Create() => factory.Create()
        ?? throw new InvalidOperationException("The command factory returned no owned command.");

    private IDbCommand ValidateCommand(IAsyncOwnedCommand resource, AsyncCommandKind kind, CancellationToken token)
    {
        var command = resource.Command ?? throw new InvalidOperationException("The command resource returned no command.");
        access.ValidateCommand(command, kind);
        token.ThrowIfCancellationRequested();
        return command;
    }

    private static ExecutionFailures Capture(Exception failure, ExecutionFailureStage stage, CancellationToken token)
    {
        var failures = new ExecutionFailures();
        failures.AddReported(failure, stage, failure is OperationCanceledException canceled &&
            canceled.CancellationToken == token && token.IsCancellationRequested
                ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
        return failures;
    }

    public ReadFailureEvidence GetReadFailureEvidence(Exception failure)
    {
        var evidence = access is IAsyncReadFailureEvidence classifier ? classifier.GetReadFailureEvidence(failure) : new();
        // Command construction/validation cannot perform provider I/O. Cleanup failure
        // still removes Continue when the enclosing reader evaluates this evidence.
        return dispatched ? evidence : evidence with { Effects = ExecutionEffects.NoStatement, Integrity = TransactionIntegrity.Confirmed };
    }
}
