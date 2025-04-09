using System;
using DataLinq.ErrorHandling;

namespace DataLinq.Exceptions;

public class DLOptionFailureException(IDLOptionFailure failure) : Exception
{
    public IDLOptionFailure Failure { get; } = failure;

    public override string Message => Failure.ToString();
}

public class DLOptionFailureException<T>(DLOptionFailure<T> failure) : DLOptionFailureException(failure)
{
    public new DLOptionFailure<T> Failure => (DLOptionFailure<T>)base.Failure;
}
