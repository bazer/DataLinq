using System;

namespace DataLinq.Exceptions;

/// <summary>
/// The exception thrown when an operation is rejected because a prior mutation failure poisoned
/// the DataLinq transaction.
/// </summary>
public sealed class TransactionPoisonedException : InvalidOperationException
{
    internal TransactionPoisonedException(string message)
        : base(message)
    {
    }
}
