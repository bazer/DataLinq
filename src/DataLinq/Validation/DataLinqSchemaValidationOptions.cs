using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Execution;

namespace DataLinq.Validation;

/// <summary>Configures a comparison of finalized model metadata with a fresh live schema.</summary>
public sealed class DataLinqSchemaValidationOptions
{
    /// <summary>Gets or sets the lowest difference severity that fails validation. Defaults to Error.</summary>
    public SchemaDifferenceSeverity FailOnSeverity { get; set; } = SchemaDifferenceSeverity.Error;

    /// <summary>Gets or sets whether typed metadata issues independently fail validation. Defaults to true.</summary>
    public bool TreatValidationIssuesAsFailures { get; set; } = true;

    /// <summary>Gets or sets the timeout for each metadata command, not connection opening or the entire validation.</summary>
    /// <remarks>Null preserves the native default; zero is unlimited. Positive values round up to seconds.
    /// Negative values and values above 2,147,483 seconds are rejected before metadata I/O.</remarks>
    public TimeSpan? CommandTimeout { get; set; }

    /// <summary>Gets or sets finalized-model database table/view names to compare. Null or empty compares all objects.</summary>
    /// <remarks>This is comparison scope, not an import filter. Server names require exact model spelling;
    /// SQLite selection ignores ASCII letter case only. Selection does not reduce metadata I/O.</remarks>
    public IReadOnlyList<string>? Include { get; set; }

    /// <summary>Gets or sets an optional synchronous metadata diagnostic callback. Callback failures propagate.</summary>
    public Action<string>? MetadataReaderLog { get; set; }
}

// Capture before provider activation/suspension. Model-name resolution is a
// separate step because it requires the selected provider's finalized metadata.
internal sealed class CapturedSchemaValidationOptions
{
    private CapturedSchemaValidationOptions(DataLinqSchemaValidationOptions options)
    {
        FailOnSeverity = options.FailOnSeverity;
        TreatValidationIssuesAsFailures = options.TreatValidationIssuesAsFailures;
        CommandTimeout = options.CommandTimeout;
        MetadataReaderLog = options.MetadataReaderLog;
        Include = Array.AsReadOnly(options.Include?.ToArray() ?? []);

        if (!Enum.IsDefined(FailOnSeverity))
            throw new ArgumentOutOfRangeException(nameof(options.FailOnSeverity), FailOnSeverity, "A defined schema difference severity is required.");
        CommandTimeoutSeconds = MetadataReadSettings.NormalizeTimeout(CommandTimeout);
        if (Include.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Include entries must be nonblank database table or view names.", nameof(options.Include));
    }

    internal SchemaDifferenceSeverity FailOnSeverity { get; }
    internal bool TreatValidationIssuesAsFailures { get; }
    internal TimeSpan? CommandTimeout { get; }
    internal int? CommandTimeoutSeconds { get; }
    internal IReadOnlyList<string> Include { get; }
    internal Action<string>? MetadataReaderLog { get; }

    internal static CapturedSchemaValidationOptions Capture(DataLinqSchemaValidationOptions? options) => new(options ?? new());

    internal static CapturedSchemaValidationOptions Configure(Action<DataLinqSchemaValidationOptions>? configure)
    {
        var options = new DataLinqSchemaValidationOptions();
        configure?.Invoke(options);
        return Capture(options);
    }
}
