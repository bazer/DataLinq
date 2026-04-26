using System;
using System.Collections.Generic;
using DataLinq.Exceptions;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using ThrowAway;

namespace DataLinq.ErrorHandling;

public enum DLFailureType
{
    Unspecified,
    Exception,
    NotImplemented,
    InvalidArgument,
    UnexpectedNull,
    InvalidType,
    Aggregation,
    FileNotFound,
    InvalidModel
}

public class FailureWithDefinition<T>
{
    public IDefinition Definition { get; }
    public T Failure { get; }
    public FailureWithDefinition(T failure, IDefinition definition)
    {
        Failure = failure;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }
    public override string ToString()
    {
        if (Definition.CsFile == null)
            return $"{Failure} in {Definition}";

        return $"{Failure} in {Definition}, {Definition.CsFile?.FullPath}";
    }
}

public abstract class IDLOptionFailure
{
    public DLFailureType FailureType { get; protected set; }
    public IDLOptionFailure[] InnerFailures { get; protected set; } = [];
    public bool HasInnerFailures => InnerFailures.Length > 0;
    public SourceLocation? SourceLocation { get; protected set; }
    public abstract string Message { get; }

    public SourceLocation? GetMostRelevantSourceLocation()
    {
        if (SourceLocation.HasValue)
            return SourceLocation;

        foreach (var innerFailure in InnerFailures)
        {
            var innerLocation = innerFailure.GetMostRelevantSourceLocation();
            if (innerLocation.HasValue)
                return innerLocation;
        }

        return null;
    }

    public static implicit operator string(IDLOptionFailure optionFailure) =>
        optionFailure.ToString();

    public static implicit operator IDLOptionFailure(string failure) =>
        DLOptionFailure.Fail(failure);

    public static implicit operator IDLOptionFailure(List<IDLOptionFailure> optionFailures) =>
        DLOptionFailure.AggregateFail(optionFailures);

    override public abstract string ToString();
    //public static implicit operator Option<T, IDLOptionFailure>(List<IDLOptionFailure> optionFailures) =>
    //    Option.Fail<T, IDLOptionFailure>(DLOptionFailure.AggregateFail(optionFailures));
}

public static class DLOptionFailure
{
    private static SourceLocation? GetDefinitionLocation(IDefinition definition) =>
        definition.CsFile.HasValue
            ? new SourceLocation(definition.CsFile.Value)
            : null;

    public static DLOptionFailure<T> Fail<T>(T failure) =>
        new(failure);

    public static DLOptionFailure<T> Fail<T>(T failure, SourceLocation sourceLocation) =>
        new(failure, sourceLocation);

    public static DLOptionFailure<FailureWithDefinition<T>> Fail<T>(T failure, IDefinition definition)
    {
        var wrappedFailure = new FailureWithDefinition<T>(failure, definition);
        var definitionLocation = GetDefinitionLocation(definition);
        return definitionLocation.HasValue
            ? new DLOptionFailure<FailureWithDefinition<T>>(wrappedFailure, definitionLocation.Value)
            : new DLOptionFailure<FailureWithDefinition<T>>(wrappedFailure);
    }

    public static DLOptionFailure<FailureWithDefinition<T>> Fail<T>(T failure, IDefinition definition, SourceLocation sourceLocation) =>
        new(new FailureWithDefinition<T>(failure, definition), sourceLocation);

    public static DLOptionFailure<T> Fail<T>(DLFailureType type, T failure) =>
        new(type, failure);

    public static DLOptionFailure<T> Fail<T>(DLFailureType type, T failure, SourceLocation sourceLocation) =>
        new(type, failure, sourceLocation);

    public static DLOptionFailure<FailureWithDefinition<T>> Fail<T>(DLFailureType type, T failure, IDefinition definition)
    {
        var wrappedFailure = new FailureWithDefinition<T>(failure, definition);
        var definitionLocation = GetDefinitionLocation(definition);
        return definitionLocation.HasValue
            ? new DLOptionFailure<FailureWithDefinition<T>>(type, wrappedFailure, definitionLocation.Value)
            : new DLOptionFailure<FailureWithDefinition<T>>(type, wrappedFailure);
    }

    public static DLOptionFailure<FailureWithDefinition<T>> Fail<T>(DLFailureType type, T failure, IDefinition definition, SourceLocation sourceLocation) =>
        new(type, new FailureWithDefinition<T>(failure, definition), sourceLocation);

    public static DLOptionFailure<T> Fail<T>(T failure, IEnumerable<IDLOptionFailure> innerFailures) =>
        new(failure, innerFailures);

    public static DLOptionFailure<FailureWithDefinition<T>> Fail<T>(T failure, IDefinition definition, IEnumerable<IDLOptionFailure> innerFailures) =>
        new(new FailureWithDefinition<T>(failure, definition), innerFailures);

    public static DLOptionFailure<T> Fail<T>(DLFailureType type, T failure, IEnumerable<IDLOptionFailure> innerFailures) =>
        new(type, failure, innerFailures);

    public static DLOptionFailure<FailureWithDefinition<T>> Fail<T>(DLFailureType type, T failure, IDefinition definition, IEnumerable<IDLOptionFailure> innerFailures) =>
        new(type, new FailureWithDefinition<T>(failure, definition), innerFailures);

    public static DLOptionFailure<string> AggregateFail(IEnumerable<IDLOptionFailure> innerFailures) =>
        new("", innerFailures);

    public static DLOptionFailureException<T> Exception<T>(DLFailureType type, T failure) =>
        new DLOptionFailureException<T>(Fail(type, failure));

    public static Option<T, IDLOptionFailure> CatchAll<T>(Func<T> func) =>
        Option.CatchAll<T, IDLOptionFailure>(() => func(), x => Fail(DLFailureType.Exception, x));

    public static Option<T, IDLOptionFailure> CatchAll<T>(Func<Option<T, IDLOptionFailure>> func) =>
        Option.CatchAll(() => func(), x => Fail(DLFailureType.Exception, x));
}

public class DLOptionFailure<T> : IDLOptionFailure
{
    //public DLFailureType Type { get; }
    public T Failure { get; }
    //public IDLOptionFailure[] InnerFailures { get; } = [];
    public override string Message => ToString();

    public DLOptionFailure(T failure)
    {
        FailureType = failure is Exception ? DLFailureType.Exception : DLFailureType.Unspecified;
        Failure = failure;
    }

    public DLOptionFailure(T failure, SourceLocation sourceLocation)
        : this(failure)
    {
        SourceLocation = sourceLocation;
    }

    public DLOptionFailure(DLFailureType type, T failure)
    {
        FailureType = type;
        Failure = failure;
    }

    public DLOptionFailure(DLFailureType type, T failure, SourceLocation sourceLocation)
        : this(type, failure)
    {
        SourceLocation = sourceLocation;
    }

    public DLOptionFailure(T failure, IEnumerable<IDLOptionFailure> innerFailure)
    {
        FailureType = failure is Exception ? DLFailureType.Exception : DLFailureType.Aggregation;
        Failure = failure;
        InnerFailures = [.. innerFailure];
    }

    public DLOptionFailure(DLFailureType type, T failure, IEnumerable<IDLOptionFailure> innerFailure)
    {
        FailureType = type;
        Failure = failure;
        InnerFailures = [.. innerFailure];
    }

    public override string ToString()
    {
        if (InnerFailures.Length == 0)
            return $"[{FailureType}] {Failure?.ToString()}";

        var innerFailureOutput = InnerFailures.ToJoinedString("\n");

        if (Failure is string sFailure && string.IsNullOrEmpty(sFailure))
            return $"{innerFailureOutput}";
        else
            return $"{innerFailureOutput}\n  {Failure?.ToString()}";
    }

    public static implicit operator T(DLOptionFailure<T> optionFailure) =>
        optionFailure.Failure;

    public static implicit operator DLOptionFailure<T>(T failure) =>
        DLOptionFailure.Fail(failure);
}
