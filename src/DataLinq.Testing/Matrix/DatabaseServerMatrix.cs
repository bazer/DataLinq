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
                x.HostPort,
                x.IsLts,
                x.IsDefault))
            .ToArray();

        EnsureUnique(targets.Select(static x => x.Id), StringComparer.OrdinalIgnoreCase, "target ids", matrixPath);
        EnsureUnique(targets.Select(static x => x.Image), StringComparer.OrdinalIgnoreCase, "target images", matrixPath);
        EnsureUnique(targets.Select(static x => x.HostPort), EqualityComparer<int>.Default, "target host ports", matrixPath);

        var profiles = dto.Profiles
            .Select(x =>
            {
                var profileTargets = x.Targets.Select(targetId =>
                    targets.SingleOrDefault(target => string.Equals(target.Id, targetId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(
                        $"Database server matrix '{matrixPath}' profile '{x.Id}' references unknown target '{targetId}'."));

                return DatabaseServerProfile.Create(x.Id, x.DisplayName, x.IsDefault, profileTargets);
            })
            .ToArray();

        Validate(targets, profiles, matrixPath);
        var defaultProfile = profiles.Single(static x => x.IsDefault);
        return new DatabaseServerMatrixData(targets, profiles, defaultProfile);
    }

    internal static void Validate(
        IReadOnlyList<DatabaseServerTarget> targets,
        IReadOnlyList<DatabaseServerProfile> profiles,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (targets.Count == 0)
            throw new InvalidOperationException($"Database server matrix '{sourceName}' must contain at least one target.");
        if (profiles.Count == 0)
            throw new InvalidOperationException($"Database server matrix '{sourceName}' must contain at least one profile.");

        EnsureUnique(targets.Select(static x => x.Id), StringComparer.OrdinalIgnoreCase, "target ids", sourceName);
        EnsureUnique(targets.Select(static x => x.Image), StringComparer.OrdinalIgnoreCase, "target images", sourceName);
        EnsureUnique(targets.Select(static x => x.HostPort), EqualityComparer<int>.Default, "target host ports", sourceName);
        EnsureUnique(profiles.Select(static x => x.Id), StringComparer.OrdinalIgnoreCase, "profile ids", sourceName);

        var defaults = new List<DatabaseServerTarget>();
        foreach (var family in Enum.GetValues<DatabaseServerFamily>())
        {
            var familyDefaults = targets
                .Where(target => target.Family == family && target.IsDefault)
                .ToArray();
            if (familyDefaults.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Database server matrix '{sourceName}' must configure exactly one default target for family " +
                    $"'{family}'; found {familyDefaults.Length}: [{string.Join(", ", familyDefaults.Select(static x => x.Id))}].");
            }

            if (!familyDefaults[0].IsLts)
            {
                throw new InvalidOperationException(
                    $"Database server matrix '{sourceName}' default target '{familyDefaults[0].Id}' must be marked as LTS.");
            }

            defaults.Add(familyDefaults[0]);
        }

        var defaultProfiles = profiles.Where(static x => x.IsDefault).ToArray();
        if (defaultProfiles.Length != 1)
        {
            throw new InvalidOperationException(
                $"Database server matrix '{sourceName}' must configure exactly one default profile; found " +
                $"{defaultProfiles.Length}: [{string.Join(", ", defaultProfiles.Select(static x => x.Id))}].");
        }

        foreach (var profile in profiles)
        {
            EnsureUnique(
                profile.Targets.Select(static x => x.Id),
                StringComparer.OrdinalIgnoreCase,
                $"targets in profile '{profile.Id}'",
                sourceName);

            var duplicateFamilies = profile.Targets
                .GroupBy(static x => x.Family)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToArray();
            if (duplicateFamilies.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Database server matrix '{sourceName}' profile '{profile.Id}' contains multiple targets for " +
                    $"families [{string.Join(", ", duplicateFamilies)}].");
            }
        }

        var defaultTargetIds = defaults.Select(static x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultProfileTargetIds = defaultProfiles[0].Targets.Select(static x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!defaultTargetIds.SetEquals(defaultProfileTargetIds))
        {
            throw new InvalidOperationException(
                $"Database server matrix '{sourceName}' default profile '{defaultProfiles[0].Id}' must contain exactly " +
                $"the explicitly configured family defaults [{string.Join(", ", defaults.Select(static x => x.Id))}]; " +
                $"found [{string.Join(", ", defaultProfiles[0].Targets.Select(static x => x.Id))}].");
        }
    }

    private static void EnsureUnique<T>(
        IEnumerable<T> values,
        IEqualityComparer<T> comparer,
        string description,
        string sourceName)
        where T : notnull
    {
        var duplicates = values
            .GroupBy(static value => value, comparer)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Database server matrix '{sourceName}' contains duplicate {description}: [{string.Join(", ", duplicates)}].");
        }
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
        int HostPort,
        bool IsLts,
        bool IsDefault);

    private sealed record MatrixProfileDto(
        string Id,
        string DisplayName,
        bool IsDefault,
        string[] Targets);
}
