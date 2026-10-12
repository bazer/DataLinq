using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Extensions.DependencyInjection;

internal sealed class DataLinqRegistration<TDatabase>(IServiceCollection services, object? key)
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    private readonly object activationKey = new();
    private readonly List<ServiceDescriptor> aliases = [];
    private Func<IServiceProvider, Database<TDatabase>>? factory;
    private Database<TDatabase>? instance;
    private ReadOnlyAccess<TDatabase>? borrowedAccess;
    private TDatabase? borrowedModel;
    private Type? implementationType;
    private DataLinqOwnership ownership = DataLinqOwnership.Container;
    private bool frozen;
    internal IServiceCollection Services { get; } = services;
    internal object? Key { get; } = key;
    internal bool IsDefault { get; private set; } = key is null;

    internal void Register()
    {
        EnsureAvailable(AliasTypes(), Key);
        Services.AddKeyedSingleton<DataLinqActivation<TDatabase>>(activationKey, (provider, _) => CreateActivation(provider));
        InstallAliases();
    }

    internal void EnsureMutable()
    {
        if (frozen) throw new InvalidOperationException("This DataLinq registration was already resolved; its construction recipe is frozen.");
    }

    internal void SelectDefault()
    {
        EnsureMutable();
        if (IsDefault) return;
        if (Services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ImplementationInstance is DataLinqRegistration<TDatabase> other && other != this && other.IsDefault))
            throw new InvalidOperationException($"Model '{typeof(TDatabase).FullName}' already has an explicit default registration.");
        EnsureAvailable(AliasTypes(), null);
        IsDefault = true;
        InstallAliases();
    }

    internal void SetFactory(Type implementation, Func<IServiceProvider, Database<TDatabase>> create)
    {
        EnsureSourceAvailable(implementation);
        RejectDisposableOwnedModel();
        implementationType = implementation;
        factory = create;
        InstallAliases();
    }

    internal void SetInstance(Type implementation, Database<TDatabase> supplied, DataLinqOwnership requestedOwnership)
    {
        EnsureSourceAvailable(implementation);
        EnsureProviderOwnership(supplied, requestedOwnership);
        ReadOnlyAccess<TDatabase>? access = null;
        TDatabase? model = null;
        if (requestedOwnership == DataLinqOwnership.Container)
        {
            RejectDisposableOwnedModel();
            supplied.ValidateContainerOwnership();
        }
        else
        {
            // These objects already exist. Instance descriptors are essential:
            // a factory returning even a non-disposable interface can be captured.
            access = supplied.Provider.ReadOnlyAccess;
            model = access.Query();
        }
        implementationType = implementation;
        instance = supplied;
        ownership = requestedOwnership;
        borrowedAccess = access;
        borrowedModel = model;
        InstallAliases();
    }

    private void EnsureSourceAvailable(Type implementation)
    {
        EnsureMutable();
        if (factory is not null || instance is not null)
            throw new InvalidOperationException("A DataLinq registration can select only one provider, factory, or existing instance.");
        if (implementation != typeof(Database<TDatabase>))
            foreach (var aliasKey in AliasKeys()) EnsureAvailable([implementation], aliasKey);
    }

    private static void RejectDisposableOwnedModel()
    {
        if (typeof(IDisposable).IsAssignableFrom(typeof(TDatabase)) || typeof(IAsyncDisposable).IsAssignableFrom(typeof(TDatabase)))
            throw new NotSupportedException("Container-owned registrations cannot expose a generated read model that implements disposal. Use an application-owned instance for disposable read models.");
    }

    private void EnsureProviderOwnership(Database<TDatabase> database, DataLinqOwnership requestedOwnership)
    {
        if (Services.Any(descriptor => !descriptor.IsKeyedService &&
            descriptor.ImplementationInstance is DataLinqRegistration<TDatabase> other && other != this &&
            other.instance is { } otherDatabase && ReferenceEquals(otherDatabase.Provider, database.Provider) &&
            other.ownership != requestedOwnership))
            throw new InvalidOperationException("The same provider cannot be both application-owned and container-owned in DataLinq registrations.");
    }

    private Type[] AliasTypes() => new[] { typeof(Database<TDatabase>), typeof(ReadOnlyAccess<TDatabase>), typeof(TDatabase),
        typeof(IDataLinqUnitOfWorkFactory<TDatabase>), typeof(DataLinqUnitOfWorkFactory<TDatabase>), implementationType }
        .Where(type => type is not null).Cast<Type>().Distinct().ToArray();

    private IEnumerable<object?> AliasKeys()
    {
        yield return Key;
        if (Key is not null && IsDefault) yield return null;
    }

    private void EnsureAvailable(IEnumerable<Type> types, object? aliasKey)
    {
        foreach (var type in types)
            if (Services.Any(descriptor => descriptor.ServiceType == type &&
                Equals(descriptor.IsKeyedService ? descriptor.ServiceKey : null, aliasKey) && !aliases.Contains(descriptor)))
                throw new InvalidOperationException($"Service '{type.FullName}' already has a registration for the requested key.");
    }

    private void InstallAliases()
    {
        foreach (var descriptor in aliases) Services.Remove(descriptor);
        aliases.Clear();
        foreach (var aliasKey in AliasKeys())
        foreach (var type in AliasTypes())
        {
            var borrowed = BorrowedAlias(type);
            var descriptor = borrowed is not null
                ? aliasKey is null ? ServiceDescriptor.Singleton(type, borrowed) : ServiceDescriptor.KeyedSingleton(type, aliasKey, borrowed)
                : aliasKey is null ? ServiceDescriptor.Singleton(type, provider => ResolveAlias(provider, type))
                    : ServiceDescriptor.KeyedSingleton(type, aliasKey, (provider, _) => ResolveAlias(provider, type));
            Services.Add(descriptor);
            aliases.Add(descriptor);
        }
    }

    private object? BorrowedAlias(Type type)
    {
        if (ownership != DataLinqOwnership.Application) return null;
        if (type == typeof(TDatabase)) return borrowedModel;
        if (type == typeof(ReadOnlyAccess<TDatabase>)) return borrowedAccess;
        return type == typeof(Database<TDatabase>) || type == implementationType ? instance : null;
    }

    private object ResolveAlias(IServiceProvider provider, Type type)
    {
        var activation = provider.GetRequiredKeyedService<DataLinqActivation<TDatabase>>(activationKey).Get();
        if (type == typeof(TDatabase)) return activation.Model;
        if (type == typeof(ReadOnlyAccess<TDatabase>)) return activation.Access;
        if (type == typeof(IDataLinqUnitOfWorkFactory<TDatabase>) || type == typeof(DataLinqUnitOfWorkFactory<TDatabase>)) return activation.Factory;
        return activation.Database;
    }

    private DataLinqActivation<TDatabase> CreateActivation(IServiceProvider provider)
    {
        frozen = true;
        var create = factory;
        var existing = instance;
        var owns = ownership == DataLinqOwnership.Container;
        return new(() =>
        {
            var database = existing ?? create?.Invoke(provider) ?? throw new InvalidOperationException("This DataLinq registration has no provider or its factory returned null.");
            // Reject a factory that returns a known borrowed provider before taking
            // any ownership. Disposing that conflicting result would harm its caller.
            EnsureProviderOwnership(database, ownership);
            try
            {
                var access = borrowedAccess ?? database.Provider.ReadOnlyAccess;
                var model = borrowedModel ?? access.Query();
                if (owns && (access is IDisposable or IAsyncDisposable || model is IDisposable or IAsyncDisposable))
                    throw new NotSupportedException("Container-owned read aliases cannot implement disposal. Use an application-owned instance for custom disposable read objects.");
                var result = new ActivatedDataLinq<TDatabase>(database, access, model, new(database));
                if (owns) database.EnableContainerOwnership();
                return result;
            }
            catch (Exception failure) when (existing is null)
            {
                // Factory results are owned even when validation fails. Supplied
                // instances retain application ownership until successful handoff.
                using var diagnostics = ExecutionFailureScope.Begin();
                var failures = new ExecutionFailures();
                failures.AddReported(failure, ExecutionFailureStage.Validation);
                try { ((IDisposable)database).Dispose(); }
                catch (Exception cleanup) { failures.AddCleanup(cleanup); }
                ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                    ExecutionRecoveryActions.None, null, ExecutionOperationKind.Dispose, database.Provider.TelemetryInstanceId));
                failures.ThrowIfAny();
                throw;
            }
        }, owns);
    }
}

internal sealed record ActivatedDataLinq<TDatabase>(Database<TDatabase> Database, ReadOnlyAccess<TDatabase> Access,
    TDatabase Model, DataLinqUnitOfWorkFactory<TDatabase> Factory) where TDatabase : class, IDatabaseModel<TDatabase>;

internal sealed class DataLinqActivation<TDatabase>(Func<ActivatedDataLinq<TDatabase>> create, bool owns) : IDisposable, IAsyncDisposable
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    private readonly object sync = new();
    private readonly Lazy<ActivatedDataLinq<TDatabase>> value = new(create);
    private bool disposed;

    internal ActivatedDataLinq<TDatabase> Get()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return value.Value;
        }
    }

    private Database<TDatabase>? Close()
    {
        // Serialize construction and handoff with shutdown. Merely tracking this
        // holder must never force activation or take ownership of a failed instance.
        lock (sync)
        {
            if (disposed) return null;
            disposed = true;
            return owns && value.IsValueCreated ? value.Value.Database : null;
        }
    }

    public void Dispose() => Close()?.Dispose();
    public ValueTask DisposeAsync() => Close()?.DisposeAsync() ?? default;
}
