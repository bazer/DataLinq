using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DataLinq.Config;

namespace DataLinq.CLI;

internal sealed record SecretReference(string Provider, string Key, string RawText);

internal sealed record SecretResolutionResult(string? Value, string? Error)
{
    public bool Succeeded => Error == null;

    public static SecretResolutionResult Success(string value) => new(value, null);

    public static SecretResolutionResult Failure(string error) => new(null, error);
}

internal interface IDataLinqSecretStore
{
    bool IsAvailable { get; }
    string UnavailableReason { get; }
    IReadOnlyList<string> List();
    SecretResolutionResult Get(string name);
    SecretResolutionResult Set(string name, string value);
    SecretResolutionResult Remove(string name);
}

internal interface ISecretPrompt
{
    bool CanPrompt { get; }
    SecretResolutionResult Prompt(string label);
    SecretResolutionResult PromptNewSecret(string label, bool confirm);
}

internal sealed class SecretRedactor
{
    private const string Replacement = "********";
    private readonly List<string> values = [];

    public void Register(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (value.Length < 3 && !value.StartsWith("${", StringComparison.Ordinal))
            return;

        if (!values.Contains(value, StringComparer.Ordinal))
            values.Add(value);
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";

        var redacted = text;
        foreach (var value in values.OrderByDescending(static value => value.Length))
            redacted = redacted.Replace(value, Replacement, StringComparison.Ordinal);

        return redacted;
    }
}

internal sealed class SecretResolutionContext
{
    private readonly Dictionary<string, SecretResolutionResult> promptCache = new(StringComparer.Ordinal);

    public SecretResolutionContext(
        IDataLinqSecretStore localSecrets,
        ISecretPrompt prompt,
        SecretRedactor redactor,
        Func<string, string?>? environmentProvider = null,
        bool allowPrompt = false)
    {
        LocalSecrets = localSecrets;
        Prompt = prompt;
        Redactor = redactor;
        EnvironmentProvider = environmentProvider ?? Environment.GetEnvironmentVariable;
        AllowPrompt = allowPrompt;
    }

    public IDataLinqSecretStore LocalSecrets { get; }
    public ISecretPrompt Prompt { get; }
    public SecretRedactor Redactor { get; }
    public Func<string, string?> EnvironmentProvider { get; }
    public bool AllowPrompt { get; }

    public bool TryGetCachedPrompt(string label, out SecretResolutionResult result) =>
        promptCache.TryGetValue(label, out result!);

    public void CachePrompt(string label, SecretResolutionResult result)
    {
        if (result.Succeeded)
            promptCache[label] = result;
    }

    public static SecretResolutionContext CreateDefault() =>
        new(
            CliSecretStoreFactory.CreateDefault(),
            new ConsoleSecretPrompt(),
            new SecretRedactor(),
            allowPrompt: !Console.IsInputRedirected);
}

internal static class SecretReferenceResolver
{
    public static SecretResolutionResult ResolveString(string value, SecretResolutionContext context)
    {
        var references = SecretReferenceParser.FindReferences(value);
        if (!references.Succeeded)
            return SecretResolutionResult.Failure(references.Error!);

        var resolved = value;
        foreach (var reference in references.Value!)
        {
            context.Redactor.Register(reference.RawText);
            var secret = ResolveReference(reference, context);
            if (!secret.Succeeded)
                return secret;

            resolved = resolved.Replace(reference.RawText, secret.Value, StringComparison.Ordinal);
        }

        return SecretResolutionResult.Success(resolved);
    }

    public static SecretResolutionResult ResolveConnectionString(string value, SecretResolutionContext context)
    {
        if (SecretReferenceParser.TryParseSingleReference(value, out var singleReference, out var singleError))
        {
            context.Redactor.Register(singleReference.RawText);
            return ResolveReference(singleReference, context);
        }

        if (singleError != null)
            return SecretResolutionResult.Failure(singleError);

        var references = SecretReferenceParser.FindReferences(value);
        if (!references.Succeeded)
            return SecretResolutionResult.Failure(references.Error!);

        if (references.Value!.Count == 0)
        {
            RegisterPasswordValues(value, context.Redactor);
            return SecretResolutionResult.Success(value);
        }

        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder
            {
                ConnectionString = value
            };
        }
        catch (ArgumentException exception)
        {
            return SecretResolutionResult.Failure($"ConnectionString contains a secret reference but could not be parsed safely. {exception.Message}");
        }

        foreach (var key in builder.Keys.Cast<string>().ToArray())
        {
            var rawValue = builder[key]?.ToString() ?? "";
            var resolvedValue = ResolveString(rawValue, context);
            if (!resolvedValue.Succeeded)
                return resolvedValue;

            builder[key] = resolvedValue.Value;
        }

        var connectionString = builder.ConnectionString;
        RegisterPasswordValues(connectionString, context.Redactor);
        return SecretResolutionResult.Success(connectionString);
    }

    private static SecretResolutionResult ResolveReference(SecretReference reference, SecretResolutionContext context)
    {
        return reference.Provider.ToLowerInvariant() switch
        {
            "env" => ResolveEnvironment(reference, context),
            "secret" => ResolveLocalSecret(reference, context),
            "prompt" => ResolvePrompt(reference, context),
            _ => SecretResolutionResult.Failure(
                $"Unknown secret reference provider '{reference.Provider}'. Supported providers: env, secret, prompt.")
        };
    }

    private static SecretResolutionResult ResolveEnvironment(SecretReference reference, SecretResolutionContext context)
    {
        var value = context.EnvironmentProvider(reference.Key);
        if (string.IsNullOrEmpty(value))
        {
            return SecretResolutionResult.Failure(
                $"Secret reference {reference.RawText} could not be resolved because environment variable '{reference.Key}' is not set.");
        }

        context.Redactor.Register(value);
        return SecretResolutionResult.Success(value);
    }

    private static SecretResolutionResult ResolveLocalSecret(SecretReference reference, SecretResolutionContext context)
    {
        if (!context.LocalSecrets.IsAvailable)
        {
            return SecretResolutionResult.Failure(
                $"Secret reference {reference.RawText} could not be resolved because DataLinq local secrets are unavailable. {context.LocalSecrets.UnavailableReason} Use ${{env:NAME}} or ${{prompt:label}} instead.");
        }

        var value = context.LocalSecrets.Get(reference.Key);
        if (!value.Succeeded)
            return SecretResolutionResult.Failure($"Secret reference {reference.RawText} could not be resolved. {value.Error}");

        context.Redactor.Register(value.Value);
        return value;
    }

    private static SecretResolutionResult ResolvePrompt(SecretReference reference, SecretResolutionContext context)
    {
        if (!context.AllowPrompt || !context.Prompt.CanPrompt)
        {
            return SecretResolutionResult.Failure(
                $"Secret reference {reference.RawText} requires interactive input, but standard input is not interactive.");
        }

        if (context.TryGetCachedPrompt(reference.Key, out var cached))
        {
            if (cached.Succeeded)
                context.Redactor.Register(cached.Value);

            return cached;
        }

        var value = context.Prompt.Prompt(reference.Key);
        if (value.Succeeded)
        {
            context.CachePrompt(reference.Key, value);
            context.Redactor.Register(value.Value);
        }

        return value;
    }

    private static void RegisterPasswordValues(string connectionString, SecretRedactor redactor)
    {
        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            foreach (var key in builder.Keys.Cast<string>())
            {
                if (IsPasswordKey(key))
                    redactor.Register(builder[key]?.ToString());
            }
        }
        catch (ArgumentException)
        {
        }
    }

    private static bool IsPasswordKey(string key) =>
        key.Equals("password", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("pwd", StringComparison.OrdinalIgnoreCase);
}

internal sealed record SecretReferenceParseResult(IReadOnlyList<SecretReference>? Value, string? Error)
{
    public bool Succeeded => Error == null;
}

internal static class SecretReferenceParser
{
    public static SecretReferenceParseResult FindReferences(string value)
    {
        var references = new List<SecretReference>();
        var index = 0;
        while (index < value.Length)
        {
            var start = value.IndexOf("${", index, StringComparison.Ordinal);
            if (start < 0)
                break;

            var end = value.IndexOf('}', start + 2);
            if (end < 0)
                return new SecretReferenceParseResult(null, $"Malformed secret reference starting at character {start}. Missing closing '}}'.");

            var raw = value[start..(end + 1)];
            if (!TryParse(raw, out var reference, out var error))
                return new SecretReferenceParseResult(null, error);

            references.Add(reference);
            index = end + 1;
        }

        return new SecretReferenceParseResult(references, null);
    }

    public static bool TryParseSingleReference(string value, out SecretReference reference, out string? error)
    {
        reference = null!;
        error = null;

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("${", StringComparison.Ordinal))
        {
            if (trimmed.Contains("${", StringComparison.Ordinal))
                return false;

            return false;
        }

        if (!trimmed.EndsWith('}'))
        {
            error = "Malformed secret reference. Missing closing '}'.";
            return false;
        }

        if (!TryParse(trimmed, out reference, out error))
            return false;

        return trimmed.Length == value.Trim().Length;
    }

    private static bool TryParse(string raw, out SecretReference reference, out string? error)
    {
        reference = null!;
        error = null;

        if (!raw.StartsWith("${", StringComparison.Ordinal) || !raw.EndsWith('}'))
        {
            error = $"Malformed secret reference '{raw}'. Expected '${{provider:key}}'.";
            return false;
        }

        var body = raw[2..^1];
        var separator = body.IndexOf(':');
        if (separator <= 0 || separator == body.Length - 1)
        {
            error = $"Malformed secret reference '{raw}'. Expected '${{provider:key}}'.";
            return false;
        }

        var provider = body[..separator].Trim();
        var key = body[(separator + 1)..].Trim();
        if (provider.Length == 0 || key.Length == 0)
        {
            error = $"Malformed secret reference '{raw}'. Provider and key are required.";
            return false;
        }

        reference = new SecretReference(provider, key, raw);
        return true;
    }
}

internal sealed class ConsoleSecretPrompt : ISecretPrompt
{
    private readonly Dictionary<string, string> promptCache = new(StringComparer.Ordinal);

    public bool CanPrompt => !Console.IsInputRedirected;

    public SecretResolutionResult Prompt(string label)
    {
        if (promptCache.TryGetValue(label, out var cached))
            return SecretResolutionResult.Success(cached);

        var value = ReadSecret($"{label}: ");
        promptCache[label] = value;
        return SecretResolutionResult.Success(value);
    }

    public SecretResolutionResult PromptNewSecret(string label, bool confirm)
    {
        var value = ReadSecret($"{label}: ");
        if (!confirm)
            return SecretResolutionResult.Success(value);

        var confirmation = ReadSecret($"Confirm {label}: ");
        if (!string.Equals(value, confirmation, StringComparison.Ordinal))
            return SecretResolutionResult.Failure("Secret values did not match.");

        return SecretResolutionResult.Success(value);
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? "";

        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                    builder.Length--;

                continue;
            }

            if (!char.IsControl(key.KeyChar))
                builder.Append(key.KeyChar);
        }
    }
}

internal static class CliSecretStoreFactory
{
    public static IDataLinqSecretStore CreateDefault() =>
        OperatingSystem.IsWindows()
            ? new WindowsCredentialSecretStore()
            : new UnavailableSecretStore("No secure DataLinq local secret backend is implemented for this platform yet.");
}

internal sealed class UnavailableSecretStore(string reason) : IDataLinqSecretStore
{
    public bool IsAvailable => false;
    public string UnavailableReason { get; } = reason;
    public IReadOnlyList<string> List() => [];
    public SecretResolutionResult Get(string name) => SecretResolutionResult.Failure(UnavailableReason);
    public SecretResolutionResult Set(string name, string value) => SecretResolutionResult.Failure(UnavailableReason);
    public SecretResolutionResult Remove(string name) => SecretResolutionResult.Failure(UnavailableReason);
}

internal sealed class WindowsCredentialSecretStore : IDataLinqSecretStore
{
    private const string TargetPrefix = "DataLinq/";
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string UnavailableReason => IsAvailable
        ? ""
        : "Windows Credential Manager is available only on Windows.";

    public IReadOnlyList<string> List()
    {
        if (!IsAvailable)
            return [];

        if (!CredEnumerate($"{TargetPrefix}*", 0, out var count, out var credentials))
            return [];

        try
        {
            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var credentialPointer = Marshal.ReadIntPtr(credentials, i * IntPtr.Size);
                var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
                var targetName = Marshal.PtrToStringUni(credential.TargetName) ?? "";
                if (targetName.StartsWith(TargetPrefix, StringComparison.Ordinal))
                    names.Add(targetName[TargetPrefix.Length..]);
            }

            return names.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        }
        finally
        {
            CredFree(credentials);
        }
    }

    public SecretResolutionResult Get(string name)
    {
        if (!IsAvailable)
            return SecretResolutionResult.Failure(UnavailableReason);

        if (!CredRead(ToTargetName(name), CredentialTypeGeneric, 0, out var credentialPointer))
            return SecretResolutionResult.Failure($"DataLinq local secret '{name}' does not exist.");

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return SecretResolutionResult.Success(Encoding.Unicode.GetString(bytes));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public SecretResolutionResult Set(string name, string value)
    {
        if (!IsAvailable)
            return SecretResolutionResult.Failure(UnavailableReason);

        var bytes = Encoding.Unicode.GetBytes(value);
        var credential = new Credential
        {
            Type = CredentialTypeGeneric,
            TargetName = Marshal.StringToCoTaskMemUni(ToTargetName(name)),
            CredentialBlob = Marshal.AllocCoTaskMem(bytes.Length),
            CredentialBlobSize = bytes.Length,
            Persist = CredentialPersistLocalMachine,
            UserName = Marshal.StringToCoTaskMemUni(Environment.UserName)
        };

        try
        {
            Marshal.Copy(bytes, 0, credential.CredentialBlob, bytes.Length);
            if (!CredWrite(ref credential, 0))
                return SecretResolutionResult.Failure($"Failed to save DataLinq local secret '{name}'.");

            return SecretResolutionResult.Success("true");
        }
        finally
        {
            Marshal.FreeCoTaskMem(credential.TargetName);
            Marshal.FreeCoTaskMem(credential.CredentialBlob);
            Marshal.FreeCoTaskMem(credential.UserName);
        }
    }

    public SecretResolutionResult Remove(string name)
    {
        if (!IsAvailable)
            return SecretResolutionResult.Failure(UnavailableReason);

        if (!CredDelete(ToTargetName(name), CredentialTypeGeneric, 0))
            return SecretResolutionResult.Failure($"DataLinq local secret '{name}' does not exist.");

        return SecretResolutionResult.Success("true");
    }

    private static string ToTargetName(string name) => $"{TargetPrefix}{name}";

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential userCredential, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
