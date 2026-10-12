using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Extensions.DependencyInjection;

internal interface IDataLinqServiceRegistration
{
    void Validate(bool asDefault);
    void Install();
}

internal sealed class DataLinqServiceRegistration<TDatabase, TService>(DataLinqRegistration<TDatabase> registration, ServiceLifetime lifetime)
    : IDataLinqServiceRegistration
    where TDatabase : class, IDatabaseModel<TDatabase>
    where TService : class, IDataLinqService<TDatabase>, IDataLinqServiceFactory<TDatabase, TService>
{
    private readonly List<ServiceDescriptor> descriptors = [];

    public void Validate(bool asDefault)
    {
        if (registration.Key is not null && asDefault && lifetime != ServiceLifetime.Transient &&
            (typeof(IDisposable).IsAssignableFrom(typeof(TService)) || typeof(IAsyncDisposable).IsAssignableFrom(typeof(TService))))
            throw new NotSupportedException("Disposable singleton/scoped DataLinq application services cannot use both a key and a default alias. Use one identity or independent transient activations.");
        EnsureAvailable(registration.Key);
        if (registration.Key is not null && asDefault) EnsureAvailable(null);
    }

    private void EnsureAvailable(object? key)
    {
        if (registration.Services.Any(descriptor => descriptor.ServiceType == typeof(TService) &&
            Equals(descriptor.IsKeyedService ? descriptor.ServiceKey : null, key) && !descriptors.Contains(descriptor)))
            throw new InvalidOperationException($"Service '{typeof(TService).FullName}' already has a registration for the requested key.");
    }

    public void Install()
    {
        foreach (var descriptor in descriptors) registration.Services.Remove(descriptor);
        descriptors.Clear();
        object Create(IServiceProvider provider) => TService.Create(provider, registration.ResolveFactory(provider));
        var key = registration.Key;
        if (key is null) Add(ServiceDescriptor.Describe(typeof(TService), Create, lifetime));
        else
        {
            Add(ServiceDescriptor.DescribeKeyed(typeof(TService), key, (provider, _) => Create(provider), lifetime));
            if (registration.IsDefault)
            {
                // Singleton/scoped aliases are proven non-disposable. Transients
                // construct directly so DI captures each disposable activation once.
                Func<IServiceProvider, object> resolve = lifetime == ServiceLifetime.Transient ? Create
                    : provider => provider.GetRequiredKeyedService<TService>(key);
                Add(ServiceDescriptor.Describe(typeof(TService), resolve, lifetime));
            }
        }
    }

    private void Add(ServiceDescriptor descriptor)
    {
        registration.Services.Add(descriptor);
        descriptors.Add(descriptor);
    }
}
