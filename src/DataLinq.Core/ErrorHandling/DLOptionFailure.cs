using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Extensions.Helpers;

namespace DataLinq.ErrorHandling;

public enum DLFailureType
{
    Unspecified,
    Exception,
    NotImplemented,
    InvalidArgument,
    UnexpectedNull,
    InvalidType,
    Aggregation
}

public abstract class IDLOptionFailure
{
    public static implicit operator string(IDLOptionFailure optionFailure) =>
        optionFailure.ToString();

    public static implicit operator IDLOptionFailure(string failure) =>
        DLOptionFailure.Fail(failure);

    
}

public static class DLOptionFailure
{
    public static DLOptionFailure<T> Fail<T>(T failure) =>
        new(failure);

    public static DLOptionFailure<T> Fail<T>(DLFailureType type, T failure) =>
        new(type, failure);

    public static DLOptionFailure<T> Fail<T>(T failure, IEnumerable<IDLOptionFailure> innerFailures) =>
        new(failure, innerFailures);

    public static DLOptionFailure<T> Fail<T>(DLFailureType type, T failure, IEnumerable<IDLOptionFailure> innerFailures) =>
        new(failure, innerFailures);
}

public class DLOptionFailure<T> : IDLOptionFailure
{
    public DLFailureType Type { get; }
    public T Failure { get; }
    public IDLOptionFailure[] InnerFailures { get; } = [];

    public DLOptionFailure(T failure)
    {
        Type = failure is Exception ? DLFailureType.Exception : DLFailureType.Unspecified;
        Failure = failure;
    }

    public DLOptionFailure(DLFailureType type, T failure)
    {
        Type = type;
        Failure = failure;
    }

    public DLOptionFailure(T failure, IEnumerable<IDLOptionFailure> innerFailure)
    {
        Type = failure is Exception ? DLFailureType.Exception : DLFailureType.Aggregation;
        Failure = failure;
        InnerFailures = [.. innerFailure];
    }

    public DLOptionFailure(DLFailureType type, T failure, IEnumerable<IDLOptionFailure> innerFailure)
    {
        Type = type;
        Failure = failure;
        InnerFailures = [.. innerFailure];
    }

    public override string ToString()
    {
        return $"[{Type}] {Failure?.ToString()}\n{InnerFailures.ToJoinedString("\n")}";
    }

    public static implicit operator T(DLOptionFailure<T> optionFailure) =>
        optionFailure.Failure;

    public static implicit operator DLOptionFailure<T>(T failure) =>
        DLOptionFailure.Fail(failure);

    public static implicit operator DLOptionFailure<string>(IEnumerable<IDLOptionFailure> optionFailures) =>
        new DLOptionFailure<string>(DLFailureType.Aggregation, "Aggregated failure", optionFailures);
}
