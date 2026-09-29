using System;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.Query;
using ThrowAway;

internal sealed class LegacyMetadataFactory : IMetadataFromSqlFactory
{
    public Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName,
        string csNamespace, string dbName, string connectionString) => throw new Exception("sync metadata fallback");
}

internal sealed class LegacyProvisioningFactory : ISqlFromMetadataFactory
{
    public Option<Sql, IDLOptionFailure> GetCreateTables(DatabaseDefinition metadata, bool foreignKeyRestrict) =>
        throw new Exception("sync SQL generation");
    public Option<int, IDLOptionFailure> CreateDatabase(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict) =>
        throw new Exception("sync provisioning fallback");
}
