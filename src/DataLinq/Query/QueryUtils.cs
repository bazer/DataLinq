using System;

namespace DataLinq.Query;

public static class QueryUtils
{
    public static (string name, string alias) ParseTableNameAndAlias(string nameAndAlias)
    {
        return nameAndAlias.IndexOf(' ') switch
        {
            -1 => (nameAndAlias, null),
            var i when i > -1 => (nameAndAlias.Substring(0, i), nameAndAlias.Substring(i + 1)),
            _ => throw new NotImplementedException()
        };
    }

    public static (string name, string alias) ParseColumnNameAndAlias(string nameAndAlias)
    {
        return nameAndAlias.IndexOf('.') switch
        {
            -1 => (nameAndAlias, null),
            var i when i > -1 => (nameAndAlias.Substring(i + 1), nameAndAlias.Substring(0, i)),
            _ => throw new NotImplementedException()
        };
    }
}
