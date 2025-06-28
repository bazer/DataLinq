using DataLinq.Metadata;

namespace DataLinq.MySql;

public class SqlFromMariaDBFactory : SqlFromMetadataFactory
{
    protected override string? ParseCsType(string csType)
    {
        if (csType.ToLower() == "guid")
            return "uuid";

        return base.ParseCsType(csType);
    }
}