using System;
using System.Diagnostics.CodeAnalysis;

namespace DataLinq.Mutation;

/// <summary>A success payload for an operation with no value. It does not complete a transaction.</summary>
public readonly record struct Unit;

/// <summary>A successful business result. Constructing this value does not commit or clear a rollback request.</summary>
public readonly record struct Success<T>(T Value);

/// <summary>A typed business rollback payload. Constructing this value alone does not request a rollback.</summary>
public readonly struct Rollback<TFailure> where TFailure : notnull
{
    private readonly TFailure reason;
    private readonly bool initialized;

    /// <summary>Constructs a payload with a non-null reason, without changing transaction state.</summary>
    public Rollback(TFailure reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        this.reason = reason;
        initialized = true;
    }

    /// <summary>Gets the reason. An uninitialized marker throws.</summary>
    public TFailure Reason => initialized ? reason : throw new InvalidOperationException("The rollback payload is uninitialized.");
}

/// <summary>Construction helpers for optional transaction result values.</summary>
public static class TransactionResult
{
    /// <summary>Creates a no-value success payload without completing a transaction.</summary>
    public static Success<Unit> Success() => new(default);

    /// <summary>Creates a success payload, including a permitted null value.</summary>
    public static Success<T> Success<T>(T value) => new(value);
}

/// <summary>An optional success or rollback payload, independent of transaction state and physical completion.</summary>
/// <remarks>
/// Nullable successes are valid. Default results are uninitialized and throw on inspection.
/// Returning a rollback payload does not veto commit; call the participating session's RequestRollback explicitly.
/// Use FromSuccess/FromRollback when the success type is itself Rollback&lt;TFailure&gt;, making implicit conversions ambiguous.
/// Value and HasValue are reserved for a possible future union view; no native union integration is enabled.
/// </remarks>
public readonly struct TransactionResult<T, TFailure> where TFailure : notnull
{
    private readonly byte kind;
    private readonly T value;
    private readonly TFailure failure;

    private TransactionResult(T value) { kind = 1; this.value = value; failure = default!; }
    private TransactionResult(Rollback<TFailure> rollback) { failure = rollback.Reason; kind = 2; value = default!; }

    /// <summary>Gets whether this initialized result carries a success. Does not report transaction completion.</summary>
    public bool IsSuccess { get { EnsureInitialized(); return kind == 1; } }
    /// <summary>Gets the success payload; throws for rollback or uninitialized results.</summary>
    public T SuccessValue => IsSuccess ? value : throw new InvalidOperationException("The result carries a rollback payload.");
    /// <summary>Gets the rollback payload; throws for success or uninitialized results.</summary>
    public TFailure Reason => !IsSuccess ? failure : throw new InvalidOperationException("The result carries a success payload.");

    /// <summary>Returns true for success (including null), false for rollback, and throws for uninitialized results.</summary>
    public bool TryUnwrap([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out TFailure failure)
    {
        EnsureInitialized();
        value = this.value;
        failure = this.failure;
        return kind == 1;
    }

    /// <summary>Invokes the callback for the initialized case without changing transaction state.</summary>
    public TResult Match<TResult>(Func<T, TResult> success, Func<TFailure, TResult> rollback)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(rollback);
        return IsSuccess ? success(value) : rollback(failure);
    }

    private void EnsureInitialized()
    {
        if (kind == 0) throw new InvalidOperationException("The transaction result is uninitialized.");
    }

    /// <summary>Explicitly constructs a success, including marker-shaped or nullable payloads.</summary>
    public static TransactionResult<T, TFailure> FromSuccess(T value) => new(value);
    /// <summary>Explicitly constructs rollback data without requesting rollback on a transaction.</summary>
    public static TransactionResult<T, TFailure> FromRollback(Rollback<TFailure> rollback) => new(rollback);

    /// <summary>Constructs a success, including a permitted null payload.</summary>
    public static implicit operator TransactionResult<T, TFailure>(T value) => new(value);
    /// <summary>Constructs a success from its explicit payload.</summary>
    public static implicit operator TransactionResult<T, TFailure>(Success<T> success) => new(success.Value);
    /// <summary>Constructs a rollback payload. Does not request rollback on any transaction.</summary>
    public static implicit operator TransactionResult<T, TFailure>(Rollback<TFailure> rollback) => new(rollback);
}

/// <summary>An optional transaction result with a string rollback reason and a success payload of type T.</summary>
public readonly struct TransactionResult<T>
{
    private readonly TransactionResult<T, string> result;
    private TransactionResult(TransactionResult<T, string> result) => this.result = result;
    /// <summary>Gets the initialized case; does not report transaction completion.</summary>
    public bool IsSuccess => result.IsSuccess;
    /// <summary>Gets the success value; throws for rollback or uninitialized results.</summary>
    public T SuccessValue => result.SuccessValue;
    /// <summary>Gets the rollback reason; throws for success or uninitialized results.</summary>
    public string Reason => result.Reason;
    /// <summary>Unwraps an initialized result, permitting null successes.</summary>
    public bool TryUnwrap([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out string failure) => result.TryUnwrap(out value, out failure);
    /// <summary>Invokes the callback for the initialized case without changing transaction state.</summary>
    public TResult Match<TResult>(Func<T, TResult> success, Func<string, TResult> rollback) => result.Match(success, rollback);
    /// <summary>Explicitly constructs a success, including marker-shaped or nullable payloads.</summary>
    public static TransactionResult<T> FromSuccess(T value) => new(TransactionResult<T, string>.FromSuccess(value));
    /// <summary>Explicitly constructs rollback data without requesting rollback on a transaction.</summary>
    public static TransactionResult<T> FromRollback(Rollback<string> rollback) => new(TransactionResult<T, string>.FromRollback(rollback));
    /// <summary>Constructs a success.</summary>
    public static implicit operator TransactionResult<T>(T value) => new((TransactionResult<T, string>)value);
    /// <summary>Constructs an explicit success.</summary>
    public static implicit operator TransactionResult<T>(Success<T> success) => new((TransactionResult<T, string>)success);
    /// <summary>Constructs rollback data without requesting rollback.</summary>
    public static implicit operator TransactionResult<T>(Rollback<string> rollback) => new((TransactionResult<T, string>)rollback);
}
