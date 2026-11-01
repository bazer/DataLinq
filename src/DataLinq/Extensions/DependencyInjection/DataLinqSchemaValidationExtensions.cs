using System;
using System.Linq;
using DataLinq.Extensions.Hosting;
using DataLinq.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataLinq.Extensions.DependencyInjection;

/// <summary>Installs one startup validation barrier using existing canonical DataLinq registrations.</summary>
public static class DataLinqSchemaValidationExtensions
{
    /// <summary>Configures host defaults and selects targets centrally. Enabled validation requires explicit selections.</summary>
    public static DataLinqSchemaValidationBuilder AddDataLinqSchemaValidation(this IServiceCollection services,
        Action<DataLinqSchemaStartupOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registration = GetOrAdd(services);
        if (configure is not null) registration.Defaults.Add(configure);
        return new(services, registration);
    }

    /// <summary>Selects this canonical registration for startup validation. A second explicit target configuration is rejected.</summary>
    public static DataLinqRegistrationBuilder<TDatabase> ValidateSchemaOnStartup<TDatabase>(this DataLinqRegistrationBuilder<TDatabase> builder,
        Action<DataLinqSchemaStartupOptions>? configure = null) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(builder);
        GetOrAdd(builder.Services).Selections.Add(new(typeof(TDatabase), builder.ServiceKey, builder.Registration, configure));
        return builder;
    }

    /// <summary>Selects this provider registration while preserving the provider-specific fluent builder.</summary>
    public static DataLinqProviderRegistrationBuilder<TDatabase, TOptions> ValidateSchemaOnStartup<TDatabase, TOptions>(
        this DataLinqProviderRegistrationBuilder<TDatabase, TOptions> builder, Action<DataLinqSchemaStartupOptions>? configure = null)
        where TDatabase : class, IDatabaseModel<TDatabase> where TOptions : DataLinqProviderOptions
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Registration.ValidateSchemaOnStartup(configure);
        return builder;
    }

    private static SchemaValidationRegistration GetOrAdd(IServiceCollection services)
    {
        var existing = services.Where(descriptor => !descriptor.IsKeyedService).Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SchemaValidationRegistration>().SingleOrDefault();
        if (existing is not null) return existing;
        var registration = new SchemaValidationRegistration();
        services.AddSingleton(registration);
        services.AddSingleton<IHostedService>(provider => new SchemaValidationHostedService(provider, registration,
            provider.GetService<ILogger<SchemaValidationHostedService>>() ?? NullLogger<SchemaValidationHostedService>.Instance));
        return registration;
    }
}
