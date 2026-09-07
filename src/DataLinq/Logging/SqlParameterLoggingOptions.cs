using System;
using System.Data;

namespace DataLinq.Logging;

/// <summary>Controls disclosure of parameter values independently from the SQL debug log level.</summary>
public sealed class SqlParameterLoggingOptions
{
    internal static SqlParameterLoggingOptions Default { get; } = new();
    private int maximumValueLength = 256;

    /// <summary>Opt in to bounded parameter values. The default logs types and lengths only.</summary>
    public bool IncludeSensitiveValues { get; init; }

    /// <summary>Maximum formatted value characters, excluding quotes/prefix and the truncation marker.</summary>
    public int MaximumValueLength
    {
        get => maximumValueLength;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            maximumValueLength = value;
        }
    }

    /// <summary>Additional redaction when values are enabled. Returning true always hides the value.</summary>
    public Func<IDbDataParameter, bool>? RedactParameter { get; init; }
}
