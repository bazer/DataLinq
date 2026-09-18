using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>Invocation-recorded dispatch, not a provider's inference about statement effects.</summary>
internal interface IAsyncCommandDispatchEvidence
{
    bool CommandDispatched { get; }
}

/// <summary>Raw readers cannot acquire the ordinary-read exception after dispatch.</summary>
internal class RawAsyncReaderSource : IAsyncReaderSource, IAsyncReadFailureEvidence, IAsyncCommandDispatchEvidence
{
    private readonly IAsyncReaderSource source;
    private bool opening;

    private RawAsyncReaderSource(IAsyncReaderSource source) => this.source = source;

    internal static IAsyncReaderSource Wrap(IAsyncReaderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source is IAsyncTransactionReaderSource owned ? new TransactionSource(owned) : new RawAsyncReaderSource(source);
    }

    public bool CommandDispatched => source is IAsyncCommandDispatchEvidence evidence ? evidence.CommandDispatched : opening;
    public void Validate() => source.Validate();

    public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken)
    {
        opening = false;
        Validate();
        cancellationToken.ThrowIfCancellationRequested();
        opening = true;
        return source.OpenReaderAsync(cancellationToken);
    }

    public ReadFailureEvidence GetReadFailureEvidence(Exception failure)
    {
        var evidence = source is IAsyncReadFailureEvidence classifier ? classifier.GetReadFailureEvidence(failure) : new();
        // Unknown dispatch must not weaken a terminal initialization failure supplied
        // by a custom source that has no separate dispatch-observation capability.
        return CommandDispatched && evidence.Effects != ExecutionEffects.Initialization
            ? evidence with { Effects = ExecutionEffects.Unknown } : evidence;
    }

    private sealed class TransactionSource(IAsyncTransactionReaderSource ownedSource) : RawAsyncReaderSource(ownedSource), IAsyncTransactionReaderSource
    {
        public Task<IAsyncDataReader> OpenReaderAsync(TransactionOperationGate.Step owner, CancellationToken token)
        {
            opening = false;
            Validate();
            token.ThrowIfCancellationRequested();
            opening = true;
            return ownedSource.OpenReaderAsync(owner, token);
        }
    }
}
