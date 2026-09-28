using System;
using DataLinq.ErrorHandling;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using ThrowAway;

namespace DataLinq;

public abstract partial class DatabaseProvider<T> where T : class, IDatabaseModel<T>
{
    /// <summary>Constructs a provider with a separate, validated capture of its execution settings.</summary>
    protected DatabaseProvider(string connectionString, DatabaseType databaseType,
        DataLinqLoggingConfiguration loggingConfiguration, string? databaseName,
        DataLinqExecutionOptions executionOptions)
        : base(connectionString, typeof(T), databaseType, loggingConfiguration, databaseName,
            MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<T>, false, BindGeneratedMetadata, executionOptions)
    {
        ReadOnlyAccess = new ReadOnlyAccess<T>(this);
    }
}

public abstract partial class DatabaseProvider
{
    /// <summary>Constructs a provider with a separate, validated capture of its execution settings.</summary>
    protected DatabaseProvider(string connectionString, Type type, DatabaseType databaseType,
        DataLinqLoggingConfiguration loggingConfiguration, string? databaseName,
        Func<Option<DatabaseDefinition, IDLOptionFailure>>? metadataFactory, bool createReadOnlyAccess,
        DataLinqExecutionOptions executionOptions)
        : this(connectionString, type, databaseType, loggingConfiguration, databaseName,
            metadataFactory, createReadOnlyAccess, null, executionOptions)
    {
    }
}
