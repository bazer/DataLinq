# Runtime Schema Validation

These are implemented **0.10 development APIs**, not yet released. SQLite, MySQL and MariaDB can compare a finalized, compiled DataLinq model with a fresh observation of its database. Applications can inspect the result directly or require validation before Generic Host starts workers and HTTP listeners.

Validation observes the selected provider's effective database. It does not create a missing database, apply migrations, repair drift, borrow an application transaction, or dispose the provider. It needs no model source files, `datalinq.json`, user companion file, or Tools/CLI package. [CLI validation and diff](Schema%20Validation%20and%20Diff.md) are separate workflows that read tooling configuration and model files.

## Validate an existing database

Given an existing `Database<Employees>` named `database`:

```csharp
using DataLinq.Validation;

var result = await database.ValidateSchemaAsync(
    options =>
    {
        options.CommandTimeout = TimeSpan.FromSeconds(30);
        options.Include = ["employees", "departments"];
    },
    cancellationToken: cancellationToken);

foreach (var difference in result.Differences)
    Console.WriteLine($"{difference.Severity}: {difference.Path}: {difference.Message}");

foreach (var issue in result.Issues)
    Console.WriteLine($"{issue.Severity}: {issue.ObjectPath}: {issue.Message}");

if (result.HasFailures)
{
    // The observation completed, but the captured policy rejected its findings.
}
```

Use the database names in your compiled model for Include. The example names are illustrative, not C# property names. Omit configuration to compare all model/live tables and views. `ValidateSchema()` is the synchronous equivalent. `EnsureSchemaValid()` and `EnsureSchemaValidAsync()` perform the same comparison and throw `DataLinqSchemaValidationException` only when a completed result has `HasFailures=true`; its `Result` property exposes that exact result.

The static helper accepts an existing provider and an options object:

```csharp
var result = await DataLinqSchemaValidator.ValidateAsync(
    database.Provider,
    new DataLinqSchemaValidationOptions { CommandTimeout = TimeSpan.FromSeconds(30) },
    cancellationToken);
```

`DataLinqSchemaValidator.Validate(provider, options)` is synchronous. Database configuration callbacks run once, synchronously, before the call returns a task. Options and Include are captured before asynchronous work; changing a retained options object cannot change an in-flight comparison. Each invocation reads fresh metadata. The synchronous path uses native synchronous commands; the asynchronous path uses the provider's async APIs. [Microsoft.Data.Sqlite's async I/O limitation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async) still applies.

## Policy, scope and results

| Option | Default | Meaning |
| --- | --- | --- |
| `FailOnSeverity` | `Error` | Lowest difference severity that fails the comparison: `Info`, `Warning` or `Error`. |
| `TreatValidationIssuesAsFailures` | `true` | Any typed metadata issue independently fails the comparison, regardless of the difference threshold. |
| `Include` | `null` | Selected compiled-model database table/view names; null or empty compares all objects. |
| `CommandTimeout` | `null` | Timeout for each metadata command. Null retains the native default; zero is unlimited. Positive durations round up to whole seconds. |
| `MetadataReaderLog` | `null` | Optional synchronous diagnostic callback. Callback exceptions propagate. |

Negative command timeouts and values greater than 2,147,483 seconds are rejected before metadata I/O. A command timeout is neither a connection-open timeout nor an overall deadline. Use the cancellation token for the operation deadline; owned metadata work and cleanup settle before cancellation propagates.

MySQL/MariaDB Include names must exactly match the compiled model spelling, even on case-insensitive servers. SQLite selection is case-insensitive. Unknown/blank names are rejected before validation metadata I/O and duplicates are collapsed. Include limits comparison, not metadata acquisition. An extra live object outside the selected scope is excluded; a relation to an excluded parent still retains its endpoint identity without adding that parent to the counts. `ModelTableCount` and `DatabaseTableCount` count comparable tables/views in the selected scope.

The result preserves every difference and issue, including informational findings. `HasDifferences` means the difference list is nonempty; `HasFailures` applies the captured threshold and issue policy. A warning can therefore remain visible without failing, and an issue can fail even when there are no differences. Collections and issue-context messages are snapshots; references to finalized metadata in differences are retained rather than deep-cloned.

## Require validation before host startup

Attach validation to an existing [DataLinq registration](Dependency%20Injection%20and%20Unit%20of%20Work.md):

```csharp
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Extensions.Hosting;
using DataLinq.SQLite.Extensions.DependencyInjection;

builder.Services.AddDataLinq<Employees>()
    .UseSQLite("Data Source=employees.db")
    .ValidateSchemaOnStartup(options =>
    {
        options.Mode = DataLinqSchemaStartupMode.FailStartup;
        options.Validation.CommandTimeout = TimeSpan.FromSeconds(30);
    });
```

The attachment also works before provider selection and on MySQL/MariaDB typed builders. Alternatively, select targets centrally and configure common defaults:

```csharp
builder.Services.AddDataLinq<Employees>("primary")
    .UseSQLiteConnectionString("Employees")
    .AsDefault();

builder.Services.AddDataLinqSchemaValidation(defaults =>
    {
        defaults.Validation.CommandTimeout = TimeSpan.FromSeconds(30);
    })
    .ValidateDatabase<Employees>("primary", configure: options =>
    {
        options.Validation.Include = ["employees"];
    });
```

Connection names resolve through the application's `IConfiguration`. Central selection requires an existing registration; it does not construct a second provider. Omitted/null keys select the unnamed/default registration, while a supplied key selects that exact registration. For an unkeyed override, use `ValidateDatabase<Employees>(configure: options => ...)`: the first parameter is the key.

Explicit selections are required when validation is enabled; adding defaults does not implicitly select every database. A keyed registration exposed through `AsDefault()` is one canonical target, even if selected through both aliases. Repeated selections without another configuration callback are idempotent. A second explicit configuration for the same target is rejected. Separate registrations remain separate targets even if they refer to the same provider instance.

At startup, defaults callbacks run in registration order. Each target receives a copy, then its override runs once. Target selection and option preflight precede activation; checks that need the compiled model follow that target's activation. Separate service containers capture independently. Global Disabled skips target callbacks and provider activation; it cannot be overridden by a target. Per-target Disabled skips that target's activation. This keeps disabled targets from resolving connection names or invoking provider factories.

| Mode | Completed result fails policy | Operational read/activation failure |
| --- | --- | --- |
| `FailStartup` (default) | Prevents startup | Prevents startup |
| `WarningOnly` | Logs findings and continues | Prevents startup |
| `Disabled` | No validation | No validation activation |

WarningOnly does not change the result's `HasFailures` value and does not soften invalid configuration, callback/logger exceptions, connection errors or timeouts. Independent enabled targets are observed sequentially in first-selection order. Completed policy failures and ordinary operational failures allow later independent targets to settle before startup is rejected. Configuration, application callback, logger and orchestration failures stop scheduling. Cancellation stops later targets and propagates after owned work settles.

## Migration ordering and the startup barrier

The adapter uses `IHostedLifecycleService.StartingAsync`. Generic Host finishes that phase before invoking ordinary hosted services' `StartAsync`, including when `HostOptions.ServicesStartConcurrently=true`. Validation therefore precedes ordinary workers and the ASP.NET Core listener regardless of their registration order.

Complete application-owned migrations **before calling `StartAsync` or `RunAsync`**. For example:

```csharp
using var host = builder.Build();

// Application-owned migration code. Await it before entering host startup.
await ApplyApplicationMigrationsAsync(host.Services, cancellationToken);

await host.StartAsync(cancellationToken);
await host.WaitForShutdownAsync(cancellationToken);
```

`ApplyApplicationMigrationsAsync` is your application method, not a DataLinq API. A migration in an ordinary hosted service's `StartAsync` runs too late for this barrier. Service constructors and application code executed before host startup are outside the barrier. Other lifecycle services can also perform their own work during `StartingAsync`; do not rely on this adapter to sequence those effects. The original DI ownership rules still govern provider/root disposal after either successful or failed startup.

## Inspect failures and diagnostics

A connection, timeout, unsupported-provider or incomplete metadata read throws without fabricating a comparison result. When one original exception represents the read failure, it is preserved. More complex failure trees use `DataLinqSchemaReadException`, whose `Failure`, ordered `Exceptions` and ordinary inner-exception chain retain original causes and context. `DataLinq.Diagnostics.DataLinqFailure.GetContext(exception)` exposes execution and secondary cleanup evidence where available.

Host rejection uses `DataLinqSchemaStartupException`. Its ordered `Outcomes` include settled successful comparisons as well as failures. Each outcome has a canonical `Target` (model, key, ordinal and display name), captured `Mode`, `PreventsStartup`, and exactly one of `Result` or `OperationalFailure`. `RunFailure` identifies a configuration/callback/logger/orchestration failure that stopped the run; original causes also remain in the inner-exception chain. Cancellation remains an `OperationCanceledException` rather than a startup-result exception.

The host logs summaries, differences and typed issues with target identity and original messages. WarningOnly preserves original finding severities; it does not relabel errors as warnings. Metadata callbacks and exception context likewise retain original provider text. Custom keys are identified without invoking arbitrary `ToString()` implementations.

## What the comparison can establish

MySQL/MariaDB table/database identity follows the observed server `lower_case_table_names` setting: mode 0 is case-sensitive, modes 1/2 are insensitive. Column, index and constraint identifiers use their respective rules. Defaults, comments and SQL literals retain case-sensitive content. Relevant ambiguous model identities fail as configuration errors; inconsistent live identities fail operationally. The client operating system and data collation do not substitute for the server setting. See the [MySQL](https://dev.mysql.com/doc/refman/8.4/en/identifier-case-sensitivity.html) and [MariaDB](https://mariadb.com/docs/server/reference/sql-structure/sql-language-structure/identifier-case-sensitivity) identifier documentation.

Provider capabilities still bound the comparison. SQLite does not compare column order, CHECK constraints or comments; numeric type-size/sign decorations are ignored while declared type names remain significant. MySQL/MariaDB CHECK and default SQL expressions are compared conservatively as text, not general SQL equivalence. Runtime comparison preserves quoted contents and identifier quoting, folds whitespace outside quotes, and removes complete redundant enclosing parentheses from CHECK expressions. Expressions containing comments or backslashes retain their trimmed text. Server rewriting can still produce textual differences for semantically equivalent SQL.

Known metadata limitations become typed issues: examples include generated/hidden columns, partial/expression/descending or otherwise unsupported indexes, unrepresentable defaults and relationship endpoints, and SQLite view nullability. An issue suppresses only the associated claim that cannot be proved. It cannot hide unrelated missing objects or a known foreign-key endpoint/action disagreement. SQLite cannot reliably report view-column nullability, so a view can produce an issue even when other properties match. With the default issue policy, that result fails. Setting `TreatValidationIssuesAsFailures=false` accepts this uncertainty while retaining the issues; it does not establish full schema equivalence.

An incomplete catalog read is an operational failure, not a successful result with omissions. A valid existing empty database can be compared and report missing model tables; a missing SQLite file is not created. Named and anonymous SQLite memory databases preserve the original provider's identity and keeper connection. Validation is an observation across multiple metadata commands, not an atomic snapshot under concurrent DDL, and cannot detect objects hidden from the connection's metadata permissions.

The separate read-only `MemoryDatabase<T>` has no SQL schema-validation surface. Custom/legacy providers without the internal runtime metadata capability are unsupported; implementing the old import methods does not opt a provider into this runtime contract.
