> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: CLI Secret References

**Status:** Implemented for V1 on Windows; cross-platform local secret backends and wizard polish remain.
**Goal:** Let `datalinq.json` and `datalinq.user.json` reference secrets without storing secret values directly in config files. V1 supports environment variables, DataLinq local secrets, and prompt-at-runtime secrets.

## Executive Position

DataLinq supports secret references, not encrypted config blobs.

Encrypted values inside config files usually become theater unless key management is solved. If the encrypted value and the key needed to decrypt it both live in the same repo, user profile, or command line, the system is more complicated without being meaningfully safer.

The right model is:

```json
"ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=${secret:datalinq/AppDb/password};"
```

The config file stores a reference. The secret value lives somewhere else and is resolved only when the CLI needs to connect.

V1 supports exactly these providers:

- `${env:NAME}`
- `${secret:name}`
- `${prompt:label}`

Do not add Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, or .NET User Secrets in V1. Those can come later. The first slice should get the resolution model, redaction behavior, local secret store, and CLI commands right.

## Current Implementation Status

Implemented:

- `${env:NAME}`
- `${secret:name}`
- `${prompt:label}`
- whole-connection-string secret references
- placeholder resolution inside connection-string values through `DbConnectionStringBuilder`
- prompt caching per process
- secret redaction for resolved values and raw secret references in CLI-controlled output
- password/pwd connection-string redaction
- `datalinq secrets list`
- `datalinq secrets set <name>`
- `datalinq secrets remove <name>`
- Windows Credential Manager backend
- explicit unavailable-secret-store behavior on macOS/Linux instead of plaintext fallback
- config loading resolves secret references before building `DataLinqConfig`

Remaining work worth doing:

1. Add macOS Keychain and Linux Secret Service/libsecret backends if cross-platform local secrets become a priority.
2. Improve `config init` so MySQL/MariaDB password prompts offer local secret, environment variable, runtime prompt, or direct plaintext with a warning. Current defaults use prompt references for server providers, which is safe but not as ergonomic as the planned menu.
3. Add a non-interactive `secrets set --stdin` only if automation needs it. Do not add plaintext command-line value arguments.
4. Expand redaction keys later if providers need `Access Token`, `Token`, `ApiKey`, or `Secret`. V1 intentionally covers database `Password`/`Pwd`.
5. Route secret-resolution diagnostics through the future diagnostics renderer once that slice lands.

## Why Not .NET User Secrets First?

.NET User Secrets are useful, but they are not a proper secure store. Microsoft documents that Secret Manager is for development only and does not encrypt stored secrets. The values live in a JSON file under the user profile. That is still better than checking secrets into source control, but it is not the "proper secrets" feature this plan is aiming for.

Reference: Microsoft docs, "Safe storage of app secrets in development in ASP.NET Core":

- <https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets>

DataLinq can add a `${dotnet-user-secrets:...}` provider later as a convenience bridge, but it should not be the primary answer.

## Secret Reference Syntax

Use:

```text
${provider:key}
```

Supported V1 providers:

```text
${env:DATALINQ_APPDB_PASSWORD}
${secret:datalinq/AppDb/password}
${prompt:AppDb password}
```

Rules:

- provider names are case-insensitive
- keys are provider-specific
- missing values are errors
- malformed references are errors
- unknown providers are errors
- resolved secret values are not logged
- references can appear in `ConnectionString`

For V1, do not support nested secret references. If resolving `${env:FOO}` returns `${secret:bar}`, treat that as a literal value unless a later feature explicitly adds recursive expansion. Recursive resolution is harder to reason about and can create cycles.

## Connection String Handling

Connection strings are the most important target.

DataLinq should support:

```json
"ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=${secret:datalinq/AppDb/password};"
```

and:

```json
"ConnectionString": "${secret:datalinq/AppDb/connection-string}"
```

Connection-string resolution needs care. Passwords can contain semicolons, quotes, braces, and other characters that break raw string substitution.

Recommended behavior:

1. If the entire `ConnectionString` field is one secret reference, resolve it as a full connection string.
2. Otherwise parse the connection string with placeholders still present.
3. Resolve placeholders inside connection-string values.
4. Rebuild the final connection string through `DbConnectionStringBuilder` so special characters are escaped correctly.

This means the common password case is safe:

```text
Password=${secret:datalinq/AppDb/password}
```

even if the password itself contains `;`.

For V1, arbitrary string interpolation inside one connection-string value is allowed only if the final value is assigned through the builder. Do not concatenate raw connection-string text after resolving secrets.

## Supported Providers

### Environment Variables

Syntax:

```text
${env:DATALINQ_APPDB_PASSWORD}
```

Behavior:

- read from `Environment.GetEnvironmentVariable`
- missing or empty values should fail by default
- value is redacted in diagnostics

Environment variables are the best V1 provider for CI and containerized runs. They are not a secure local secret store by themselves, but they are the universal integration point.

### DataLinq Local Secrets

Syntax:

```text
${secret:datalinq/AppDb/password}
```

Behavior:

- read from a DataLinq-managed local secret store
- set/list/remove through `datalinq secrets`
- no plaintext fallback
- fail with an actionable message if no secure local backend is available

Recommended commands:

```bash
datalinq secrets set datalinq/AppDb/password
datalinq secrets list
datalinq secrets remove datalinq/AppDb/password
```

`set` should prompt for the value without echoing it by default.

Avoid:

```bash
datalinq secrets set datalinq/AppDb/password "plain text on command line"
```

Command-line arguments are often visible in shell history and process inspection. If non-interactive secret setting is needed later, add:

```bash
datalinq secrets set datalinq/AppDb/password --stdin
```

### Prompt at Runtime

Syntax:

```text
${prompt:AppDb password}
```

Behavior:

- prompt the user when the secret is needed
- hide input when possible
- cache the resolved value for the current process only
- never write the value to disk

Prompt references are useful for one-off admin operations and highly locked-down local setups.

They are bad for automation. In non-interactive mode, they should fail with a clear message.

For batch commands such as `validate --all` or `validate --recursive`, prompt once per unique prompt label per process, then reuse the value for targets that reference the same prompt.

## Local Secret Store Backend

V1 needs a backend abstraction:

```csharp
public interface IDataLinqSecretStore
{
    bool IsAvailable { get; }
    IReadOnlyList<string> List();
    Option<string, IDLOptionFailure> Get(string name);
    Option<bool, IDLOptionFailure> Set(string name, string value);
    Option<bool, IDLOptionFailure> Remove(string name);
}
```

The security rule is simple:

```text
Do not silently fall back to plaintext files.
```

If the platform backend is unavailable, `${secret:...}` should fail and suggest `${env:...}` or `${prompt:...}`.

### Windows

Use an OS-backed store.

Pragmatic options:

- Windows Credential Manager
- DPAPI-protected user-profile store

`System.Security.Cryptography.ProtectedData` wraps Windows DPAPI. Microsoft documents that DPAPI uses user or machine credentials for encryption, but `ProtectedData` is Windows-only.

Reference:

- <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata>

Credential Manager is more semantically correct for named secrets, but DPAPI-protected local storage may be simpler to ship first. Either is acceptable if the plan is explicit and values are not plaintext.

### macOS

Remaining work: use Keychain.

Current behavior: `${secret:...}` reports that local DataLinq secrets are unavailable on this platform and suggests `${env:...}` or `${prompt:...}`.

### Linux

Remaining work: use Secret Service/libsecret.

Linux developer machines vary. If no supported secret service is available, fail clearly. Do not write plaintext secrets to `~/.config/datalinq` and call it secure.

## Redaction Policy

Secret handling is useless if diagnostics leak the resolved values.

Add a redaction layer that tracks:

- resolved secret values
- connection-string password keys
- raw secret references

Then redact output from:

- config read logs
- verbose logs
- validation output
- generate models output
- provider connection failures where possible
- JSON output
- exception text surfaced by CLI commands

Redaction should replace secrets with:

```text
********
```

Do not redact non-secret config like server names, database names, provider types, or model directory paths.

### Connection String Redaction

`DataLinqConnectionString` already knows whether it has password-like keys via `HasPassword`.

Add a redacted representation, for example:

```csharp
public string RedactedOriginal { get; }
```

or a helper:

```csharp
DataLinqConnectionStringRedactor.Redact(connectionString)
```

Password-like keys should be redacted even if they did not come from a secret reference:

- `Password`
- `Pwd`

Consider common variants later:

- `Access Token`
- `Token`
- `ApiKey`
- `Secret`

For V1, password and pwd are enough for database connection strings.

## Config Resolution Architecture

Do not put console prompting in low-level config parsing.

Recommended layers:

1. `ConfigReader` reads raw JSON.
2. A `SecretReferenceResolver` resolves secret references in string values.
3. `DataLinqConfig` is built from resolved config.
4. CLI commands receive a `SecretRedactor` to protect logs/diagnostics.

Possible shape:

```csharp
public sealed class SecretResolutionContext
{
    public required ISecretReferenceProvider Env { get; init; }
    public required ISecretReferenceProvider LocalSecrets { get; init; }
    public required ISecretReferenceProvider Prompt { get; init; }
    public required SecretRedactor Redactor { get; init; }
    public bool AllowPrompt { get; init; }
}
```

Add config-loading overloads only where needed:

```csharp
DataLinqConfig.FindAndReadConfigs(configPath, log, secretResolutionContext)
```

If a caller does not provide a resolver and the config contains secret references, fail with a clear message rather than passing unresolved placeholders into providers.

## Interaction With `datalinq config init`

The `config init` wizard should prefer secret references for passwords. The current server-provider defaults use `${prompt:...}`; the richer storage-choice prompt below is still future work.

Recommended prompt:

```text
How should DataLinq store the database password?
  1. DataLinq local secret store (recommended)
  2. Environment variable
  3. Prompt each time
  4. Put it directly in datalinq.user.json
```

The direct plaintext option should exist only with a warning. Some local SQLite setups do not need secrets at all, and some internal dev databases may be intentionally low-risk, but the wizard should not normalize plaintext passwords.

If the user chooses local secret store:

- write `${secret:datalinq/<DatabaseName>/password}` to `datalinq.user.json`
- call the same secret-store service as `datalinq secrets set`
- never echo the value

If the user chooses environment variable:

- write `${env:DATALINQ_<DATABASE>_PASSWORD}`
- tell the user how to set it

If the user chooses prompt:

- write `${prompt:<DatabaseName> password}`

## CLI Command Design

Add a new command family:

```bash
datalinq secrets list
datalinq secrets set <name>
datalinq secrets remove <name>
```

Implement these as nested commands after the CLI moves to `System.CommandLine`. Do not add temporary flat commands such as `secrets-list`; the CLI is still pre-1.0 and the nested command shape is the one worth teaching.

Recommended UX:

```text
datalinq secrets set datalinq/AppDb/password
Enter value:
Confirm value:
Secret saved: datalinq/AppDb/password
```

`list` should show names only, never values.

`remove` should ask for confirmation unless a later `--yes` flag is added.

## Error Behavior

Examples:

Missing env var:

```text
Error: Secret reference ${env:DATALINQ_APPDB_PASSWORD} could not be resolved because environment variable 'DATALINQ_APPDB_PASSWORD' is not set.
```

Missing local secret:

```text
Error: Secret reference ${secret:datalinq/AppDb/password} could not be resolved because that DataLinq local secret does not exist.
```

Prompt in non-interactive run:

```text
Error: Secret reference ${prompt:AppDb password} requires interactive input, but standard input is not interactive.
```

Unknown provider:

```text
Error: Unknown secret reference provider 'vault'. Supported providers: env, secret, prompt.
```

Do not print the resolved value in errors.

## Testing Plan

Add tests for:

- parsing one secret reference
- parsing multiple references in a string
- malformed references fail clearly
- unknown providers fail clearly
- missing environment variable fails clearly
- environment variable resolves and registers redaction
- local secret resolves through a fake store
- prompt resolves through a fake prompt service
- prompt values are cached per process
- prompt provider fails when prompts are disabled
- whole-connection-string secret works
- connection-string password placeholder with semicolons rebuilds safely
- connection-string password is redacted
- provider errors do not leak registered secret values where the CLI controls formatting
- `secrets list` does not print values
- `secrets set` can be tested with an in-memory store and fake hidden input

Use fake secret providers for unit tests. Do not require actual DPAPI, Keychain, or Secret Service in ordinary tests.

## Documentation Plan

Updated:

- `docs/Configuration files.md`
- `docs/CLI Documentation.md`
- `docs/getting-started/Configuration and Model Generation.md`
- `datalinq config init` docs

Document:

- `${env:...}`
- `${secret:...}`
- `${prompt:...}`
- how to set local secrets
- how to use environment variables in CI
- that DataLinq local secrets are machine/user local
- that prompt secrets are not suitable for automation
- that .NET User Secrets are not the same feature

These docs now describe shipped V1 behavior. Keep future provider/backend notes clearly labeled as future work.

## Non-Goals

- Do not add Azure Key Vault in V1.
- Do not add AWS Secrets Manager in V1.
- Do not add HashiCorp Vault in V1.
- Do not add .NET User Secrets in V1.
- Do not encrypt values inside `datalinq.json` or `datalinq.user.json`.
- Do not silently fall back to plaintext secret storage.
- Do not print secret values through `secrets list`.
- Do not support recursive secret-reference expansion in V1.
- Do not make prompt-based secrets work in non-interactive automation.

## Acceptance Criteria

- Config strings can reference environment variables with `${env:NAME}`.
- Config strings can reference DataLinq local secrets with `${secret:name}`.
- Config strings can request runtime input with `${prompt:label}`.
- Secret references in connection strings are resolved before provider connection.
- Password values containing connection-string separators are handled safely.
- Missing or malformed secret references fail with clear diagnostics.
- Resolved secret values are redacted from CLI-controlled output.
- `datalinq secrets set/list/remove` or equivalent V1 verbs exist for local secrets.
- Local secret storage never silently falls back to plaintext.
- `datalinq config init` can write secret references instead of plaintext passwords once both features are implemented.
