using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.CLI;

namespace DataLinq.Tests.Unit;

public class DataLinqSecretCommandTests
{
    [Test]
    public async Task List_PrintsSecretNamesOnly()
    {
        var store = new FakeSecretStore();
        store.Set("datalinq/AppDb/password", "secret-value");
        var lines = new List<string>();

        var exitCode = CliSecretCommandService.List(store, lines.Add, (_, message) => lines.Add(message));

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(lines).IsEquivalentTo(["datalinq/AppDb/password"]);
        await Assert.That(string.Join(Environment.NewLine, lines)).DoesNotContain("secret-value");
    }

    [Test]
    public async Task Set_UsesPromptAndStoresValue()
    {
        var store = new FakeSecretStore();
        var prompt = new FakeSecretPrompt("secret-value");
        var lines = new List<string>();

        var exitCode = CliSecretCommandService.Set(
            store,
            prompt,
            "datalinq/AppDb/password",
            lines.Add,
            (_, message) => lines.Add(message));

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(store.Get("datalinq/AppDb/password").Value).IsEqualTo("secret-value");
        await Assert.That(lines.Single()).IsEqualTo("Secret saved: datalinq/AppDb/password");
    }

    [Test]
    public async Task Remove_AsksForConfirmation()
    {
        var store = new FakeSecretStore();
        store.Set("datalinq/AppDb/password", "secret-value");
        var lines = new List<string>();

        var cancelled = CliSecretCommandService.Remove(
            store,
            "datalinq/AppDb/password",
            _ => false,
            lines.Add,
            (_, message) => lines.Add(message));
        var removed = CliSecretCommandService.Remove(
            store,
            "datalinq/AppDb/password",
            _ => true,
            lines.Add,
            (_, message) => lines.Add(message));

        await Assert.That(cancelled).IsEqualTo(0);
        await Assert.That(removed).IsEqualTo(0);
        await Assert.That(store.List()).IsEmpty();
    }

    private sealed class FakeSecretStore : IDataLinqSecretStore
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public bool IsAvailable => true;
        public string UnavailableReason => "";
        public IReadOnlyList<string> List() => values.Keys.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        public SecretResolutionResult Get(string name) => SecretResolutionResult.Success(values[name]);

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

    private sealed class FakeSecretPrompt(string value) : ISecretPrompt
    {
        public bool CanPrompt => true;
        public SecretResolutionResult Prompt(string label) => SecretResolutionResult.Success(value);
        public SecretResolutionResult PromptNewSecret(string label, bool confirm) => SecretResolutionResult.Success(value);
    }
}
