using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DataLinq.CLI;

namespace DataLinq.Tests.Unit;

public class DataLinqConfigSchemaTests
{
    [Test]
    public async Task SchemaJson_ParsesAndUsesPublicDatalinqUrl()
    {
        var schema = ReadSchema();

        await Assert.That((string?)schema["$id"]).IsEqualTo("https://datalinq.org/schemas/datalinq.schema.json");
        await Assert.That((string?)schema["$schema"]).IsEqualTo("https://json-schema.org/draft/2020-12/schema");
    }

    [Test]
    public async Task SchemaJson_MarksCompatibilityFieldsDeprecated()
    {
        var databaseProperties = ReadSchema()["$defs"]!["database"]!["properties"]!.AsObject();

        await Assert.That((bool?)databaseProperties["SourceDirectories"]!["deprecated"]).IsTrue();
        await Assert.That((bool?)databaseProperties["DestinationDirectory"]!["deprecated"]).IsTrue();
    }

    [Test]
    public async Task SchemaJson_ContainsCurrentPublicConfigFields()
    {
        var databaseProperties = ReadSchema()["$defs"]!["database"]!["properties"]!.AsObject();
        var connectionProperties = ReadSchema()["$defs"]!["connection"]!["properties"]!.AsObject();
        var layoutProperties = ReadSchema()["$defs"]!["modelLayout"]!["properties"]!.AsObject();

        await Assert.That(databaseProperties.Select(property => property.Key).ToArray()).IsEquivalentTo(
        [
            "Name",
            "CsType",
            "Namespace",
            "ModelDirectory",
            "DestinationDirectory",
            "SourceDirectories",
            "ModelLayout",
            "Include",
            "UseRecord",
            "UseFileScopedNamespaces",
            "UseNullableReferenceTypes",
            "CapitalizeNames",
            "RemoveInterfacePrefix",
            "SeparateTablesAndViews",
            "Connections",
            "FileEncoding"
        ]);
        await Assert.That(connectionProperties.Select(property => property.Key).ToArray()).IsEquivalentTo(
        [
            "Type",
            "DatabaseName",
            "DataSourceName",
            "ConnectionString"
        ]);
        await Assert.That(layoutProperties.Select(property => property.Key).ToArray()).IsEquivalentTo(
        [
            "PropertyOrder",
            "KeyPlacement",
            "RelationPlacement"
        ]);
    }

    [Test]
    public async Task SchemaJson_ValidatesRepresentativeConfigAndRejectsMisspelledFields()
    {
        var schema = ReadSchema();
        var validConfig = JsonNode.Parse(
            """
            {
              "$schema": "https://datalinq.org/schemas/datalinq.schema.json",
              "Databases": [
                {
                  "Name": "AppDb",
                  "CsType": "AppDb",
                  "Namespace": "MyApp.Models",
                  "ModelDirectory": "Models",
                  "ModelLayout": {
                    "PropertyOrder": "Alphabetical",
                    "KeyPlacement": "Inline",
                    "RelationPlacement": "WithForeignKey"
                  },
                  "UseNullableReferenceTypes": true,
                  "UseFileScopedNamespaces": true,
                  "Connections": [
                    {
                      "Type": "SQLite",
                      "DataSourceName": "app.db",
                      "ConnectionString": "Data Source=app.db;Cache=Shared;"
                    }
                  ]
                }
              ]
            }
            """)!;
        var typoConfig = JsonNode.Parse(
            """
            {
              "Databases": [
                {
                  "Name": "AppDb",
                  "ModelDirecotry": "Models"
                }
              ]
            }
            """)!;

        var validErrors = SchemaSmokeValidator.Validate(schema, validConfig);
        var typoErrors = SchemaSmokeValidator.Validate(schema, typoConfig);

        await Assert.That(validErrors).IsEmpty();
        await Assert.That(typoErrors.Any(error => error.Contains("ModelDirecotry", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task ConfigSchemaCommand_WritesSchemaToDefaultFileNextToSelectedConfig()
    {
        using var fixture = SchemaCommandFixture.Create();

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "schema",
            "--config",
            fixture.BasePath);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(fixture.OutputPath)).IsEqualTo(CliConfigSchema.ReadSchemaJson());
        await Assert.That(output).Contains($"Wrote DataLinq config schema to {fixture.OutputPath}");
        await Assert.That(output).Contains("Run again with --update-config");
    }

    [Test]
    [NotInParallel]
    public async Task ConfigSchemaCommand_WritesEmbeddedSchemaToOutputFile()
    {
        using var fixture = SchemaCommandFixture.Create();

        var customOutputPath = Path.Combine(fixture.BasePath, "schema", "custom.schema.json");

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "schema",
            "--output",
            customOutputPath);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(customOutputPath)).IsEqualTo(CliConfigSchema.ReadSchemaJson());
        await Assert.That(output).Contains($"Wrote DataLinq config schema to {customOutputPath}");
    }

    [Test]
    [NotInParallel]
    public async Task ConfigSchemaCommand_Stdout_PrintsSchemaWithoutWritingDefaultFile()
    {
        using var fixture = SchemaCommandFixture.Create();

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "schema",
            "--config",
            fixture.BasePath,
            "--stdout");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).IsEqualTo(CliConfigSchema.ReadSchemaJson());
        await Assert.That(File.Exists(fixture.OutputPath)).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task ConfigSchemaCommand_OutputDash_PrintsSchemaWithoutWritingDefaultFile()
    {
        using var fixture = SchemaCommandFixture.Create();

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "schema",
            "--config",
            fixture.BasePath,
            "--output",
            "-");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).IsEqualTo(CliConfigSchema.ReadSchemaJson());
        await Assert.That(File.Exists(fixture.OutputPath)).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task ConfigSchemaCommand_RejectsStdoutWithOutputFile()
    {
        using var fixture = SchemaCommandFixture.Create();

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "schema",
            "--stdout",
            "--output",
            fixture.OutputPath);

        await Assert.That(exitCode).IsEqualTo(2);
        await Assert.That(output).Contains("Use either --stdout or --output, not both.");
        await Assert.That(File.Exists(fixture.OutputPath)).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task ConfigSchemaCommand_UpdateConfig_AddsMissingLocalSchemaReferenceToExistingConfigFiles()
    {
        using var fixture = SchemaCommandFixture.Create();
        fixture.WriteConfig("datalinq.json", """{ "Databases": [] }""");
        fixture.WriteConfig("datalinq.user.json", """{ "Databases": [] }""");

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "schema",
            "--config",
            fixture.BasePath,
            "--update-config");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(fixture.OutputPath)).IsEqualTo(CliConfigSchema.ReadSchemaJson());
        await Assert.That(File.ReadAllText(fixture.ConfigPath)).Contains("\"$schema\": \"./datalinq.schema.json\"");
        await Assert.That(File.ReadAllText(fixture.UserConfigPath)).Contains("\"$schema\": \"./datalinq.schema.json\"");
        await Assert.That(output).Contains($"Updated '{fixture.ConfigPath}'");
        await Assert.That(output).Contains($"Updated '{fixture.UserConfigPath}'");
    }

    [Test]
    [NotInParallel]
    public async Task ConfigSchemaCommand_UpdateConfig_LeavesExistingSchemaReferenceUnchanged()
    {
        using var fixture = SchemaCommandFixture.Create();
        fixture.WriteConfig(
            "datalinq.json",
            """
            {
              "$schema": "https://datalinq.org/schemas/datalinq.schema.json",
              "Databases": []
            }
            """);

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "schema",
            "--config",
            fixture.BasePath,
            "--update-config");

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(fixture.ConfigPath)).Contains("\"$schema\": \"https://datalinq.org/schemas/datalinq.schema.json\"");
        await Assert.That(File.ReadAllText(fixture.ConfigPath)).DoesNotContain("\"$schema\": \"./datalinq.schema.json\"");
        await Assert.That(output).Contains("already has \"$schema\"");
        await Assert.That(output).Contains("JSON config files support one top-level $schema reference");
    }

    [Test]
    [NotInParallel]
    public async Task ConfigSchemaCommand_UpdateConfig_RejectsStdout()
    {
        using var fixture = SchemaCommandFixture.Create();
        fixture.WriteConfig("datalinq.json", """{ "Databases": [] }""");

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "schema",
            "--config",
            fixture.BasePath,
            "--stdout",
            "--update-config");

        await Assert.That(exitCode).IsEqualTo(2);
        await Assert.That(output).Contains("--update-config needs a schema file");
        await Assert.That(File.ReadAllText(fixture.ConfigPath)).DoesNotContain("$schema");
    }

    private static JsonObject ReadSchema() =>
        JsonNode.Parse(CliConfigSchema.ReadSchemaJson())!.AsObject();

    private static async Task<(int ExitCode, string Output)> InvokeWithOutput(params string[] args)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
#pragma warning disable TUnit0055
            Console.SetOut(writer);
#pragma warning restore TUnit0055
            var exitCode = await Program.InvokeAsync(args);
            return (exitCode, writer.ToString());
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetOut(originalOut);
#pragma warning restore TUnit0055
        }
    }

    private static class SchemaSmokeValidator
    {
        public static IReadOnlyList<string> Validate(JsonObject schema, JsonNode instance)
        {
            var errors = new List<string>();
            ValidateNode(schema, schema, instance, "$", errors);
            return errors;
        }

        private static void ValidateNode(
            JsonObject rootSchema,
            JsonObject schema,
            JsonNode? instance,
            string path,
            List<string> errors)
        {
            if (schema["$ref"] is JsonValue refValue)
            {
                var reference = refValue.GetValue<string>();
                var resolved = ResolveReference(rootSchema, reference);
                ValidateNode(rootSchema, resolved, instance, path, errors);
                return;
            }

            if (schema["type"] is JsonValue typeValue && !MatchesType(typeValue.GetValue<string>(), instance))
            {
                errors.Add($"{path} should be {typeValue.GetValue<string>()}.");
                return;
            }

            if (schema["enum"] is JsonArray enumValues &&
                instance is JsonValue enumInstance &&
                !enumValues.Any(value => value?.ToJsonString() == enumInstance.ToJsonString()))
            {
                errors.Add($"{path} has unsupported value {enumInstance.ToJsonString()}.");
            }

            if (instance is JsonObject instanceObject)
                ValidateObject(rootSchema, schema, instanceObject, path, errors);

            if (instance is JsonArray instanceArray &&
                schema["items"] is JsonObject itemSchema)
            {
                for (var i = 0; i < instanceArray.Count; i++)
                    ValidateNode(rootSchema, itemSchema, instanceArray[i], $"{path}[{i}]", errors);
            }
        }

        private static void ValidateObject(
            JsonObject rootSchema,
            JsonObject schema,
            JsonObject instance,
            string path,
            List<string> errors)
        {
            if (schema["required"] is JsonArray required)
            {
                foreach (var requiredProperty in required.Select(value => value!.GetValue<string>()))
                {
                    if (!instance.ContainsKey(requiredProperty))
                        errors.Add($"{path}.{requiredProperty} is required.");
                }
            }

            var properties = schema["properties"]?.AsObject();
            if (properties == null)
                return;

            if (schema["additionalProperties"] is JsonValue additionalProperties &&
                additionalProperties.GetValue<bool>() == false)
            {
                foreach (var property in instance)
                {
                    if (!properties.ContainsKey(property.Key))
                        errors.Add($"{path}.{property.Key} is not allowed.");
                }
            }

            foreach (var property in instance)
            {
                if (properties[property.Key] is JsonObject propertySchema)
                    ValidateNode(rootSchema, propertySchema, property.Value, $"{path}.{property.Key}", errors);
            }
        }

        private static JsonObject ResolveReference(JsonObject rootSchema, string reference)
        {
            var current = (JsonNode)rootSchema;
            foreach (var segment in reference.TrimStart('#', '/').Split('/'))
                current = current[segment] ?? throw new InvalidOperationException($"Could not resolve schema reference '{reference}'.");

            return current.AsObject();
        }

        private static bool MatchesType(string type, JsonNode? instance) =>
            type switch
            {
                "object" => instance is JsonObject,
                "array" => instance is JsonArray,
                "string" => instance is JsonValue value && value.TryGetValue<string>(out _),
                "boolean" => instance is JsonValue value && value.TryGetValue<bool>(out _),
                _ => true
            };
    }

    private sealed class SchemaCommandFixture : IDisposable
    {
        private SchemaCommandFixture(string basePath)
        {
            BasePath = basePath;
            OutputPath = Path.Combine(basePath, "datalinq.schema.json");
        }

        public string BasePath { get; }
        public string OutputPath { get; }
        public string ConfigPath => Path.Combine(BasePath, "datalinq.json");
        public string UserConfigPath => Path.Combine(BasePath, "datalinq.user.json");

        public static SchemaCommandFixture Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-schema-command-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            return new SchemaCommandFixture(basePath);
        }

        public void WriteConfig(string relativePath, string content)
        {
            var path = Path.Combine(BasePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(BasePath))
                    Directory.Delete(BasePath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
