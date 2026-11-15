using System;
using DataLinq.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Extensions.Hosting;

/// <summary>Selects explicit existing DataLinq registrations for startup validation.</summary>
public sealed class DataLinqSchemaValidationBuilder
{
    internal DataLinqSchemaValidationBuilder(IServiceCollection services) => Services = services;

    /// <summary>Gets the application service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Selects an unnamed/default or explicit keyed registration. Use named configure for an unkeyed override.</summary>
    public DataLinqSchemaValidationBuilder ValidateDatabase<TDatabase>(object? serviceKey = null,
        Action<DataLinqSchemaStartupOptions>? configure = null) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        if (ReferenceEquals(serviceKey, KeyedService.AnyKey))
            throw new ArgumentException("Startup validation requires a concrete key, not AnyKey.", nameof(serviceKey));
        Services.AddSingleton(new SchemaValidationSelection(typeof(TDatabase), serviceKey, null, configure));
        return this;
    }
}
