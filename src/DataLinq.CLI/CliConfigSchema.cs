using System;
using System.IO;
using System.Reflection;

namespace DataLinq.CLI;

internal static class CliConfigSchema
{
    public const string SchemaUrl = "https://datalinq.org/schemas/datalinq.schema.json";
    private const string ResourceName = "DataLinq.CLI.Schemas.datalinq.schema.json";

    public static string ReadSchemaJson()
    {
        var assembly = typeof(CliConfigSchema).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded config schema resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
