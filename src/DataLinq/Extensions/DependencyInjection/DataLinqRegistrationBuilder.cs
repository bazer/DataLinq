using System;
using DataLinq.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Extensions.DependencyInjection;

/// <summary>Specifies who disposes an existing database supplied to registration.</summary>
public enum DataLinqOwnership
{
    /// <summary>The application retains ownership; no alias asks the container to dispose the supplied objects.</summary>
    Application,
    /// <summary>Ownership transfers only after successful first activation.</summary>
    Container
}

/// <summary>Configures one model/key identity. Registration and container construction do not construct a provider.</summary>
public sealed class DataLinqRegistrationBuilder<TDatabase> where TDatabase : class, IDatabaseModel<TDatabase>
{
    internal DataLinqRegistration<TDatabase> Registration { get; }
    internal DataLinqRegistrationBuilder(DataLinqRegistration<TDatabase> registration) => Registration = registration;

    /// <summary>Gets the collection to which this registration belongs.</summary>
    public IServiceCollection Services => Registration.Services;
    /// <summary>Gets the registration key; null selects the unnamed identity.</summary>
    public object? ServiceKey => Registration.Key;

    /// <summary>Exposes this keyed registration through unnamed injection without creating another database.</summary>
    public DataLinqRegistrationBuilder<TDatabase> AsDefault()
    {
        Registration.SelectDefault();
        return this;
    }

    /// <summary>Supplies a lazy, always container-owned database factory.</summary>
    /// <remarks>The declared factory result type is exposed as a concrete alias. Results must use the Database&lt;T&gt; disposal implementations; owned read aliases cannot implement disposal.</remarks>
    public DataLinqRegistrationBuilder<TDatabase> UseFactory<TImplementation>(Func<IServiceProvider, TImplementation> factory)
        where TImplementation : Database<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(factory);
        Registration.SetFactory(typeof(TImplementation), provider => factory(provider));
        return this;
    }

    /// <summary>Registers an existing database, borrowed by default. Explicit container ownership transfers on successful first activation.</summary>
    /// <remarks>Borrowed aliases use instance descriptors, including disposable custom read roots. No settings are applied to an existing instance.</remarks>
    public DataLinqRegistrationBuilder<TDatabase> UseInstance<TImplementation>(TImplementation instance,
        DataLinqOwnership ownership = DataLinqOwnership.Application) where TImplementation : Database<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));
        Registration.SetInstance(instance.GetType(), instance, ownership);
        return this;
    }
}
