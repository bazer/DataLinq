using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataLinq.Extensions.DependencyInjection;

internal sealed class DataLinqRegistration<TDatabase>(IServiceCollection services, object? key)
    : IDataLinqRegistration
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    private readonly object activationKey = new();
    private readonly List<ServiceDescriptor> aliases = [];
    private readonly List<IDataLinqServiceRegistration> applicationServices = [];

    // Each service collection binds the logical registration key to its own construction recipe.
    private sealed class ActivationRecipe
    {
        internal Type? ImplementationType { get; init; }
        internal Func<IServiceProvider, Database<TDatabase>>? Factory { get; init; }
        internal object? ProviderBuilder { get; init; }
        internal Database<TDatabase>? Instance { get; init; }
        internal DataLinqOwnership Ownership { get; init; } = DataLinqOwnership.Container;
        internal ReadOnlyAccess<TDatabase>? BorrowedAccess { get; init; }
        internal TDatabase? BorrowedModel { get; init; }
        internal DataLinqUnitOfWorkFactory<TDatabase>? BorrowedFactory { get; init; }
        internal bool Frozen { get; set; }
    }

    private ActivationRecipe recipe = new();
    private ServiceDescriptor? defaultAlias;
    internal IServiceCollection Services { get; } = services;
    internal object? Key { get; } = key;
    internal bool IsDefault => IsDefaultIn(Services);
    internal bool IsDefaultIn(IServiceCollection collection) => Key is null || (defaultAlias is not null && collection.Contains(defaultAlias));
    Type IDataLinqRegistration.ModelType => typeof(TDatabase);
    object? IDataLinqRegistration.Key => Key;
    bool IDataLinqRegistration.IsDefaultFor(IServiceProvider provider) =>
        Key is null || ReferenceEquals(this, provider.GetKeyedService<IDataLinqRegistration>(typeof(TDatabase)));
    IDatabaseProvider IDataLinqRegistration.ResolveProvider(IServiceProvider provider) =>
        provider.GetRequiredKeyedService<DataLinqActivation<TDatabase>>(activationKey).Get().Database.Provider;

    internal void Register()
    {
        EnsureAvailable(AliasTypes(), Key);
        InstallRecipe(recipe);
    }

    private void InstallRecipe(ActivationRecipe next)
    {
        Services.Replace(ServiceDescriptor.KeyedSingleton(activationKey, next));
        Services.Replace(ServiceDescriptor.KeyedSingleton<DataLinqActivation<TDatabase>>(activationKey,
            (provider, _) => CreateActivation(provider, next)));
        recipe = next;
        InstallAliases();
    }

    internal void EnsureMutable()
    {
        if (recipe.Frozen) throw new InvalidOperationException("This DataLinq registration was already resolved; its construction recipe is frozen.");
    }

    internal void EnsureCurrentProvider(object builder)
    {
        if (!ReferenceEquals(recipe.ProviderBuilder, builder))
            throw new InvalidOperationException("This provider builder was displaced by a replacement registration.");
    }

    internal void SelectDefault()
    {
        EnsureMutable();
        if (IsDefault) return;
        if (Services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ImplementationInstance is DataLinqRegistration<TDatabase> other && other != this && other.IsDefaultIn(Services)))
            throw new InvalidOperationException($"Model '{typeof(TDatabase).FullName}' already has an explicit default registration.");
        EnsureAvailable(AliasTypes(), null);
        foreach (var service in applicationServices) service.Validate(asDefault: true);
        // The default selection follows the descriptors copied to each host.
        defaultAlias = ServiceDescriptor.KeyedSingleton<IDataLinqRegistration>(typeof(TDatabase), this);
        Services.Add(defaultAlias);
        InstallAliases();
        foreach (var service in applicationServices) service.Install();
    }

    internal void AddService<TService>(ServiceLifetime lifetime)
        where TService : class, IDataLinqService<TDatabase>, IDataLinqServiceFactory<TDatabase, TService>
    {
        EnsureMutable();
        var service = new DataLinqServiceRegistration<TDatabase, TService>(this, lifetime);
        service.Validate(IsDefault);
        applicationServices.Add(service);
        service.Install();
    }

    internal IDataLinqUnitOfWorkFactory<TDatabase> ResolveFactory(IServiceProvider provider) =>
        provider.GetRequiredKeyedService<DataLinqActivation<TDatabase>>(activationKey).Get().Factory;

    internal void SetFactory(Type implementation, Func<IServiceProvider, Database<TDatabase>> create, object? providerBuilder = null)
    {
        EnsureSourceAvailable(implementation);
        RejectDisposableOwnedModel();
        InstallRecipe(new() { ImplementationType = implementation, Factory = create, ProviderBuilder = providerBuilder });
    }

    internal void SetInstance(Type implementation, Database<TDatabase> supplied, DataLinqOwnership requestedOwnership)
    {
        EnsureSourceAvailable(implementation);
        InstallInstance(implementation, supplied, requestedOwnership, null);
    }

    internal void ReplaceWithBorrowedInstance(Database<TDatabase> supplied, DataLinqUnitOfWorkFactory<TDatabase> suppliedFactory)
    {
        // A sibling may already have resolved its copy. Only the owner's new recipe will change.
        var implementation = supplied.GetType();
        foreach (var aliasKey in AliasKeys()) EnsureAvailable([implementation], aliasKey);
        InstallInstance(implementation, supplied, DataLinqOwnership.Application, suppliedFactory);
    }

    private void InstallInstance(Type implementation, Database<TDatabase> supplied, DataLinqOwnership requestedOwnership,
        DataLinqUnitOfWorkFactory<TDatabase>? suppliedFactory)
    {
        var otherRecipes = Services
            .Where(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(ActivationRecipe)
                && !ReferenceEquals(descriptor.ServiceKey, activationKey))
            .Select(descriptor => (ActivationRecipe)descriptor.KeyedImplementationInstance!);
        EnsureProviderOwnership(supplied, requestedOwnership, otherRecipes);
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
        InstallRecipe(new()
        {
            ImplementationType = implementation, Instance = supplied, Ownership = requestedOwnership,
            BorrowedAccess = access, BorrowedModel = model, BorrowedFactory = suppliedFactory
        });
    }

    private void EnsureSourceAvailable(Type implementation)
    {
        EnsureMutable();
        if (recipe.Factory is not null || recipe.Instance is not null)
            throw new InvalidOperationException("A DataLinq registration can select only one provider, factory, or existing instance.");
        if (implementation != typeof(Database<TDatabase>))
            foreach (var aliasKey in AliasKeys()) EnsureAvailable([implementation], aliasKey);
    }

    private static void RejectDisposableOwnedModel()
    {
        if (typeof(IDisposable).IsAssignableFrom(typeof(TDatabase)) || typeof(IAsyncDisposable).IsAssignableFrom(typeof(TDatabase)))
            throw new NotSupportedException("Container-owned registrations cannot expose a generated read model that implements disposal. Use an application-owned instance for disposable read models.");
    }

    private static void EnsureProviderOwnership(Database<TDatabase> database, DataLinqOwnership requestedOwnership,
        IEnumerable<ActivationRecipe> recipes)
    {
        if (recipes.Any(other => other.Instance is { } otherDatabase && ReferenceEquals(otherDatabase.Provider, database.Provider)
            && other.Ownership != requestedOwnership))
            throw new InvalidOperationException("The same provider cannot be both application-owned and container-owned in DataLinq registrations.");
    }

    private Type[] AliasTypes() => new[] { typeof(Database<TDatabase>), typeof(ReadOnlyAccess<TDatabase>), typeof(TDatabase),
        typeof(IDataLinqUnitOfWorkFactory<TDatabase>), typeof(DataLinqUnitOfWorkFactory<TDatabase>), recipe.ImplementationType }
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
        if (recipe.Ownership != DataLinqOwnership.Application) return null;
        if (type == typeof(TDatabase)) return recipe.BorrowedModel;
        if (type == typeof(ReadOnlyAccess<TDatabase>)) return recipe.BorrowedAccess;
        return type == typeof(Database<TDatabase>) || type == recipe.ImplementationType ? recipe.Instance : null;
    }

    private object ResolveAlias(IServiceProvider provider, Type type)
    {
        var activation = provider.GetRequiredKeyedService<DataLinqActivation<TDatabase>>(activationKey).Get();
        if (type == typeof(TDatabase)) return activation.Model;
        if (type == typeof(ReadOnlyAccess<TDatabase>)) return activation.Access;
        if (type == typeof(IDataLinqUnitOfWorkFactory<TDatabase>) || type == typeof(DataLinqUnitOfWorkFactory<TDatabase>)) return activation.Factory;
        return activation.Database;
    }

    private static DataLinqActivation<TDatabase> CreateActivation(IServiceProvider provider, ActivationRecipe recipe)
    {
        recipe.Frozen = true;
        var create = recipe.Factory;
        var existing = recipe.Instance;
        var owns = recipe.Ownership == DataLinqOwnership.Container;
        return new(() =>
        {
            Database<TDatabase> database;
            try { database = existing ?? create?.Invoke(provider) ?? throw new InvalidOperationException("This DataLinq registration has no provider or its factory returned null."); }
            catch (Exception failure)
            {
                if (recipe.ProviderBuilder is null || create is null) DataLinqActivationFailureScope.ReportConfiguration(failure);
                throw;
            }
            // Reject a factory that returns a known borrowed provider before taking
            // any ownership. Disposing that conflicting result would harm its caller.
            try
            {
                // DI 8.0.1 returns no recipes for AnyKey. Resolve each concrete key in this host.
                var recipes = provider.GetServices<DataLinqRegistration<TDatabase>>()
                    .Select(registration => provider.GetRequiredKeyedService<ActivationRecipe>(registration.activationKey));
                EnsureProviderOwnership(database, recipe.Ownership, recipes);
            }
            catch (Exception failure) { DataLinqActivationFailureScope.ReportConfiguration(failure); throw; }
            try
            {
                var access = recipe.BorrowedAccess ?? database.Provider.ReadOnlyAccess;
                var model = recipe.BorrowedModel ?? access.Query();
                if (owns && (access is IDisposable or IAsyncDisposable || model is IDisposable or IAsyncDisposable))
                    throw new NotSupportedException("Container-owned read aliases cannot implement disposal. Use an application-owned instance for custom disposable read objects.");
                var result = new ActivatedDataLinq<TDatabase>(database, access, model, recipe.BorrowedFactory ?? new(database));
                if (owns) database.EnableContainerOwnership();
                return result;
            }
            catch (Exception failure)
            {
                DataLinqActivationFailureScope.ReportConfiguration(failure);
                if (existing is not null) throw;
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
