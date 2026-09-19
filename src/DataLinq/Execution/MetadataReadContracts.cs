using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;

namespace DataLinq.Execution;

internal enum MetadataReadPurpose { Import, RuntimeValidation }

internal sealed class MetadataReadSettings
{
    private MetadataReadSettings(MetadataReadPurpose purpose, MetadataFromDatabaseFactoryOptions options,
        TimeSpan? commandTimeout)
    {
        Purpose = purpose;
        CapitaliseNames = options.CapitaliseNames;
        DeclareEnumsInClass = options.DeclareEnumsInClass;
        Include = Array.AsReadOnly(options.Include?.ToArray() ?? []);
        Log = options.Log;
        CommandTimeoutSeconds = NormalizeTimeout(commandTimeout);
    }

    internal MetadataReadPurpose Purpose { get; }
    internal bool CapitaliseNames { get; }
    internal bool DeclareEnumsInClass { get; }
    internal IReadOnlyList<string> Include { get; }
    internal Action<string>? Log { get; }
    internal int? CommandTimeoutSeconds { get; }

    internal static MetadataReadSettings Import(MetadataFromDatabaseFactoryOptions options) =>
        new(MetadataReadPurpose.Import, options, null);

    // Runtime Include is comparison scope (AAPI-108), never an import filter.
    // It therefore cannot be supplied to this live-reader contract.
    internal static MetadataReadSettings Runtime(TimeSpan? commandTimeout, Action<string>? log) =>
        new(MetadataReadPurpose.RuntimeValidation, new() { Log = log }, commandTimeout);

    internal static int? NormalizeTimeout(TimeSpan? timeout)
    {
        if (timeout is null) return null;
        const long maximumSeconds = 2_147_483;
        var ticks = timeout.Value.Ticks;
        if (ticks < 0 || ticks > maximumSeconds * TimeSpan.TicksPerSecond)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Metadata command timeout must be between zero and 2,147,483 seconds.");
        return checked((int)((ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond));
    }
}

internal sealed record MetadataImportRequest(string Name, string CsTypeName, string CsNamespace,
    string DatabaseName, string ConnectionString, MetadataReadSettings Settings)
{
    public override string ToString() => "Captured metadata import request";
}

internal interface IAsyncMetadataFactory
{
    // Read once before capture; mutable Include is copied by the coordinator.
    MetadataFromDatabaseFactoryOptions Options { get; }
    IAsyncMetadataReadPlan CaptureImport(MetadataImportRequest request);
}

internal interface IAsyncProviderMetadataSource
{
    // Bind to this provider's effective identity and existing keeper ownership.
    // No provider reconstruction, application transaction, creation or setup I/O.
    IAsyncMetadataReadPlan CaptureValidationMetadata(MetadataReadSettings settings);
}

internal interface IAsyncMetadataReadPlan
{
    // I/O-free validation includes the requested timeout and actual resource /
    // command capabilities. No unsupported setting may be silently ignored.
    void Validate();
    // I/O-free construction transfers owned, unopened resources only on success.
    // A failing implementation must clean partial construction it cannot hand off.
    IAsyncMetadataSession CreateSession();
    // Use the context for every reader/scalar command. Local parsing remains
    // synchronous; do not turn cancellation into an Option through CatchAll.
    Task<Option<DatabaseDefinition, IDLOptionFailure>> ReadAsync(MetadataReadContext context, CancellationToken token);
}

internal interface IAsyncMetadataSession : IAsyncDisposable
{
    IAsyncDatabaseAccess Access { get; }
    IAsyncMetadataCommands Commands { get; }
    // Open only owned read resources; missing/unreadable is not an empty schema.
    Task OpenAsync(CancellationToken token);
}

internal interface IAsyncMetadataCommands
{
    void Validate(int? commandTimeoutSeconds);
    // I/O-free invocation binding. The common layer applies an explicit timeout
    // to every created command; null retains the provider's command default.
    IAsyncOwnedCommandFactory Capture(CapturedSql query);
}
