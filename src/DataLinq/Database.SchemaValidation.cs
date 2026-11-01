using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Validation;

namespace DataLinq;

public abstract partial class Database<T> where T : class, IDatabaseModel<T>
{
    /// <summary>Compares this database's finalized model with a fresh live schema. Configuration runs synchronously once.</summary>
    public DataLinqSchemaValidationResult ValidateSchema(Action<DataLinqSchemaValidationOptions>? configure = null) =>
        SchemaValidationInvocation.Capture(Provider, CapturedSchemaValidationOptions.Configure(configure)).Execute();

    /// <summary>Throws a result-based exception when a completed comparison fails the configured policy.</summary>
    public void EnsureSchemaValid(Action<DataLinqSchemaValidationOptions>? configure = null) => EnsureValid(ValidateSchema(configure));

    /// <summary>Compares this database's model asynchronously, capturing configuration before returning.</summary>
    public Task<DataLinqSchemaValidationResult> ValidateSchemaAsync(Action<DataLinqSchemaValidationOptions>? configure = null,
        CancellationToken cancellationToken = default) =>
        SchemaValidationInvocation.Capture(Provider, CapturedSchemaValidationOptions.Configure(configure)).ExecuteAsync(cancellationToken);

    /// <summary>Asynchronously compares the schema and throws when the completed result fails policy.</summary>
    public Task EnsureSchemaValidAsync(Action<DataLinqSchemaValidationOptions>? configure = null,
        CancellationToken cancellationToken = default) => EnsureValidAsync(ValidateSchemaAsync(configure, cancellationToken));

    private static void EnsureValid(DataLinqSchemaValidationResult result)
    {
        if (result.HasFailures) throw new DataLinqSchemaValidationException(result);
    }

    private static async Task EnsureValidAsync(Task<DataLinqSchemaValidationResult> validation) =>
        EnsureValid(await validation.ConfigureAwait(false));
}
