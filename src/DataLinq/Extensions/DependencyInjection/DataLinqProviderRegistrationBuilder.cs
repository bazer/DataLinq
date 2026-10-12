using System;
using System.Collections.Generic;
using DataLinq.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataLinq.Extensions.DependencyInjection;

/// <summary>Configures the selected provider. Bindings run before ordered code callbacks at first resolution.</summary>
public sealed class DataLinqProviderRegistrationBuilder<TDatabase, TOptions>
    where TDatabase : class, IDatabaseModel<TDatabase>
    where TOptions : DataLinqProviderOptions
{
    private readonly List<IConfiguration> bindings = [];
    private readonly List<Action<TOptions>> configurations = [];

    private DataLinqProviderRegistrationBuilder(DataLinqRegistrationBuilder<TDatabase> registration) => Registration = registration;

    /// <summary>Gets the common registration builder for service wiring.</summary>
    public DataLinqRegistrationBuilder<TDatabase> Registration { get; }
    /// <summary>Gets the service collection.</summary>
    public IServiceCollection Services => Registration.Services;
    /// <summary>Gets this registration's key.</summary>
    public object? ServiceKey => Registration.ServiceKey;

    /// <summary>Exposes this keyed registration through unnamed injection.</summary>
    public DataLinqProviderRegistrationBuilder<TDatabase, TOptions> AsDefault()
    {
        Registration.AsDefault();
        return this;
    }

    /// <summary>Reads supported provider and execution settings from configuration at first resolution.</summary>
    /// <remarks>Multiple bindings run in registration order, followed by all Configure callbacks. No reload subscription is created.</remarks>
    public DataLinqProviderRegistrationBuilder<TDatabase, TOptions> Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Registration.Registration.EnsureMutable();
        bindings.Add(configuration);
        return this;
    }

    /// <summary>Adds a code override, applied after bindings in callback registration order.</summary>
    public DataLinqProviderRegistrationBuilder<TDatabase, TOptions> Configure(Action<TOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Registration.Registration.EnsureMutable();
        configurations.Add(configure);
        return this;
    }

    internal static DataLinqProviderRegistrationBuilder<TDatabase, TOptions> Create<TImplementation>(
        DataLinqRegistrationBuilder<TDatabase> registration, Func<TOptions> defaults,
        Action<string> validateConnectionString, Func<DataLinqProviderSettings, ILoggerFactory?, TImplementation> create)
        where TImplementation : Database<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(registration);
        var builder = new DataLinqProviderRegistrationBuilder<TDatabase, TOptions>(registration);
        registration.UseFactory(provider =>
        {
            var options = defaults();
            foreach (var binding in builder.bindings) options.Bind(binding);
            foreach (var configure in builder.configurations) configure(options);
            var settings = options.Capture(provider, validateConnectionString);
            return create(settings, provider.GetService<ILoggerFactory>());
        });
        return builder;
    }
}
