> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: CLI Diagnostics Output Style

**Status:** Draft implementation plan.
**Goal:** Make DataLinq CLI errors and warnings readable, consistent, colorized, and portable across Windows, Linux, and macOS.

## Executive Position

The CLI already has the beginning of a diagnostic system, but the text shape is not good enough yet.

Current output mixes:

- `Error:` prefixes
- `Warning:` prefixes
- bracketed failure types such as `[InvalidModel]`
- unbracketed ad hoc errors
- validation drift lines with bracketed safety tags
- warning strings passed through generic log callbacks

That is readable enough for a developer who already knows the tool. It is not polished. The CLI should have one human diagnostic style and route every error/warning through it.

Recommended default text shape:

```text
error InvalidModel: Model 'Employee' does not inherit from 'ITableModel' or 'IViewModel'.
warning: Foreign key 'FK_orders_customer' generated fallback relation property 'Customer1'.
```

With source location:

```text
Models/Employee.cs:18:1: error InvalidModel: Model 'Employee' does not inherit from 'ITableModel' or 'IViewModel'.
```

No square brackets. The severity and optional code are enough.

Colors:

- `error` in red
- `warning` in yellow
- no color when output is redirected unless an explicit future color option asks for it

## Current Code Audit

### ConsoleDiagnosticWriter

`src/DataLinq.CLI/ConsoleDiagnosticWriter.cs` currently has:

```csharp
private const string ErrorPrefix = "Error:";
private const string WarningPrefix = "Warning:";
private const ConsoleColor ErrorColor = ConsoleColor.Red;
private const ConsoleColor WarningColor = ConsoleColor.Yellow;
```

It already colors recognized prefixes when output is not redirected:

```csharp
Console.ForegroundColor = color;
Console.Write(prefix);
```

This is good because `Console.ForegroundColor` is supported across normal Windows, Linux, and macOS terminals. It also avoids emitting raw ANSI escapes into redirected output.

But issue formatting currently uses square brackets:

```csharp
$"[{issue.FailureType}] {issue.Message}"
```

and with source locations:

```csharp
$"{location}: [{issue.FailureType}] {issue.Message}"
```

The tests in `src/DataLinq.Tests.Unit/CliDiagnosticWriterTests.cs` lock this in today.

### Failure Formatting

`src/DataLinq.SharedCore/ErrorHandling/DLOptionFailure.cs` also formats failures directly as:

```csharp
$"[{FailureType}] {Failure}"
```

The CLI should avoid using raw `IDLOptionFailure.ToString()` for human output. It should flatten failures into `DataLinqDiagnosticIssue` and apply the CLI's diagnostic format.

`IDLOptionFailure.ToString()` can remain as a low-level/debug representation if changing it would be too disruptive.

### Ad Hoc Console Errors

`src/DataLinq.CLI/Program.cs` has ad hoc output such as:

```csharp
Console.WriteLine("Invalid output format. Expected 'text' or 'json'.");
Console.WriteLine($"Usage: datalinq [command] -n name");
```

Those should go through the diagnostic writer.

### Warnings From Tools

Some warnings are emitted as strings through generic log callbacks:

```csharp
log($"Warning: Foreign key ...");
Log?.Invoke($"Warning: Could not read enum context file ...");
```

`ConsoleDiagnosticWriter.WriteLogLine(...)` currently detects `Warning:` and colors the prefix. That is useful, but weak. The long-term shape should be structured warning calls, not severity encoded in strings.

### Validation Drift Output

`validate` currently reports drift as:

```text
- MissingColumn [Safe] table.column
  message
```

This is not the same as CLI errors/warnings. Schema drift differences are command results, not necessarily operational diagnostics. Still, if a drift difference has severity `Error` or `Warning`, it should visually align with the diagnostic style where reasonable.

## Desired Human Format

### Error Without Location

```text
error InvalidModel: Model 'Employee' does not inherit from 'ITableModel' or 'IViewModel'.
```

### Error With Location

```text
Models/Employee.cs:18:1: error InvalidModel: Model 'Employee' does not inherit from 'ITableModel' or 'IViewModel'.
```

### Warning Without Code

```text
warning: Could not read enum context file 'Models/OrderStatus.cs': file not found.
```

### Warning With Code

```text
warning RelationNameFallback: Foreign key 'FK_orders_customer' generated fallback relation property 'Customer1'.
```

### Context Lines

Context should be indented and uncolored:

```text
Models/Employee.cs:18:1: error InvalidModel: Property 'Name' has duplicate column metadata.
  context: Parsing properties for Employee
  context: Parsing models from Models/Employee.cs
```

### Multiple Diagnostics

Each diagnostic should stand on its own line:

```text
Models/Employee.cs:18:1: error InvalidModel: Property 'Name' has duplicate column metadata.
Models/Department.cs:9:1: error InvalidModel: Table property 'Employees' references missing model 'Employee'.
```

Do not print a giant `Error:` prefix before every continuation line. Continuation lines should be visually subordinate.

## Color Policy

### Default

Default color mode should be `auto`:

- color diagnostic severity labels when writing to an interactive terminal
- do not color when output is redirected
- do not color JSON output

Use `Console.ForegroundColor` for portability.

### Streams

Diagnostics should be written to stderr:

- errors
- warnings
- validation/config failures
- deprecation warnings

Normal command output should stay on stdout:

- generated SQL script text
- JSON output
- list output
- success summaries

This matters for scripting. A user should be able to do:

```bash
datalinq diff > drift.sql
```

without error text being mixed into the SQL file.

### Explicit Color Control

V1 can keep only auto color if that keeps scope tight, but the cleaner long-term option is:

```text
--color auto|always|never
```

If implemented:

- default: `auto`
- respect `NO_COLOR` by treating default auto as never
- explicit `--color always` overrides redirection and `NO_COLOR`
- explicit `--color never` disables color everywhere

Do not block the basic formatting work on adding the option.

## Severity and Code

Use:

```text
error <code>: <message>
warning <code>: <message>
```

when a code exists.

Use:

```text
error: <message>
warning: <message>
```

when no code exists.

For existing `DataLinqDiagnosticIssue`, use `FailureType` as the code when it is meaningful:

- `InvalidModel`
- `InvalidArgument`
- `FileNotFound`
- `Exception`

If `FailureType` is `Unspecified`, omit the code:

```text
error: Couldn't find database with name 'Foo'.
```

This avoids noisy output like:

```text
error Unspecified: Couldn't find database with name 'Foo'.
```

## Validation Drift Formatting

Schema drift differences are not the same as operational diagnostics, but they should become cleaner too.

Current:

```text
- MissingColumn [Safe] employee.name
  Column 'name' exists in model but not database.
```

Recommended:

```text
warning MissingColumn: employee.name
  safety: Safe
  Column 'name' exists in model but not database.
```

or for error-level drift:

```text
error ColumnTypeMismatch: employee.birth_date
  safety: ReviewRequired
  Model type 'date' does not match database type 'datetime'.
```

This keeps the same no-brackets rule.

Whether drift output is written to stdout or stderr is a product decision. My recommendation:

- validation result summaries and drift details go to stdout
- operational failures go to stderr

That lets `validate` remain a reporting command while keeping actual command failures separate.

## JSON Output

JSON output must not contain color escape sequences.

Do not rename JSON fields solely for prettier text output. Existing machine-readable fields such as:

```json
"failureType": "InvalidModel"
```

can remain as-is.

Text style changes should not break JSON consumers.

## Implementation Plan

### 1. Introduce a Diagnostic Renderer

Refactor `ConsoleDiagnosticWriter` around a renderer that takes structured values:

```csharp
internal sealed record CliDiagnostic(
    DataLinqDiagnosticSeverity Severity,
    string? Code,
    string Message,
    string? Location = null,
    IReadOnlyList<string>? Context = null);
```

or reuse `DataLinqDiagnosticIssue` directly and add formatting helpers.

The important result:

- formatting is centralized
- color is centralized
- stream choice is centralized
- tests assert one format

### 2. Remove Bracketed Failure Type Formatting From CLI Output

Change CLI issue text from:

```text
Error: [InvalidModel] Broken model
```

to:

```text
error InvalidModel: Broken model
```

With location:

```text
Account.cs:2:1: error InvalidModel: Broken property
```

Update `CliDiagnosticWriterTests`.

Do not rely on `IDLOptionFailure.ToString()` for CLI-facing output.

### 3. Add Warning Writer APIs

Add:

```csharp
ConsoleDiagnosticWriter.WriteWarning(string message)
ConsoleDiagnosticWriter.WriteWarning(string? code, string message)
ConsoleDiagnosticWriter.WriteError(string message)
ConsoleDiagnosticWriter.WriteError(string? code, string message)
```

Then replace log-string warnings over time:

```csharp
log("Warning: ...")
```

with structured warning calls where the API boundary allows it.

If some lower-level APIs still only accept `Action<string> log`, keep prefix parsing as a compatibility bridge.

### 4. Route Diagnostics to stderr

Change diagnostic writes to `Console.Error`.

Color detection should check the target stream:

- `Console.IsErrorRedirected` for diagnostics
- `Console.IsOutputRedirected` for stdout logs if any remain

Keep JSON output on stdout.

### 5. Normalize Ad Hoc CLI Errors

Replace ad hoc errors in `Program.cs`:

```csharp
Console.WriteLine("Invalid output format. Expected 'text' or 'json'.");
```

with:

```csharp
ConsoleDiagnosticWriter.WriteError("InvalidArgument", "Invalid output format. Expected 'text' or 'json'.");
```

Replace parser fallback usage text with a diagnostic plus ordinary help text if needed.

### 6. Improve Validation Drift Text

Update `WriteValidationText(...)` to remove bracketed safety tags.

Use severity labels:

```text
error ColumnTypeMismatch: path
  safety: ReviewRequired
  message
```

Color `error` and `warning` labels if validation output remains on an interactive terminal.

### 7. Keep Cross-Platform Color Boring

Use `Console.ForegroundColor`.

Do not introduce a Spectre.Console dependency into `DataLinq.CLI` just for red/yellow diagnostics. The other repo CLIs use Spectre, but this feature does not need it.

Add tests around formatted text, not terminal color behavior. Color behavior can be covered by small unit tests for color policy decisions if needed.

### 8. Tests

Update and add tests for:

- error without location: `error InvalidModel: Broken model`
- error with location: `Account.cs:2:1: error InvalidModel: Broken property`
- unspecified failure omits code: `error: message`
- aggregate failures flatten into one diagnostic per line
- context lines are indented
- warnings format as `warning: message` or `warning Code: message`
- no square brackets in text diagnostics
- redirected output disables color under auto policy
- JSON validation output has no color escapes
- validation drift output uses no bracketed safety tags

## Non-Goals

- Do not redesign the full error model in this slice.
- Do not change JSON output fields unless necessary.
- Do not introduce Spectre.Console just for diagnostics.
- Do not emit ANSI escape sequences by default.
- Do not color output when redirected by default.
- Do not remove `IDLOptionFailure.ToString()` in this slice unless it is easy and safe.
- Do not make every lower-level warning structured before improving CLI output.

## Acceptance Criteria

- CLI text diagnostics use one format for errors and warnings.
- Error severity labels are red in interactive terminals.
- Warning severity labels are yellow in interactive terminals.
- No CLI human diagnostic output uses square brackets around failure types or safety tags.
- Diagnostics are written to stderr.
- Normal command output and JSON output remain on stdout.
- Color is disabled automatically for redirected output.
- The same behavior works on Windows, Linux, and macOS using standard .NET console APIs.
- Unit tests cover formatting for failures, warnings, locations, aggregate failures, and validation drift text.
