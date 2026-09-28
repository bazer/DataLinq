using System;
using System.Collections.Generic;
using System.Data;
using DataLinq.Interfaces;
using DataLinq.Mutation;

internal sealed class LegacyDataSource(IDatabaseProvider provider) : DataSourceAccess(provider)
{
    public override IDatabaseAccess DatabaseAccess => throw new Exception("sync access");
    public override IEnumerable<T> GetFromQuery<T>(string query) => throw new Exception("sync query");
    public override IEnumerable<T> GetFromCommand<T>(IDbCommand dbCommand) => throw new Exception("sync command");
}
