using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.CLI;
using DataLinq.Config;

namespace DataLinq.Tests.Unit;

public class DataLinqSecretReferenceTests
{
    [Test]
    public async Task ResolveString_ResolvesEnvironmentReferencesAndRegistersRedaction()
    {
        var context = CreateContext(environment: new Dictionary<string, string?>
        {
            ["APP_PASSWORD"] = "p@ssw0rd"
        });

        var result = SecretReferenceResolver.ResolveString("Password=${env:APP_PASSWORD}", context);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value).IsEqualTo("Password=p@ssw0rd");
        await Assert.That(context.Redactor.Redact("value p@ssw0rd from ${env:APP_PASSWORD}")).IsEqualTo("value ******** from ********");
    }

    [Test]
    public async Task ResolveString_FailsForMissingEnvironmentVariable()
    {
        var result = SecretReferenceResolver.ResolveString("${env:MISSING_PASSWORD}", CreateContext());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("MISSING_PASSWORD");
    }

    [Test]
    public async Task ResolveString_FailsForMalformedAndUnknownReferences()
    {
        var malformed = SecretReferenceResolver.ResolveString("${env:APP_PASSWORD", CreateContext());
        var unknown = SecretReferenceResolver.ResolveString("${vault:app/password}", CreateContext());

        await Assert.That(malformed.Succeeded).IsFalse();
        await Assert.That(unknown.Succeeded).IsFalse();
        await Assert.That(unknown.Error).Contains("Unknown secret reference provider");
    }

    [Test]
    public async Task ResolveConnectionString_HandlesPasswordSeparatorsSafely()
    {
        var store = new FakeSecretStore();
        store.Set("datalinq/AppDb/password", "semi;colon");
        var context = CreateContext(store: store);

        var result = SecretReferenceResolver.ResolveConnectionString(
            "Server=localhost;Database=appdb;Password=${secret:datalinq/AppDb/password};",
            context);

        await Assert.That(result.Succeeded).IsTrue();
        var connectionString = new DataLinqConnectionString(result.Value);
        await Assert.That(connectionString.GetValue("Password")).IsEqualTo("semi;colon");
        await Assert.That(context.Redactor.Redact(result.Value)).DoesNotContain("semi;colon");
    }

    [Test]
    public async Task ResolveConnectionString_AllowsWholeConnectionStringSecret()
    {
        var store = new FakeSecretStore();
        store.Set("datalinq/AppDb/connection-string", "Data Source=app.db;Cache=Shared;");
        var context = CreateContext(store: store);

        var result = SecretReferenceResolver.ResolveConnectionString("${secret:datalinq/AppDb/connection-string}", context);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value).IsEqualTo("Data Source=app.db;Cache=Shared;");
    }

    [Test]
    public async Task PromptReferences_AreCachedPerContextAndFailWhenPromptingIsDisabled()
    {
        var prompt = new FakeSecretPrompt(["first"]);
        var allowedContext = CreateContext(prompt: prompt, allowPrompt: true);
        var blockedContext = CreateContext(prompt: prompt, allowPrompt: false);

        var first = SecretReferenceResolver.ResolveString("${prompt:AppDb password}", allowedContext);
        var second = SecretReferenceResolver.ResolveString("${prompt:AppDb password}", allowedContext);
        var blocked = SecretReferenceResolver.ResolveString("${prompt:AppDb password}", blockedContext);

        await Assert.That(first.Value).IsEqualTo("first");
        await Assert.That(second.Value).IsEqualTo("first");
        await Assert.That(prompt.PromptCount).IsEqualTo(1);
        await Assert.That(blocked.Succeeded).IsFalse();
    }

    [Test]
    public async Task ConfigLoader_ResolvesSecretReferencesBeforeBuildingConfig()
    {
        using var fixture = SecretConfigFixture.Create();
        fixture.WriteConfig(
            """
            {
              "Databases": [
                {
                  "Name": "AppDb",
                  "Connections": [
                    {
                      "Type": "SQLite",
                      "DataSourceName": "app.db",
                      "ConnectionString": "${env:APP_CONNECTION}"
                    }
                  ]
                }
              ]
            }
            """);
        var context = CreateContext(environment: new Dictionary<string, string?>
        {
            ["APP_CONNECTION"] = "Data Source=app.db;Cache=Shared;"
        });

        var loaded = CliConfigLoader.TryRead(fixture.ConfigPath, _ => { }, out var config, out var failure, context);

        await Assert.That(loaded).IsTrue();
        await Assert.That(failure).IsNull();
        await Assert.That(config.Databases.Single().Connections.Single().ConnectionString.Original).IsEqualTo("Data Source=app.db;Cache=Shared;");
    }

    private static SecretResolutionContext CreateContext(
        FakeSecretStore? store = null,
        FakeSecretPrompt? prompt = null,
        Dictionary<string, string?>? environment = null,
        bool allowPrompt = true) =>
        new(
            store ?? new FakeSecretStore(),
            prompt ?? new FakeSecretPrompt([]),
            new SecretRedactor(),
            name => environment != null && environment.TryGetValue(name, out var value) ? value : null,
            allowPrompt);

    private sealed class FakeSecretStore : IDataLinqSecretStore
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public bool IsAvailable { get; set; } = true;
        public string UnavailableReason { get; set; } = "unavailable";

        public IReadOnlyList<string> List() => values.Keys.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

        public SecretResolutionResult Get(string name) =>
            values.TryGetValue(name, out var value)
                ? SecretResolutionResult.Success(value)
                : SecretResolutionResult.Failure($"DataLinq local secret '{name}' does not exist.");

        public SecretResolutionResult Set(string name, string value)
        {
            values[name] = value;
            return SecretResolutionResult.Success("true");
        }

        public SecretResolutionResult Remove(string name)
        {
            values.Remove(name);
            return SecretResolutionResult.Success("true");
        }
    }

    private sealed class FakeSecretPrompt : ISecretPrompt
    {
        private readonly Queue<string> values;

        public FakeSecretPrompt(IEnumerable<string> values)
        {
            this.values = new Queue<string>(values);
        }

        public bool CanPrompt { get; set; } = true;
        public int PromptCount { get; private set; }

        public SecretResolutionResult Prompt(string label)
        {
            PromptCount++;
            return SecretResolutionResult.Success(values.Dequeue());
        }

        public SecretResolutionResult PromptNewSecret(string label, bool confirm)
        {
            PromptCount++;
            return SecretResolutionResult.Success(values.Dequeue());
        }
    }

    private sealed class SecretConfigFixture : IDisposable
    {
        private SecretConfigFixture(string basePath)
        {
            BasePath = basePath;
            ConfigPath = Path.Combine(basePath, "datalinq.json");
        }

        public string BasePath { get; }
        public string ConfigPath { get; }

        public static SecretConfigFixture Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-secret-config-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            return new SecretConfigFixture(basePath);
        }

        public void WriteConfig(string contents) => File.WriteAllText(ConfigPath, contents);

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
