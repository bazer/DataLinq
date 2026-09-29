using DataLinq.Execution;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.ErrorHandling;
using ThrowAway;
using DataLinq.Query;

namespace DataLinq.SQLite;

public partial class SqlFromSQLiteFactory : IAsyncSqlProvisioningFactory
{
    /// <summary>Creates a SQLite database asynchronously. Partial effects are possible and are not automatically undone.</summary>
    public virtual Task<Option<int, IDLOptionFailure>> CreateDatabaseAsync(Sql sql, string databaseName, string connectionString,
        bool foreignKeyRestrict, CancellationToken cancellationToken = default) =>
        this.CreateDatabaseAsyncCore(sql, databaseName, connectionString, foreignKeyRestrict, cancellationToken);

    IAsyncProvisioningPlan IAsyncSqlProvisioningFactory.CaptureProvisioning(ProvisioningRequest request)
    {
        var connectionString = SQLiteConnectionStringFactory.NormalizeConnectionString(request.ConnectionString, request.DatabaseName);
        var sql = CapturedSql.Capture(new Sql(request.Script));
        return new ProvisioningPlan(connectionString, sql);
    }

    private sealed class ProvisioningPlan(string connectionString, CapturedSql sql) : IAsyncProvisioningPlan
    {
        public void Validate() => new SQLiteDbAccess.NativeCommandFactory(sql).Validate(AsyncCommandKind.NonQuery);
        public IAsyncProvisioningSession CreateSession() => SQLiteAdministrativeSession.CreateOwned(connectionString, sql,
            ExecutionOperationKind.Provisioning, provisioning: true);
    }
}
