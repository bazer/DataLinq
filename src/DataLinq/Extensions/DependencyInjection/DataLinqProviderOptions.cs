using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Extensions.DependencyInjection;

/// <summary>Common construction settings for a provider registration. Each activation receives its own options.</summary>
public abstract class DataLinqProviderOptions
{
    private string? connectionString;
    private string? connectionStringName;
    private string? boundRecoveryTimeout;
    private DataLinqExecutionOptions execution = new();

    /// <summary>Gets or selects a literal connection string. Setting this replaces any named selection.</summary>
    public string? ConnectionString
    {
        get => connectionString;
        set { connectionString = value; connectionStringName = null; }
    }

    /// <summary>Gets or selects a name in the application's ConnectionStrings section. Setting this replaces any literal selection.</summary>
    public string? ConnectionStringName
    {
        get => connectionStringName;
        set { connectionStringName = value; connectionString = null; }
    }

    /// <summary>Gets or sets the optional database-name override passed to the selected provider.</summary>
    public string? DatabaseName { get; set; }

    /// <summary>Gets or replaces the execution settings captured before provider construction.</summary>
    public DataLinqExecutionOptions Execution
    {
        get
        {
            if (boundRecoveryTimeout is null) return execution;
            if (!TimeSpan.TryParse(boundRecoveryTimeout, CultureInfo.InvariantCulture, out var timeout))
                throw new InvalidOperationException("DataLinq Execution:RecoveryRollbackTimeout must be a valid invariant TimeSpan.");
            return new() { RecoveryRollbackTimeout = timeout };
        }
        set { execution = value; boundRecoveryTimeout = null; }
    }

    internal void Bind(IConfiguration configuration)
    {
        // Preserve both bound alternatives until code has a chance to replace
        // the selection as a whole. Binding never selects a provider or a key.
        if (configuration[nameof(ConnectionString)] is { } literal) connectionString = literal;
        if (configuration[nameof(ConnectionStringName)] is { } name) connectionStringName = name;
        if (configuration[nameof(DatabaseName)] is { } databaseName) DatabaseName = databaseName;
        if (configuration["Execution:RecoveryRollbackTimeout"] is { } timeout) boundRecoveryTimeout = timeout;
    }

    internal DataLinqProviderSettings Capture(IServiceProvider provider, Action<string> validateConnectionString)
    {
        var literal = !string.IsNullOrWhiteSpace(connectionString);
        var named = !string.IsNullOrWhiteSpace(connectionStringName);
        if (literal == named)
            throw new InvalidOperationException(literal
                ? "DataLinq configuration is ambiguous: select either ConnectionString or ConnectionStringName."
                : "DataLinq requires a non-empty ConnectionString or ConnectionStringName.");
        var resolved = connectionString;
        if (named)
        {
            var configuration = provider.GetService<IConfiguration>()
                ?? throw new InvalidOperationException("A named DataLinq connection string requires an IConfiguration service.");
            resolved = configuration.GetConnectionString(connectionStringName!);
            if (string.IsNullOrWhiteSpace(resolved))
                throw new InvalidOperationException("The selected DataLinq connection string is missing or empty in ConnectionStrings.");
        }
        try { validateConnectionString(resolved!); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            // Driver parse errors can include connection-string content. Keep
            // neither that content nor the original exception in diagnostics.
            throw new InvalidOperationException("The selected DataLinq connection string is invalid for this provider.");
        }
        return new(resolved!, DatabaseName, DataLinqExecutionOptions.Capture(Execution));
    }
}

internal sealed record DataLinqProviderSettings(string ConnectionString, string? DatabaseName, DataLinqExecutionOptions Execution);
