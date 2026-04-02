using DataLinq;

namespace DataLinq.Testing;

public sealed record TestProviderDescriptor(
    string Name,
    TestProviderKind Kind,
    DatabaseType DatabaseType,
    bool RequiresExternalServer,
    bool UsesPodman)
{
    public bool IsSQLite => DatabaseType == DatabaseType.SQLite;
    public bool IsServerDatabase => RequiresExternalServer;
}
