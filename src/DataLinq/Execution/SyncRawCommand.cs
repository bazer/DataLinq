using System;
using System.Data;
using System.Threading;

namespace DataLinq.Execution;

internal enum SyncCommandKind { Reader, Scalar, NonQuery }

/// <summary>Direct provider dispatch, distinct from public raw admission.</summary>
internal interface ISyncCommandAccess
{
    void ValidateCommand(IDbCommand command, SyncCommandKind kind);
    IDataLinqDataReader ExecuteReader(IDbCommand command);
    object? ExecuteScalar(IDbCommand command);
    int ExecuteNonQuery(IDbCommand command);
}

/// <summary>I/O-free capture. Created commands belong to the invocation; supplied commands never do.</summary>
internal interface ISyncRawCommandFactory
{
    SyncRawCommand BindCommand(string sql);
    SyncRawCommand BindCommand(IDbCommand command);
    SyncRawScalarInvocation<T> BindScalar<T>(string sql);
    SyncRawScalarInvocation<T> BindScalar<T>(IDbCommand command);

    internal static ISyncRawCommandFactory Require(object access) => access as ISyncRawCommandFactory
        ?? throw new NotSupportedException("This adapter does not implement managed synchronous raw command binding.");
}

internal sealed record SyncRawScalarInvocation<T>(SyncRawCommand Command, Func<object?, T> Convert);

internal interface ISyncCommandInitialization
{
    TransactionInitializationState State { get; }
    void Validate();
    void Initialize(TransactionOperationGate.Step owner);
}

/// <summary>Single invocation; owns only resources it creates. No public reentry or async fallback.</summary>
internal sealed class SyncRawCommand : ISyncRawModelReaderSource
{
    private readonly ISyncCommandAccess access;
    private readonly Func<IDbCommand>? create;
    private readonly IDbCommand? borrowed;
    private readonly Action<SyncCommandKind>? validateFactory;
    private readonly ISyncCommandInitialization? initialization;
    private IDbCommand? command;
    private int started;
    internal bool Dispatched { get; private set; }
    internal ExecutionFailureStage Stage { get; private set; } = ExecutionFailureStage.Validation;

    internal SyncRawCommand(ISyncCommandAccess access, Func<IDbCommand> create,
        Action<SyncCommandKind>? validateFactory = null, ISyncCommandInitialization? initialization = null)
    {
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        this.validateFactory = validateFactory;
        this.initialization = initialization;
    }

    internal SyncRawCommand(ISyncCommandAccess access, IDbCommand borrowed, ISyncCommandInitialization? initialization = null)
    {
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.borrowed = borrowed ?? throw new ArgumentNullException(nameof(borrowed));
        this.initialization = initialization;
    }

    internal void Validate(SyncCommandKind kind, bool hasOwner)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (initialization is not null && !hasOwner)
            throw new InvalidOperationException("Transaction initialization requires a private execution owner.");
        initialization?.Validate();
        validateFactory?.Invoke(kind);
        if (borrowed is not null) access.ValidateCommand(borrowed, kind);
        if (Volatile.Read(ref started) != 0)
            throw new InvalidOperationException("This captured command invocation has already started.");
    }

    // Claim before entering a cleanup-owning try/finally. A rejected second caller
    // must not clean up resources belonging to the first invocation.
    internal void Reserve(SyncCommandKind kind, bool hasOwner)
    {
        Validate(kind, hasOwner);
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            throw new InvalidOperationException("This captured command invocation has already started.");
    }

    private IDbCommand Prepare(SyncCommandKind kind, TransactionOperationGate.Step? owner)
    {
        if (Interlocked.CompareExchange(ref started, 2, 1) != 1)
            throw new InvalidOperationException("Command dispatch requires its reserved invocation.");
        Stage = ExecutionFailureStage.Initialization;
        initialization?.Initialize(owner!);
        Stage = ExecutionFailureStage.Validation;
        command = borrowed ?? create!() ?? throw new InvalidOperationException("The command factory returned no command.");
        access.ValidateCommand(command, kind);
        Stage = ExecutionFailureStage.CommandExecution;
        Dispatched = true;
        return command;
    }

    internal IDataLinqDataReader ExecuteReader(TransactionOperationGate.Step? owner)
    {
        var prepared = Prepare(SyncCommandKind.Reader, owner);
        try { return access.ExecuteReader(prepared) ?? throw new InvalidOperationException("Reader acquisition returned no reader."); }
        catch (Exception failure) { ObserveDispatch(failure, prepared); throw; }
    }

    internal object? ExecuteScalar(TransactionOperationGate.Step? owner)
    {
        var prepared = Prepare(SyncCommandKind.Scalar, owner);
        try { return access.ExecuteScalar(prepared); }
        catch (Exception failure) { ObserveDispatch(failure, prepared); throw; }
    }

    internal int ExecuteNonQuery(TransactionOperationGate.Step? owner)
    {
        var prepared = Prepare(SyncCommandKind.NonQuery, owner);
        try { return access.ExecuteNonQuery(prepared); }
        catch (Exception failure) { ObserveDispatch(failure, prepared); throw; }
    }

    private void ObserveDispatch(Exception failure, IDbCommand prepared)
    {
        // Capture facts about this command before cleanup drops the live resource.
        if (CommandDispatchEvidence.ProvesNoDispatch(failure, prepared)) Dispatched = false;
    }

    internal ExecutionFailures? DisposeOwnedCommand(ExecutionFailures? failures)
    {
        var owned = borrowed is null ? command : null;
        command = null;
        if (owned is null) return failures;
        using var diagnostics = ExecutionFailureScope.Begin();
        try { owned.Dispose(); }
        catch (Exception failure) { (failures ??= new()).AddCleanup(failure); }
        return failures;
    }

    internal ReadFailureEvidence GetFailureEvidence(Exception failure)
    {
        if (initialization?.State == TransactionInitializationState.Failed)
            return new(Effects: ExecutionEffects.Initialization);
        var evidence = access is IAsyncReadFailureEvidence classifier
            ? classifier.GetReadFailureEvidence(failure) ?? throw new InvalidOperationException("The provider returned no failure evidence.")
            : new();
        // No dispatch does not bypass optional assessment or repair lost integrity.
        if (evidence.Effects == ExecutionEffects.Initialization) return evidence;
        if (!Dispatched)
            return evidence with { Effects = ExecutionEffects.NoStatement,
                Integrity = evidence.Integrity == TransactionIntegrity.Lost ? TransactionIntegrity.Lost : TransactionIntegrity.Confirmed };
        // Raw results cannot establish read-only effects. Preserve stronger initialization
        // evidence, but never permit the ordinary-read exception after raw dispatch.
        return evidence with { Effects = ExecutionEffects.Unknown };
    }

    ExecutionFailureStage ISyncRawModelReaderSource.Stage => Stage;
    void ISyncRawModelReaderSource.Reserve() => Reserve(SyncCommandKind.Reader, hasOwner: true);
    IDataLinqDataReader ISyncRawModelReaderSource.OpenReader(TransactionOperationGate.Step owner) => ExecuteReader(owner);
    ExecutionFailures? ISyncRawModelReaderSource.Dispose(IDataLinqDataReader? reader, ExecutionFailures? failures)
        => DisposeOwnedCommand(SyncReaderCleanup.Dispose(reader, failures));
    ReadFailureEvidence ISyncRawModelReaderSource.GetFailureEvidence(Exception failure) => GetFailureEvidence(failure);
}
