> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: CLI Diagnostics Output Style

**Status:** Implemented for V1; remaining work is optional color flags and deeper structured-warning cleanup.
**Goal:** Make DataLinq CLI errors and warnings readable, consistent, colorized, and portable across Windows, Linux, and macOS.

## Executive Position

The CLI diagnostic system now has the text shape it should have had from the start.

Before this slice, output mixed:

- `Error:` prefixes
- `Warning:` prefixes
- old-style capitalized diagnostic prefixes
- validation drift lines with bracketed safety tags
- warning strings passed through generic log callbacks

The current CLI-facing output uses one human diagnostic style and routes operational errors and warnings to stderr. Some lower-level APIs still pass legacy strings such as `Warning: ...` through `Action<string>` log callbacks, but the CLI compatibility bridge renders those as `warning: ...`.

Default text shape:

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

Implemented since the original draft:

- CLI failures are flattened into `DataLinqDiagnosticIssue` before formatting.
- Human failure text no longer uses `[InvalidModel]` square brackets around failure types.
- Human failure text now uses `error InvalidModel: ...` / `warning: ...`.
- Many formerly ad hoc command/parser failures now go through `ConsoleDiagnosticWriter.WriteError(...)`.
- `ConsoleDiagnosticWriter` has explicit `WriteError` and `WriteWarning` overloads that accept a code.
- Diagnostics write to stderr through `Console.Error`.
- Color auto-detection for diagnostics uses `Console.IsErrorRedirected`.
- `DLFailureType.Unspecified` is omitted from human diagnostic text.
- Validation drift text uses severity labels and `safety:` lines instead of bracketed safety tags.
- `ConsoleDiagnosticWriter.WriteLogLine(...)` keeps a compatibility bridge for legacy `Error:` / `Warning:` strings and renders them in the new style.
- Secret redaction is integrated into diagnostic formatting.
- JSON validation output redacts known secret values.

Still not implemented, intentionally:

- No explicit `--color auto|always|never` option exists yet.
- Some lower-level tooling warnings still encode severity in strings such as `Warning: ...`; prefix parsing remains a compatibility bridge until those APIs accept structured diagnostics.

### ConsoleDiagnosticWriter

`src/DataLinq.CLI/ConsoleDiagnosticWriter.cs` currently has:

```csharp
private const string ErrorSeverity = "error";
private const string WarningSeverity = "warning";
private const string LegacyErrorPrefix = "Error:";
private const string LegacyWarningPrefix = "Warning:";
private const ConsoleColor ErrorColor = ConsoleColor.Red;
private const ConsoleColor WarningColor = ConsoleColor.Yellow;
```

It colors severity labels when stderr is not redirected:

```csharp
Console.ForegroundColor = color;
Console.Error.Write(severity);
```

This is good because `Console.ForegroundColor` is supported across normal Windows, Linux, and macOS terminals. It also avoids emitting raw ANSI escapes into redirected output.

Issue formatting now produces:

```csharp
error InvalidModel: Broken model
```

and with source locations:

```csharp
Account.cs:2:1: error InvalidModel: Broken property
```

The tests in `src/DataLinq.Tests.Unit/CliDiagnosticWriterTests.cs` lock this format in.

### Failure Formatting

`src/DataLinq.SharedCore/ErrorHandling/DLOptionFailure.cs` still formats failures directly as:

```csharp
$"[{FailureType}] {Failure}"
```

The CLI mostly avoids using raw `IDLOptionFailure.ToString()` for human output now. Keep that rule: flatten failures into `DataLinqDiagnosticIssue` and apply the CLI's diagnostic format.

`IDLOptionFailure.ToString()` can remain as a low-level/debug representation if changing it would be too disruptive.

### Ad Hoc Console Errors

Most operational command errors in `src/DataLinq.CLI/Program.cs` now go through the diagnostic writer. Continue converting any new parser/config/secret failures through the renderer rather than adding raw `Console.WriteLine` errors.

### Warnings From Tools

Some warnings are emitted as strings through generic log callbacks:

```csharp
log($"Warning: Foreign key ...");
Log?.Invoke($"Warning: Could not read enum context file ...");
```

`ConsoleDiagnosticWriter.WriteLogLine(...)` detects `Warning:` and `Error:` as legacy prefixes and routes them through the new stderr diagnostic renderer. That bridge is useful, but weak. The long-term shape should be structured warning calls, not severity encoded in strings.

### Validation Drift Output

`validate` now reports drift as:

```text
warning MissingColumn: table.column
  safety: Additive
  Column 'name' exists in model but not database.
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

Schema drift differences are not the same as operational diagnostics, but they now use the same no-brackets text vocabulary:

```text
warning MissingColumn: employee.name
  safety: Additive
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

## Implemented V1 Plan

### 1. Introduce a Diagnostic Renderer

`ConsoleDiagnosticWriter` now centralizes formatting, color, stream choice, and redaction. The implementation reuses `DataLinqDiagnosticIssue` directly rather than introducing another public diagnostic record:

```csharp
ConsoleDiagnosticWriter.FormatIssuesText(...);
ConsoleDiagnosticWriter.WriteIssues(...);
```

The important result is in place:

- formatting is centralized
- color is centralized
- stream choice is centralized
- tests assert one format
- `FormatIssuesText(...)` and `WriteIssues(...)` use the same renderer
- redaction remains one layer, not scattered call-site string replacement

### 2. Remove Bracketed Failure Type Formatting From CLI Output

CLI issue text changed from:

```text
Error: InvalidModel: Broken model
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

Meaningless codes are omitted:

```text
error: Couldn't find database with name 'Foo'.
```

not:

```text
error Unspecified: Couldn't find database with name 'Foo'.
```

### 3. Add Warning Writer APIs

Implemented APIs:

```csharp
ConsoleDiagnosticWriter.WriteWarning(string message)
ConsoleDiagnosticWriter.WriteWarning(string? code, string message)
ConsoleDiagnosticWriter.WriteError(string message)
ConsoleDiagnosticWriter.WriteError(string? code, string message)
```

Lower-level log-string warnings can be replaced over time:

```csharp
log("Warning: ...")
```

with structured warning calls where the API boundary allows it.

For lower-level APIs that still only accept `Action<string> log`, prefix parsing remains as a compatibility bridge.

### 4. Route Diagnostics to stderr

Diagnostic writes now go to `Console.Error`.

Color detection should check the target stream:

- `Console.IsErrorRedirected` for diagnostics
- `Console.IsOutputRedirected` for stdout logs if any remain

Keep JSON output on stdout.

Command tests now capture stdout and stderr separately where the distinction matters.

### 5. Normalize Ad Hoc CLI Errors

Ad hoc errors in `Program.cs` should continue to use:

```csharp
Console.WriteLine("Invalid output format. Expected 'text' or 'json'.");
```

with:

```csharp
ConsoleDiagnosticWriter.WriteError("InvalidArgument", "Invalid output format. Expected 'text' or 'json'.");
```

Parser fallback usage text can remain ordinary help text when appropriate.

### 6. Improve Validation Drift Text

`WriteValidationText(...)` no longer emits bracketed safety tags.

Use severity labels:

```text
error ColumnTypeMismatch: path
  safety: Ambiguous
  message
```

Final text for a mixed drift report:

```text
Schema drift detected: 2 differences (1 error, 1 warning).

error ColumnTypeMismatch: employee.birth_date
  safety: Ambiguous
  Model type 'date' does not match database type 'datetime'.

warning MissingColumn: employee.name
  safety: Additive
  Column 'name' exists in model but not database.
```

### 7. Keep Cross-Platform Color Boring

Use `Console.ForegroundColor`.

Do not introduce a Spectre.Console dependency into `DataLinq.CLI` just for red/yellow diagnostics. The other repo CLIs use Spectre, but this feature does not need it.

Tests cover formatted text and stream routing, not terminal color behavior.

### 8. Tests

Implemented tests cover:

- error without location: `error InvalidModel: Broken model`
- error with location: `Account.cs:2:1: error InvalidModel: Broken property`
- unspecified failure omits code: `error: message`
- aggregate failures flatten into one diagnostic per line
- context lines are indented
- warnings format as `warning: message` or `warning Code: message`
- no square brackets in text diagnostics
- validation drift output uses no bracketed safety tags
- diagnostics are written to stderr
- command-level tests keep normal stdout separate while diagnostics land on stderr

### 9. Remaining Follow-Up

Optional future work:

1. Add `--color auto|always|never` if users need explicit color control.
2. Convert lower-level `Action<string>` warning logs to structured warning calls where the API boundary allows it.
3. Add more command-level stdout/stderr assertions around generated SQL and JSON if those surfaces grow.

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
