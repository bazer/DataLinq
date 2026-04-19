using System;
using System.Collections.Generic;

namespace DataLinq.DevTools;

public enum DotnetCommandType
{
    Restore,
    Build,
    Test,
    Info,
    Exec
}

public enum DotnetOutputMode
{
    Quiet,
    Summary,
    Errors,
    Failures,
    Raw,
    Diagnostic
}

public enum DotnetFailureCategory
{
    None,
    SdkResolver,
    NugetConfigAccess,
    NugetSourceAccess,
    MissingPackages,
    Compiler,
    TestFailures,
    Unknown
}

public enum DotnetDiagnosticKind
{
    Error,
    Warning
}

public sealed record DotnetDiagnostic(
    DotnetDiagnosticKind Kind,
    string? Code,
    string Message,
    IReadOnlyList<string> Projects,
    int Count);

public sealed record DotnetCommandAnalysis(
    int DistinctErrorCount,
    int DistinctWarningCount,
    IReadOnlyList<DotnetDiagnostic> Errors,
    IReadOnlyList<DotnetDiagnostic> Warnings,
    IReadOnlyList<string> FailureDetails,
    DotnetFailureCategory FailureCategory,
    string? FailureSummary);

public sealed record DotnetCommandResult(
    DotnetCommandType CommandType,
    string DisplayTarget,
    IReadOnlyList<string> Arguments,
    ExternalCommandResult ProcessResult,
    string RawLogPath,
    string? BinaryLogPath,
    DotnetCommandAnalysis Analysis);
