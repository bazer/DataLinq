using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Extensions.Hosting;

namespace DataLinq.Exceptions;

/// <summary>Startup validation rejected serving, retaining ordered completed outcomes and any run-level failure.</summary>
public sealed class DataLinqSchemaStartupException : Exception
{
    internal DataLinqSchemaStartupException(IReadOnlyList<DataLinqSchemaStartupOutcome> outcomes, Exception? runFailure = null)
        : base(MessageFor(outcomes, runFailure), CauseFor(outcomes, runFailure))
    {
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
        RunFailure = runFailure;
    }

    /// <summary>Gets settled target outcomes, including successful comparisons, in canonical selection order.</summary>
    public IReadOnlyList<DataLinqSchemaStartupOutcome> Outcomes { get; }
    /// <summary>Gets an orchestration, configuration or callback/logger failure that stopped scheduling, if any.</summary>
    public Exception? RunFailure { get; }

    private static string MessageFor(IReadOnlyList<DataLinqSchemaStartupOutcome> outcomes, Exception? failure)
    {
        var failed = outcomes.Where(outcome => outcome.PreventsStartup).Select(outcome =>
            $"{outcome.Target.DisplayName}: {outcome.OperationalFailure?.Message ?? "schema comparison failed policy"}");
        var detail = string.Join("; ", failed);
        if (failure is not null) detail += (detail.Length == 0 ? "" : "; ") + failure.Message;
        return "DataLinq schema validation prevented startup. " + detail;
    }

    private static Exception? CauseFor(IReadOnlyList<DataLinqSchemaStartupOutcome> outcomes, Exception? failure)
    {
        var errors = outcomes.Where(outcome => outcome.OperationalFailure is not null).Select(outcome => outcome.OperationalFailure!).ToList();
        if (failure is not null) errors.Add(failure);
        return errors.Count switch { 0 => null, 1 => errors[0], _ => new AggregateException(errors) };
    }
}
