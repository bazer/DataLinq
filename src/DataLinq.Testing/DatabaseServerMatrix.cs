using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DataLinq.Testing;

public static class DatabaseServerMatrix
{
    private static readonly Lazy<DatabaseServerMatrixData> LoadedData = new(Load);

    public static IReadOnlyList<DatabaseServerTarget> Targets => LoadedData.Value.Targets;
    public static IReadOnlyList<DatabaseServerProfile> Profiles => LoadedData.Value.Profiles;
    public static DatabaseServerProfile DefaultProfile => LoadedData.Value.DefaultProfile;

    public static DatabaseServerTarget GetTarget(string id) =>
        Targets.Single(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public static DatabaseServerProfile GetProfile(string id) =>
        Profiles.Single(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    private static DatabaseServerMatrixData Load()
    {
        var matrixPath = Path.Combine(RepositoryLayout.FindRepositoryRoot(), "test-infra", "podman", "matrix.json");
        if (!File.Exists(matrixPath))
            throw new FileNotFoundException($"The database server matrix file was not found: '{matrixPath}'.", matrixPath);

        var json = File.ReadAllText(matrixPath);
        var dto = JsonSerializer.Deserialize<MatrixDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Could not deserialize '{matrixPath}'.");

        var targets = dto.Targets
            .Select(x => new DatabaseServerTarget(
                x.Id,
                x.DisplayName,
                Enum.Parse<DatabaseServerFamily>(x.Family, ignoreCase: true),
                x.Version,
                x.Image,
                x.IsLts,
                x.IsDefault))
            .ToArray();

        var profiles = dto.Profiles
            .Select(x => DatabaseServerProfile.Create(
                x.Id,
                x.DisplayName,
                x.IsDefault,
                x.Targets.Select(targetId => targets.Single(target => string.Equals(target.Id, targetId, StringComparison.OrdinalIgnoreCase)))))
            .ToArray();

        var defaultProfile = profiles.Single(x => x.IsDefault);
        return new DatabaseServerMatrixData(targets, profiles, defaultProfile);
    }

    private sealed record DatabaseServerMatrixData(
        IReadOnlyList<DatabaseServerTarget> Targets,
        IReadOnlyList<DatabaseServerProfile> Profiles,
        DatabaseServerProfile DefaultProfile);

    private sealed record MatrixDto(MatrixTargetDto[] Targets, MatrixProfileDto[] Profiles);

    private sealed record MatrixTargetDto(
        string Id,
        string DisplayName,
        string Family,
        string Version,
        string Image,
        bool IsLts,
        bool IsDefault);

    private sealed record MatrixProfileDto(
        string Id,
        string DisplayName,
        bool IsDefault,
        string[] Targets);
}
