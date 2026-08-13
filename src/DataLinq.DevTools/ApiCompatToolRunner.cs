using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DataLinq.DevTools;

internal sealed record ApiCompatToolExecution(
    string Name,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int? ExitCode,
    double DurationSeconds,
    string StandardOutputPath,
    string StandardErrorPath,
    string? SuppressionPath,
    IReadOnlyList<ApiCompatSuppressionDiagnostic> Diagnostics,
    bool Succeeded,
    string? Failure);

internal interface IApiCompatProcessRunner
{
    ExternalCommandResult Execute(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environmentVariables);
}

internal sealed class ApiCompatExternalProcessRunner : IApiCompatProcessRunner
{
    public ExternalCommandResult Execute(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environmentVariables) =>
        ExternalProcessRunner.Execute(fileName, arguments, workingDirectory, environmentVariables);
}

internal sealed class ApiCompatToolRunner
{
    internal const string ExpectedToolVersion = "10.0.302";

    private readonly DevToolPaths paths;
    private readonly ToolingProfile profile;
    private readonly string manifestPath;
    private readonly string evidenceDirectory;
    private readonly IApiCompatProcessRunner processRunner;
    private string? toolVersion;

    public ApiCompatToolRunner(
        DevToolPaths paths,
        ToolingProfile profile,
        string manifestPath,
        string evidenceDirectory,
        IApiCompatProcessRunner? processRunner = null)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.profile = profile;
        this.manifestPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(manifestPath)
                ? throw new ArgumentException("The ApiCompat tool manifest path must not be blank.", nameof(manifestPath))
                : manifestPath);
        this.evidenceDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(evidenceDirectory)
                ? throw new ArgumentException("The ApiCompat evidence directory must not be blank.", nameof(evidenceDirectory))
                : evidenceDirectory);
        this.processRunner = processRunner ?? new ApiCompatExternalProcessRunner();
    }

    public string ToolVersion => toolVersion
        ?? throw new InvalidOperationException("ApiCompat tool version has not been verified.");

    public ApiCompatToolExecution VerifyTool()
    {
        var execution = Execute("tool-version", ["--version"], writeSuppression: false);
        if (!execution.Succeeded)
            return execution;

        var version = File.ReadAllText(execution.StandardOutputPath, Encoding.UTF8).Trim();
        if (!HasExpectedVersion(version))
        {
            return execution with
            {
                Succeeded = false,
                Failure = $"ApiCompat reported version '{version}', expected pinned version '{ExpectedToolVersion}'."
            };
        }

        toolVersion = version;
        return execution;
    }

    public ApiCompatToolExecution ComparePackages(
        string name,
        string baselinePackagePath,
        string currentPackagePath,
        bool strictBaseline)
    {
        EnsureToolVerified();
        var arguments = new List<string>
        {
            "package",
            Path.GetFullPath(currentPackagePath),
            "--baseline-package",
            Path.GetFullPath(baselinePackagePath),
            "--run-api-compat",
            "--enable-strict-mode-for-compatible-tfms",
            "--enable-strict-mode-for-compatible-frameworks-in-package",
            "--enable-rule-cannot-change-parameter-name",
            "--enable-rule-attributes-must-match"
        };
        if (strictBaseline)
            arguments.Add("--enable-strict-mode-for-baseline-validation");
        return Execute(name, arguments, writeSuppression: true);
    }

    public ApiCompatToolExecution ValidatePackage(string name, string packagePath)
    {
        EnsureToolVerified();
        return Execute(
            name,
            [
                "package",
                Path.GetFullPath(packagePath),
                "--run-api-compat",
                "--enable-strict-mode-for-compatible-tfms",
                "--enable-strict-mode-for-compatible-frameworks-in-package",
                "--enable-rule-cannot-change-parameter-name",
                "--enable-rule-attributes-must-match"
            ],
            writeSuppression: true);
    }

    public ApiCompatToolExecution CompareAssemblies(
        string name,
        string baselineAssemblyPath,
        string currentAssemblyPath,
        bool strict,
        string? baselineReferenceDirectory = null,
        string? currentReferenceDirectory = null)
    {
        EnsureToolVerified();
        var arguments = new List<string>
        {
            "--left",
            Path.GetFullPath(baselineAssemblyPath),
            "--right",
            Path.GetFullPath(currentAssemblyPath),
            "--enable-rule-cannot-change-parameter-name",
            "--enable-rule-attributes-must-match"
        };
        if (baselineReferenceDirectory is not null)
        {
            arguments.Add("--left-assembly-references");
            arguments.Add(Path.GetFullPath(baselineReferenceDirectory));
        }
        if (currentReferenceDirectory is not null)
        {
            arguments.Add("--right-assembly-references");
            arguments.Add(Path.GetFullPath(currentReferenceDirectory));
        }
        if (strict)
            arguments.Add("--strict-mode");
        return Execute(name, arguments, writeSuppression: true);
    }

    private ApiCompatToolExecution Execute(
        string name,
        IReadOnlyList<string> toolArguments,
        bool writeSuppression)
    {
        ValidateEvidenceName(name);
        if (!File.Exists(manifestPath))
        {
            return CreatePreflightFailure(
                name,
                toolArguments,
                $"Pinned ApiCompat tool manifest '{manifestPath}' does not exist.");
        }
        var expectedManifestPath = Path.GetFullPath(
            Path.Combine(paths.RepositoryRoot, ".config", "dotnet-tools.json"));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!manifestPath.Equals(expectedManifestPath, pathComparison))
        {
            return CreatePreflightFailure(
                name,
                toolArguments,
                $"ApiCompat must use the repository-local manifest '{expectedManifestPath}', found '{manifestPath}'.");
        }

        paths.EnsureCreated();
        Directory.CreateDirectory(evidenceDirectory);
        var stdoutPath = Path.Combine(evidenceDirectory, $"{name}.stdout.log");
        var stderrPath = Path.Combine(evidenceDirectory, $"{name}.stderr.log");
        var suppressionPath = writeSuppression
            ? Path.Combine(evidenceDirectory, $"{name}.xml")
            : null;
        RefuseExistingEvidence(stdoutPath, stderrPath, suppressionPath);

        var arguments = new List<string>
        {
            "tool",
            "run",
            "apicompat",
            "--"
        };
        arguments.AddRange(toolArguments);
        if (suppressionPath is not null)
        {
            arguments.Add("--generate-suppression-file");
            arguments.Add("--suppression-output-file");
            arguments.Add(suppressionPath);
            arguments.Add("--verbosity");
            arguments.Add("low");
        }

        ExternalCommandResult? result = null;
        string? failure = null;
        IReadOnlyList<ApiCompatSuppressionDiagnostic> diagnostics = [];
        try
        {
            result = processRunner.Execute(
                "dotnet",
                arguments,
                paths.RepositoryRoot,
                CreateToolEnvironment());
            File.WriteAllText(stdoutPath, result.StandardOutput, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(stderrPath, result.StandardError, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (result.ExitCode != 0)
            {
                failure = $"ApiCompat process exited with code {result.ExitCode}.";
            }
            else if (suppressionPath is not null && File.Exists(suppressionPath))
            {
                diagnostics = ApiCompatSuppressionParser.ParseFile(suppressionPath);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException)
        {
            failure = exception.Message;
            if (!File.Exists(stdoutPath))
                File.WriteAllText(stdoutPath, string.Empty, new UTF8Encoding(false));
            if (!File.Exists(stderrPath))
                File.WriteAllText(stderrPath, exception.ToString(), new UTF8Encoding(false));
        }

        return new ApiCompatToolExecution(
            name,
            Array.AsReadOnly(arguments.ToArray()),
            paths.RepositoryRoot,
            result?.ExitCode,
            result?.Duration.TotalSeconds ?? 0,
            stdoutPath,
            stderrPath,
            suppressionPath is not null && File.Exists(suppressionPath) ? suppressionPath : null,
            diagnostics,
            failure is null,
            failure);
    }

    private ApiCompatToolExecution CreatePreflightFailure(
        string name,
        IReadOnlyList<string> arguments,
        string failure)
    {
        Directory.CreateDirectory(evidenceDirectory);
        var stdoutPath = Path.Combine(evidenceDirectory, $"{name}.stdout.log");
        var stderrPath = Path.Combine(evidenceDirectory, $"{name}.stderr.log");
        RefuseExistingEvidence(stdoutPath, stderrPath, null);
        File.WriteAllText(stdoutPath, string.Empty, new UTF8Encoding(false));
        File.WriteAllText(stderrPath, failure, new UTF8Encoding(false));
        return new ApiCompatToolExecution(
            name,
            Array.AsReadOnly(arguments.ToArray()),
            paths.RepositoryRoot,
            null,
            0,
            stdoutPath,
            stderrPath,
            null,
            [],
            false,
            failure);
    }

    private void EnsureToolVerified()
    {
        if (toolVersion is null)
            throw new InvalidOperationException("VerifyTool must succeed before running ApiCompat comparisons.");
    }

    private static bool HasExpectedVersion(string value) =>
        value.Equals(ExpectedToolVersion, StringComparison.Ordinal) ||
        value.StartsWith(ExpectedToolVersion + "+", StringComparison.Ordinal);

    private IReadOnlyDictionary<string, string?> CreateToolEnvironment() =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "0",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_CLI_UI_LANGUAGE"] = "en",
            ["DOTNET_DISABLE_GUI_ERRORS"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DATALINQ_DEV_PROFILE"] = profile.ToCliValue()
        };

    private static void ValidateEvidenceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
            name.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
        {
            throw new ArgumentException(
                "ApiCompat evidence names must contain only lowercase ASCII letters, digits, and hyphens.",
                nameof(name));
        }
    }

    private static void RefuseExistingEvidence(
        string stdoutPath,
        string stderrPath,
        string? suppressionPath)
    {
        var existing = new[] { stdoutPath, stderrPath, suppressionPath }
            .Where(static path => path is not null && File.Exists(path))
            .FirstOrDefault();
        if (existing is not null)
            throw new InvalidOperationException($"ApiCompat evidence file '{existing}' already exists; evidence is append-never.");
    }
}
