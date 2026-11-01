using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Validation;

/// <summary>Compares finalized provider metadata with a fresh, observational read of its effective database.</summary>
public static class DataLinqSchemaValidator
{
    /// <summary>Reads and compares the live schema synchronously. Operational failures throw without producing a result.</summary>
    public static DataLinqSchemaValidationResult Validate(IDatabaseProvider provider, DataLinqSchemaValidationOptions? options = null) =>
        SchemaValidationInvocation.Capture(provider, CapturedSchemaValidationOptions.Capture(options)).Execute();

    /// <summary>Reads and compares the live schema through the provider's asynchronous metadata capability.</summary>
    /// <remarks>Options and Include are captured before this method returns. Cancellation settles owned read resources before propagating.</remarks>
    public static Task<DataLinqSchemaValidationResult> ValidateAsync(IDatabaseProvider provider,
        DataLinqSchemaValidationOptions? options = null, CancellationToken cancellationToken = default) =>
        SchemaValidationInvocation.Capture(provider, CapturedSchemaValidationOptions.Capture(options)).ExecuteAsync(cancellationToken);
}

// One invocation carries callback provenance by the boundary that invoked it,
// not by guessing from an exception type. Hosting reuses this same execution path.
internal sealed class SchemaValidationInvocation
{
    private readonly IDatabaseProvider provider;
    private readonly CapturedSchemaValidationOptions options;
    private readonly SchemaComparisonScope scope;
    private readonly MetadataReadSettings settings;
    private readonly string databaseName;
    private readonly DatabaseType databaseType;

    private SchemaValidationInvocation(IDatabaseProvider provider, CapturedSchemaValidationOptions options)
    {
        this.provider = provider;
        this.options = options;
        databaseName = provider.DatabaseName;
        databaseType = provider.DatabaseType;
        scope = SchemaComparisonScope.Capture(provider.Metadata, databaseType, options.Include);
        settings = MetadataReadSettings.Runtime(options.CommandTimeout, options.MetadataReaderLog is { } log ? message =>
        {
            try { log(message); }
            catch (Exception failure) { CallbackFailure = failure; throw; }
        } : null);
    }

    internal Exception? CallbackFailure { get; private set; }

    internal static SchemaValidationInvocation Capture(IDatabaseProvider provider, CapturedSchemaValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        return new(provider, options);
    }

    internal DataLinqSchemaValidationResult Execute() => Complete(provider.ReadValidationMetadataCore(settings));

    internal Task<DataLinqSchemaValidationResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        // Capability/plan validation is synchronous, as is argument capture.
        var read = provider.ReadValidationMetadataAsyncCore(settings, cancellationToken);
        return CompleteAsync(read, cancellationToken);
    }

    private async Task<DataLinqSchemaValidationResult> CompleteAsync(Task<Option<DatabaseDefinition, IDLOptionFailure>> read,
        CancellationToken cancellationToken)
    {
        var acquired = await read.ConfigureAwait(false);
        // The metadata coordinator owns failure/cancellation precedence. A
        // later cancellation must not erase an already failed read Option.
        if (acquired.TryUnwrap(out _, out _)) cancellationToken.ThrowIfCancellationRequested();
        return Complete(acquired);
    }

    private DataLinqSchemaValidationResult Complete(Option<DatabaseDefinition, IDLOptionFailure> acquired)
    {
        if (!acquired.TryUnwrap(out var live, out var failure)) SchemaValidationReadFailure.Throw(failure);
        var identifiers = settings.Identifiers ?? throw new InvalidOperationException("Metadata acquisition did not supply identifier rules.");
        if (identifiers.DatabaseType != databaseType)
            throw new InvalidOperationException("Metadata acquisition supplied rules for a different database type.");
        var selected = scope.Resolve(live, identifiers);
        var evidence = settings.Limitations!.Capture(identifiers,
            selected.Model.Concat(selected.Database).Select(table => table.Table.DbName));
        var differences = new SchemaComparer(identifiers, evidence).CompareRuntime(selected.Model, selected.Database);
        return new(databaseName, databaseType, selected.Model.Count, selected.Database.Count, differences, evidence.Issues, options);
    }
}
