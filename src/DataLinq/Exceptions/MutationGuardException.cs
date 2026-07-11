using System;

namespace DataLinq.Exceptions;

/// <summary>
/// The exception thrown when DataLinq rejects a model mutation before provider command execution.
/// </summary>
public sealed class MutationGuardException : InvalidOperationException
{
    internal MutationGuardException(string message)
        : base(message)
    {
    }
}
