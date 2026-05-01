namespace DataLinq.Validation;

public sealed class SchemaComparisonOptions
{
    public SchemaComparisonOptions(DatabaseType databaseType)
        : this(SchemaValidationCapabilities.For(databaseType))
    {
    }

    public SchemaComparisonOptions(SchemaValidationCapabilities capabilities)
    {
        Capabilities = capabilities;
    }

    public SchemaValidationCapabilities Capabilities { get; }

    public static SchemaComparisonOptions For(DatabaseType databaseType) => new(databaseType);
}
