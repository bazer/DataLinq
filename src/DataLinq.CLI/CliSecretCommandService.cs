using System;

namespace DataLinq.CLI;

internal static class CliSecretCommandService
{
    public static int List(
        IDataLinqSecretStore store,
        Action<string> writeLine,
        Action<string, string> writeError)
    {
        if (!store.IsAvailable)
        {
            writeError("SecretsUnavailable", store.UnavailableReason);
            return 2;
        }

        foreach (var name in store.List())
            writeLine(name);

        return 0;
    }

    public static int Set(
        IDataLinqSecretStore store,
        ISecretPrompt prompt,
        string name,
        Action<string> writeLine,
        Action<string, string> writeError)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            writeError("InvalidArgument", "Secret name is required.");
            return 2;
        }

        if (!store.IsAvailable)
        {
            writeError("SecretsUnavailable", store.UnavailableReason);
            return 2;
        }

        if (!prompt.CanPrompt)
        {
            writeError("InputUnavailable", "Setting a secret requires interactive input.");
            return 2;
        }

        var secret = prompt.PromptNewSecret("Enter value", confirm: true);
        if (!secret.Succeeded)
        {
            writeError("SecretNotSaved", secret.Error!);
            return 2;
        }

        var result = store.Set(name, secret.Value!);
        if (!result.Succeeded)
        {
            writeError("SecretNotSaved", result.Error!);
            return 2;
        }

        writeLine($"Secret saved: {name}");
        return 0;
    }

    public static int Remove(
        IDataLinqSecretStore store,
        string name,
        Func<string, bool> confirm,
        Action<string> writeLine,
        Action<string, string> writeError)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            writeError("InvalidArgument", "Secret name is required.");
            return 2;
        }

        if (!store.IsAvailable)
        {
            writeError("SecretsUnavailable", store.UnavailableReason);
            return 2;
        }

        if (!confirm(name))
        {
            writeLine("No secrets changed.");
            return 0;
        }

        var result = store.Remove(name);
        if (!result.Succeeded)
        {
            writeError("SecretNotRemoved", result.Error!);
            return 2;
        }

        writeLine($"Secret removed: {name}");
        return 0;
    }
}
