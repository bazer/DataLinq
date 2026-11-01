using System;
using System.Threading;
using DataLinq.Interfaces;

namespace DataLinq.Extensions.DependencyInjection;

// Canonical registration identity is independent of aliases and provider objects.
internal interface IDataLinqRegistration
{
    Type ModelType { get; }
    object? Key { get; }
    bool IsDefault { get; }
    IDatabaseProvider ResolveProvider(IServiceProvider services);
}

// Observe configuration/application callback boundaries without changing W4's
// exception behavior or classifying arbitrary exception types as configuration.
internal sealed class DataLinqActivationFailureScope : IDisposable
{
    private static readonly AsyncLocal<DataLinqActivationFailureScope?> current = new();
    private readonly DataLinqActivationFailureScope? parent;
    private DataLinqActivationFailureScope() { parent = current.Value; current.Value = this; }
    internal Exception? ConfigurationFailure { get; private set; }
    internal static DataLinqActivationFailureScope Begin() => new();
    internal static void ReportConfiguration(Exception failure)
    {
        for (var scope = current.Value; scope is not null; scope = scope.parent)
            scope.ConfigurationFailure ??= failure;
    }
    public void Dispose() => current.Value = parent;
}
