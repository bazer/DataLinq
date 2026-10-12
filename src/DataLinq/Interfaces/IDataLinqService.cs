using System;

namespace DataLinq.Interfaces;

/// <summary>Marks a partial application service whose operations participate in units of work for this model.</summary>
public interface IDataLinqService<TDatabase> where TDatabase : class, IDatabaseModel<TDatabase>;

/// <summary>Generated construction bridge that binds a service's factory to one explicit database registration.</summary>
/// <remarks>Other constructor dependencies come from the supplied service provider. No current session is retained.</remarks>
public interface IDataLinqServiceFactory<TDatabase, TService>
    where TDatabase : class, IDatabaseModel<TDatabase>
    where TService : class, IDataLinqService<TDatabase>
{
    /// <summary>Constructs a service with the selected factory and its ordinary container dependencies.</summary>
    static abstract TService Create(IServiceProvider services, IDataLinqUnitOfWorkFactory<TDatabase> factory);
}
