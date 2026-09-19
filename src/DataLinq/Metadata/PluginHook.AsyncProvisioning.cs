using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Query;
using ThrowAway;

namespace DataLinq.Metadata;

public static partial class PluginHook
{
    internal static Task<Option<int, IDLOptionFailure>> CreateDatabaseFromSqlAsyncCore(
        this DatabaseType type, Sql sql, string databaseOrFile, string connectionString,
        bool foreignKeyRestrict, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(databaseOrFile);
        ArgumentNullException.ThrowIfNull(connectionString);
        if (!TryGetRegistration(type, out var registration))
            return Task.FromResult<Option<int, IDLOptionFailure>>(new DLOptionFailure<string>($"No creator for {type}"));
        return registration.SqlFromMetadataFactory.CreateDatabaseAsyncCore(sql, databaseOrFile, connectionString, foreignKeyRestrict, token);
    }

    internal static Task<Option<int, IDLOptionFailure>> CreateDatabaseFromMetadataAsyncCore(
        this DatabaseType type, DatabaseDefinition metadata, string databaseNameOrFile, string connectionString,
        bool foreignKeyRestrict, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(databaseNameOrFile);
        ArgumentNullException.ThrowIfNull(connectionString);
        if (!TryGetRegistration(type, out var registration))
            return Task.FromResult<Option<int, IDLOptionFailure>>(new DLOptionFailure<string>($"No creator for {type}"));
        // SQL generation is synchronous local work and retains its Option contract.
        // Never re-read the registry after invoking a user-supplied generator.
        var factory = registration.SqlFromMetadataFactory;
        var sql = factory.GetCreateTables(metadata, foreignKeyRestrict);
        if (sql.HasFailed) return Task.FromResult<Option<int, IDLOptionFailure>>(sql.Failure);
        return factory.CreateDatabaseAsyncCore(sql.Value, databaseNameOrFile, connectionString, foreignKeyRestrict, token);
    }
}
