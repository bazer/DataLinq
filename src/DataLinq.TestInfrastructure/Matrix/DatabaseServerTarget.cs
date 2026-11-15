using System;
using DataLinq;

namespace DataLinq.TestInfrastructure;

public sealed record DatabaseServerTarget(
    string Id,
    string DisplayName,
    DatabaseServerFamily Family,
    string Version,
    string Image,
    int HostPort,
    bool IsLts,
    bool IsDefault)
{
    public DatabaseType DatabaseType => Family switch
    {
        DatabaseServerFamily.MySql => DatabaseType.MySQL,
        DatabaseServerFamily.MariaDb => DatabaseType.MariaDB,
        _ => throw new InvalidOperationException($"Unsupported family '{Family}'.")
    };
}
