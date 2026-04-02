using System;
using DataLinq;

namespace DataLinq.Testing;

public sealed record DatabaseServerTarget(
    string Id,
    string DisplayName,
    DatabaseServerFamily Family,
    string Version,
    string Image,
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
