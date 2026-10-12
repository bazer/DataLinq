using System;
using System.Linq;
using DataLinq.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Extensions.DependencyInjection;

/// <summary>Registers lazy DataLinq infrastructure in the existing runtime package.</summary>
public static class DataLinqServiceCollectionExtensions
{
    /// <summary>Starts one registration identified by a generated model and an optional service key.</summary>
    /// <remarks>A keyed registration has no unnamed alias until AsDefault is explicitly selected.</remarks>
    public static DataLinqRegistrationBuilder<TDatabase> AddDataLinq<TDatabase>(this IServiceCollection services, object? serviceKey = null)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(services);
        if (ReferenceEquals(serviceKey, KeyedService.AnyKey))
            throw new ArgumentException("A DataLinq registration requires a concrete service key, not AnyKey.", nameof(serviceKey));
        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ImplementationInstance is DataLinqRegistration<TDatabase> registration &&
            (Equals(registration.Key, serviceKey) || serviceKey is null && registration.IsDefault)))
            throw new InvalidOperationException($"A DataLinq registration or default already exists for model '{typeof(TDatabase).FullName}' and the requested key.");
        var recipe = new DataLinqRegistration<TDatabase>(services, serviceKey);
        recipe.Register();
        services.AddSingleton(recipe);
        return new(recipe);
    }

    /// <summary>Configures one unnamed lazy registration.</summary>
    public static IServiceCollection AddDataLinq<TDatabase>(this IServiceCollection services, Action<DataLinqRegistrationBuilder<TDatabase>> configure)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(services.AddDataLinq<TDatabase>());
        return services;
    }

    /// <summary>Configures one keyed lazy registration.</summary>
    public static IServiceCollection AddDataLinq<TDatabase>(this IServiceCollection services, object serviceKey, Action<DataLinqRegistrationBuilder<TDatabase>> configure)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(services.AddDataLinq<TDatabase>(serviceKey));
        return services;
    }
}
