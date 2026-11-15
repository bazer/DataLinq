using System;
using System.Linq;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Testing;

/// <summary>Connects application services to an existing SQLite test fixture.</summary>
public static class SqliteTestServiceCollectionExtensions
{
    /// <summary>Replaces the model's default registration or the specified key before building the host.</summary>
    /// <remarks>
    /// Call this on the collection that received <c>AddDataLinq</c>. The test retains ownership of the fixture.
    /// Generated services and startup validation follow the selected registration.
    /// </remarks>
    public static IServiceCollection ReplaceDataLinqWithSqliteInMemory<TDatabase>(this IServiceCollection services,
        SqliteTestDatabase<TDatabase> fixture, object? serviceKey = null)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fixture);
        var registration = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(DataLinqRegistration<TDatabase>))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DataLinqRegistration<TDatabase>>()
            .SingleOrDefault(candidate => serviceKey is null ? candidate.IsDefault : Equals(candidate.Key, serviceKey));
        if (registration is null)
        {
            var target = serviceKey is null ? "a default registration" : $"a registration for key '{serviceKey}'";
            throw new InvalidOperationException($"Model '{typeof(TDatabase).FullName}' requires {target} for SQLite fixture replacement.");
        }

        if (!ReferenceEquals(registration.Services, services))
            throw new InvalidOperationException($"Replace the registration for '{typeof(TDatabase).FullName}' on its original service collection. Copied descriptors share that registration.");

        // Generated services and attached startup validation resolve the recipe from their host.
        registration.ReplaceWithBorrowedInstance(fixture.Database, fixture.Factory);
        return services;
    }
}
