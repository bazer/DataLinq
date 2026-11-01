using System;
using System.Globalization;
using DataLinq.Validation;

namespace DataLinq.Extensions.Hosting;

/// <summary>Identifies a canonical model/key registration without retaining its provider or services.</summary>
public sealed class DataLinqSchemaValidationTarget
{
    internal DataLinqSchemaValidationTarget(Type modelType, object? serviceKey, int ordinal)
    {
        ModelType = modelType;
        ServiceKey = serviceKey;
        Ordinal = ordinal;
        var key = serviceKey switch
        {
            null => "default",
            string text => text,
            Enum value => value.ToString(),
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or bool or char =>
                Convert.ToString(serviceKey, CultureInfo.InvariantCulture)!,
            _ => $"{serviceKey.GetType().FullName} (target {ordinal})"
        };
        DisplayName = $"Target {ordinal}: {modelType.FullName} [{key}]";
    }

    /// <summary>Gets the generated database model type.</summary>
    public Type ModelType { get; }
    /// <summary>Gets the canonical registration key, not the alias used to select it.</summary>
    public object? ServiceKey { get; }
    /// <summary>Gets the one-based canonical first-selection order, including disabled targets.</summary>
    public int Ordinal { get; }
    /// <summary>Gets a diagnostic label that never calls arbitrary key formatting code.</summary>
    public string DisplayName { get; }
}

/// <summary>A settled target comparison or operational failure. No result is fabricated for a failed read.</summary>
public sealed class DataLinqSchemaStartupOutcome
{
    internal DataLinqSchemaStartupOutcome(DataLinqSchemaValidationTarget target, DataLinqSchemaStartupMode mode,
        DataLinqSchemaValidationResult? result, Exception? operationalFailure)
    {
        if ((result is null) == (operationalFailure is null)) throw new ArgumentException("Exactly one result or operational failure is required.");
        Target = target;
        Mode = mode;
        Result = result;
        OperationalFailure = operationalFailure;
    }

    /// <summary>Gets canonical target identity.</summary>
    public DataLinqSchemaValidationTarget Target { get; }
    /// <summary>Gets captured target mode.</summary>
    public DataLinqSchemaStartupMode Mode { get; }
    /// <summary>Gets the complete comparison, or null when acquisition failed.</summary>
    public DataLinqSchemaValidationResult? Result { get; }
    /// <summary>Gets the original operational error, or null after a completed comparison.</summary>
    public Exception? OperationalFailure { get; }
    /// <summary>Gets whether this outcome prevents startup under its captured mode.</summary>
    public bool PreventsStartup => OperationalFailure is not null || Mode == DataLinqSchemaStartupMode.FailStartup && Result!.HasFailures;
}
