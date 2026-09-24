using DataLinq.Execution;
using DataLinq.Query;

namespace DataLinq.SQLite;

public partial class SqlFromSQLiteFactory : IAsyncSqlProvisioningFactory
{
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
