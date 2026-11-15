using System;
using DataLinq.Extensions.Hosting;
using DataLinq.Interfaces;
using Microsoft.Extensions.DependencyInjection;
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
        AddHostedValidator(services);
        if (configure is not null) services.AddSingleton(new SchemaValidationDefaults(configure));
        return new(services);
    }

    /// <summary>Selects this canonical registration for startup validation. A second explicit target configuration is rejected.</summary>
    public static DataLinqRegistrationBuilder<TDatabase> ValidateSchemaOnStartup<TDatabase>(this DataLinqRegistrationBuilder<TDatabase> builder,
        Action<DataLinqSchemaStartupOptions>? configure = null) where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddHostedValidator(builder.Services);
        builder.Services.AddSingleton(new SchemaValidationSelection(typeof(TDatabase), builder.ServiceKey, builder.Registration, configure));
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

    private static void AddHostedValidator(IServiceCollection services) =>
        services.AddHostedService<SchemaValidationHostedService>(static provider => new(provider,
            provider.GetService<ILogger<SchemaValidationHostedService>>() ?? NullLogger<SchemaValidationHostedService>.Instance));
}
