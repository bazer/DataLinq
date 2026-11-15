using DataLinq;

namespace DataLinq.TestInfrastructure;

public sealed record TestProviderDescriptor(
    string Name,
    TestProviderKind Kind,
    DatabaseType DatabaseType,
    bool RequiresExternalServer,
    bool UsesPodman,
    DatabaseServerTarget? ServerTarget)
{
    public bool IsSQLite => DatabaseType == DatabaseType.SQLite;
    public bool IsServerDatabase => RequiresExternalServer;
}
