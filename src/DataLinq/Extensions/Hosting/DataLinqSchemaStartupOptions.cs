using System;
using System.Linq;
using DataLinq.Validation;

namespace DataLinq.Extensions.Hosting;

/// <summary>Determines how a completed comparison affects startup. Operational failures always prevent enabled startup.</summary>
public enum DataLinqSchemaStartupMode
{
    /// <summary>Prevent startup when the completed comparison fails policy.</summary>
    FailStartup,
    /// <summary>Allow policy-failing completed comparisons, preserving their original results.</summary>
    WarningOnly,
    /// <summary>Skip validation and activation. Global disablement overrides target settings.</summary>
    Disabled
}

/// <summary>Configures host defaults or one selected registration. Callbacks run once per startup snapshot.</summary>
public sealed class DataLinqSchemaStartupOptions
{
    /// <summary>Gets or sets startup mode. Defaults to FailStartup.</summary>
    public DataLinqSchemaStartupMode Mode { get; set; } = DataLinqSchemaStartupMode.FailStartup;
    /// <summary>Gets runtime validation settings. Targets start with a copy of the configured host defaults.</summary>
    public DataLinqSchemaValidationOptions Validation { get; } = new();

    internal DataLinqSchemaStartupOptions Copy()
    {
        var copy = new DataLinqSchemaStartupOptions { Mode = Mode };
        copy.Validation.FailOnSeverity = Validation.FailOnSeverity;
        copy.Validation.TreatValidationIssuesAsFailures = Validation.TreatValidationIssuesAsFailures;
        copy.Validation.CommandTimeout = Validation.CommandTimeout;
        copy.Validation.Include = Validation.Include?.ToArray();
        copy.Validation.MetadataReaderLog = Validation.MetadataReaderLog;
        return copy;
    }

    internal CapturedSchemaValidationOptions Capture()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "A defined startup validation mode is required.");
        return CapturedSchemaValidationOptions.Capture(Validation);
    }
}
