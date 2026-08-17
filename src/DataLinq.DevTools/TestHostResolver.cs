using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public sealed record TestHostResolution(
    string ProjectPath,
    string HostPath,
    string RuntimeConfigPath,
    string DependencyManifestPath,
    DateTimeOffset HostLastWriteUtc);

public static class TestHostResolver
{
    private static readonly string[] BuildInputExtensions =
    [
        ".cs",
        ".csproj",
        ".props",
        ".targets",
        ".resx",
        ".razor"
    ];

    public static TestHostResolution Resolve(
        string repositoryRoot,
        string projectPath,
        string configuration,
        bool requireCurrentOutput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        var fullProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullProjectPath))
            throw new FileNotFoundException($"The requested test project was not found: '{fullProjectPath}'.", fullProjectPath);

        var projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        var outputRoot = Path.Combine(projectDirectory, "bin", configuration);
        var projectName = Path.GetFileNameWithoutExtension(fullProjectPath);
        var candidates = FindCandidates(outputRoot, projectName);
        if (candidates.Count == 0)
        {
            throw new FileNotFoundException(
                $"No executable test host was found for '{fullProjectPath}' in configuration '{configuration}'. " +
                "Run without '--no-build' to build the test project once before execution.",
                Path.Combine(outputRoot, $"{projectName}.dll"));
        }

        var frameworks = ReadTargetFrameworks(fullProjectPath, repositoryRoot);
        var preferred = frameworks.Count == 0
            ? candidates
            : candidates
                .Where(candidate => frameworks.Contains(
                    Path.GetFileName(Path.GetDirectoryName(candidate.HostPath)),
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
        var selectedCandidates = preferred.Count > 0 ? preferred : candidates;
        if (selectedCandidates.Count != 1)
        {
            var paths = string.Join(Environment.NewLine, selectedCandidates.Select(static candidate => $"- {candidate.HostPath}"));
            throw new InvalidOperationException(
                $"Project '{fullProjectPath}' resolves to {selectedCandidates.Count} executable test hosts for " +
                $"configuration '{configuration}'. The Testing CLI requires exactly one target framework/runtime:{Environment.NewLine}{paths}");
        }

        var selected = selectedCandidates[0];
        var hostLastWriteUtc = File.GetLastWriteTimeUtc(selected.HostPath);
        if (requireCurrentOutput)
        {
            var hostDirectory = Path.GetDirectoryName(selected.HostPath)!;
            var newerInput = EnumerateProjectBuildInputs(fullProjectPath, repositoryRoot)
                .Select(input => new
                {
                    input.Path,
                    InputLastWriteUtc = File.GetLastWriteTimeUtc(input.Path),
                    OutputPath = ResolveProjectOutputPath(input.ProjectPath, fullProjectPath, selected.HostPath, hostDirectory)
                })
                .Select(input => new
                {
                    input.Path,
                    input.InputLastWriteUtc,
                    input.OutputPath,
                    OutputLastWriteUtc = File.GetLastWriteTimeUtc(input.OutputPath)
                })
                .Where(input => input.InputLastWriteUtc > input.OutputLastWriteUtc)
                .OrderByDescending(static input => input.InputLastWriteUtc)
                .FirstOrDefault();
            if (newerInput is not null)
            {
                throw new InvalidOperationException(
                    $"The prebuilt test graph is stale because '{newerInput.Path}' is newer than '{newerInput.OutputPath}'. " +
                    "Run without '--no-build' to rebuild the affected project graph.");
            }
        }

        return new TestHostResolution(
            fullProjectPath,
            selected.HostPath,
            selected.RuntimeConfigPath,
            selected.DependencyManifestPath,
            new DateTimeOffset(hostLastWriteUtc, TimeSpan.Zero));
    }

    private static IReadOnlyList<HostCandidate> FindCandidates(string outputRoot, string projectName)
    {
        if (!Directory.Exists(outputRoot))
            return Array.Empty<HostCandidate>();

        var runtimeConfigName = $"{projectName}.runtimeconfig.json";
        var candidates = new List<HostCandidate>();
        foreach (var runtimeConfigPath in Directory.EnumerateFiles(outputRoot, runtimeConfigName, SearchOption.AllDirectories))
        {
            if (HasDirectorySegment(runtimeConfigPath, "publish"))
                continue;

            var directory = Path.GetDirectoryName(runtimeConfigPath)!;
            var hostPath = Path.Combine(directory, $"{projectName}.dll");
            var dependencyManifestPath = Path.Combine(directory, $"{projectName}.deps.json");
            if (File.Exists(hostPath) && File.Exists(dependencyManifestPath))
                candidates.Add(new HostCandidate(hostPath, runtimeConfigPath, dependencyManifestPath));
        }

        return candidates
            .DistinctBy(static candidate => candidate.HostPath, PathComparer)
            .OrderBy(static candidate => candidate.HostPath, PathComparer)
            .ToArray();
    }

    private static IReadOnlySet<string> ReadTargetFrameworks(string projectPath, string repositoryRoot)
    {
        foreach (var path in EnumerateProjectAndAncestorProps(projectPath, repositoryRoot))
        {
            var document = TryLoadProject(path);
            if (document is null)
                continue;

            var value = document
                .Descendants()
                .Where(static element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .Select(static element => element.Value)
                .LastOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(value))
                continue;

            return value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveProjectOutputPath(
        string projectPath,
        string rootProjectPath,
        string hostPath,
        string hostDirectory)
    {
        if (PathComparer.Equals(projectPath, rootProjectPath))
            return hostPath;

        var copiedAssemblyPath = Path.Combine(
            hostDirectory,
            $"{Path.GetFileNameWithoutExtension(projectPath)}.dll");
        return File.Exists(copiedAssemblyPath) ? copiedAssemblyPath : hostPath;
    }

    private static IReadOnlyList<ProjectBuildInput> EnumerateProjectBuildInputs(string projectPath, string repositoryRoot)
    {
        var inputs = new HashSet<ProjectBuildInput>(ProjectBuildInputComparer.Instance);
        var visitedProjects = new HashSet<string>(PathComparer);
        var pendingProjects = new Stack<string>();
        pendingProjects.Push(Path.GetFullPath(projectPath));

        while (pendingProjects.Count > 0)
        {
            var currentProject = pendingProjects.Pop();
            if (!visitedProjects.Add(currentProject) || !File.Exists(currentProject))
                continue;

            foreach (var input in EnumerateProjectDirectoryInputs(Path.GetDirectoryName(currentProject)!))
                inputs.Add(new ProjectBuildInput(currentProject, input));
            foreach (var props in EnumerateProjectAndAncestorProps(currentProject, repositoryRoot))
                inputs.Add(new ProjectBuildInput(currentProject, props));

            var document = TryLoadProject(currentProject);
            if (document is null)
                continue;

            foreach (var reference in document
                         .Descendants()
                         .Where(static element => element.Name.LocalName == "ProjectReference")
                         .Select(static element => (string?)element.Attribute("Include"))
                         .Where(static include => !string.IsNullOrWhiteSpace(include)))
            {
                var normalizedReference = reference!
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var referencedProject = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(currentProject)!,
                    normalizedReference));
                pendingProjects.Push(referencedProject);
            }
        }

        return inputs.ToArray();
    }

    private static IEnumerable<string> EnumerateProjectDirectoryInputs(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"))
            .Where(path => BuildInputExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<string> EnumerateProjectAndAncestorProps(string projectPath, string repositoryRoot)
    {
        yield return Path.GetFullPath(projectPath);

        var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);
        while (directory is not null)
        {
            foreach (var fileName in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            {
                var path = Path.Combine(directory.FullName, fileName);
                if (File.Exists(path))
                    yield return path;
            }

            if (PathComparer.Equals(Path.TrimEndingDirectorySeparator(directory.FullName), stop))
                yield break;
            directory = directory.Parent;
        }
    }

    private static XDocument? TryLoadProject(string path)
    {
        try
        {
            return XDocument.Load(path, LoadOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static bool HasDirectorySegment(string path, string segment)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (string.Equals(Path.GetFileName(directory), segment, StringComparison.OrdinalIgnoreCase))
                return true;
            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record HostCandidate(
        string HostPath,
        string RuntimeConfigPath,
        string DependencyManifestPath);

    private sealed record ProjectBuildInput(string ProjectPath, string Path);

    private sealed class ProjectBuildInputComparer : IEqualityComparer<ProjectBuildInput>
    {
        public static ProjectBuildInputComparer Instance { get; } = new();

        public bool Equals(ProjectBuildInput? x, ProjectBuildInput? y) =>
            x is not null && y is not null &&
            PathComparer.Equals(x.ProjectPath, y.ProjectPath) &&
            PathComparer.Equals(x.Path, y.Path);

        public int GetHashCode(ProjectBuildInput obj) =>
            HashCode.Combine(
                PathComparer.GetHashCode(obj.ProjectPath),
                PathComparer.GetHashCode(obj.Path));
    }
}
