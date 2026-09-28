using DataLinq.Execution;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.ErrorHandling;
using ThrowAway;
using DataLinq.Query;
using MySqlConnector;

namespace DataLinq.MySql;

public abstract partial class SqlFromMetadataFactory : IAsyncSqlProvisioningFactory
{
    /// <summary>Creates a database from captured SQL asynchronously. Partial effects are possible and are not automatically undone.</summary>
    public virtual Task<Option<int, IDLOptionFailure>> CreateDatabaseAsync(Sql sql, string databaseName, string connectionString,
        bool foreignKeyRestrict, CancellationToken cancellationToken = default) =>
        this.CreateDatabaseAsyncCore(sql, databaseName, connectionString, foreignKeyRestrict, cancellationToken);

    IAsyncProvisioningPlan IAsyncSqlProvisioningFactory.CaptureProvisioning(ProvisioningRequest request)
    {
        // Keep synchronous creation semantics, including the configured initial
        // database and the text-only script. Generation has already captured FK policy.
        var connectionString = new MySqlConnectionStringBuilder(request.ConnectionString).ConnectionString;
        var database = SqlIdentifier.Quote(request.DatabaseName, "`");
        var sql = CapturedSql.Capture(new Sql($"CREATE DATABASE IF NOT EXISTS {database};\nUSE {database};\n{request.Script}"));
        return new NativeProvisioningPlan(connectionString, sql);
    }

    private sealed class NativeProvisioningPlan(string connectionString, CapturedSql sql) : IAsyncProvisioningPlan
    {
        public void Validate() => new SqlDbAccess.NativeCommandFactory(sql).Validate(AsyncCommandKind.NonQuery);
        public IAsyncProvisioningSession CreateSession() => SqlAdministrativeSession.CreateOwned(connectionString, sql);
    }
}
