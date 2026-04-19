using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DataLinq.DevTools;

public sealed class DotnetCommandRunner
{
    private readonly DevToolPaths paths;
    private readonly ToolingProfile profile;

    public DotnetCommandRunner(DevToolPaths paths, ToolingProfile profile)
    {
        this.paths = paths;
        this.profile = profile;
    }

    public DotnetCommandResult Execute(
        DotnetCommandType commandType,
        IReadOnlyList<string> arguments,
        string artifactPrefix,
        string displayTarget,
        bool includeNoLogo = true,
        bool includeNuGetAuditProperty = true,
        bool includeOfflineRestoreProperty = true,
        bool generateBinaryLog = false,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? additionalEnvironmentVariables = null)
    {
        paths.EnsureCreated();

        var finalArguments = new List<string>(arguments);

        if (includeNoLogo && SupportsNoLogo(commandType) && !ContainsArgument(finalArguments, "-nologo"))
            finalArguments.Add("-nologo");

        if (includeNuGetAuditProperty && SupportsMsBuildProperties(commandType) && !ContainsProperty(finalArguments, "NuGetAudit"))
            finalArguments.Add("-p:NuGetAudit=false");

        if (includeOfflineRestoreProperty && profile.IsOffline() && SupportsMsBuildProperties(commandType) &&
            !ContainsProperty(finalArguments, "RestoreIgnoreFailedSources"))
        {
            finalArguments.Add("-p:RestoreIgnoreFailedSources=true");
        }

        string? binaryLogPath = null;
        if (generateBinaryLog && commandType == DotnetCommandType.Build && !ContainsBinaryLog(finalArguments))
        {
            binaryLogPath = CreateArtifactPath(artifactPrefix, "binlog");
            finalArguments.Add($"/bl:{binaryLogPath}");
        }

        var environmentVariables = new Dictionary<string, string?>(paths.CreateEnvironment(profile), StringComparer.OrdinalIgnoreCase);
        if (additionalEnvironmentVariables is not null)
        {
            foreach (var pair in additionalEnvironmentVariables)
                environmentVariables[pair.Key] = pair.Value;
        }

        var processResult = ExternalProcessRunner.Execute(
            "dotnet",
            finalArguments,
            workingDirectory ?? paths.RepositoryRoot,
            environmentVariables);

        var rawLogPath = WriteRawLog(artifactPrefix, processResult);
        var analysis = DotnetOutputAnalyzer.Analyze(commandType, processResult);

        return new DotnetCommandResult(
            CommandType: commandType,
            DisplayTarget: displayTarget,
            Arguments: finalArguments,
            ProcessResult: processResult,
            RawLogPath: rawLogPath,
            BinaryLogPath: binaryLogPath,
            Analysis: analysis);
    }

    private string WriteRawLog(string artifactPrefix, ExternalCommandResult result)
    {
        var path = CreateArtifactPath(artifactPrefix, "log");
        File.WriteAllText(path, string.Concat(result.StandardOutput, result.StandardError), Encoding.UTF8);
        return path;
    }

    private string CreateArtifactPath(string artifactPrefix, string extension)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        return Path.Combine(paths.ArtifactRoot, $"{artifactPrefix}-{timestamp}.{extension}");
    }

    private static bool SupportsMsBuildProperties(DotnetCommandType commandType) =>
        commandType is DotnetCommandType.Restore or DotnetCommandType.Build or DotnetCommandType.Test;

    private static bool SupportsNoLogo(DotnetCommandType commandType) =>
        commandType is DotnetCommandType.Restore or DotnetCommandType.Build or DotnetCommandType.Test;

    private static bool ContainsArgument(IReadOnlyList<string> arguments, string expected) =>
        arguments.Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsBinaryLog(IReadOnlyList<string> arguments) =>
        arguments.Any(argument =>
            argument.StartsWith("/bl", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-bl", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("--binarylogger", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsProperty(IReadOnlyList<string> arguments, string propertyName) =>
        arguments.Any(argument =>
            argument.StartsWith($"-p:{propertyName}=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith($"/p:{propertyName}=", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, $"--property:{propertyName}", StringComparison.OrdinalIgnoreCase));
}
