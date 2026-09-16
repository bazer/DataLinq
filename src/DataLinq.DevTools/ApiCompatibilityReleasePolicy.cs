using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.DevTools;

// These are evidence policies, not inferred package capabilities. Unknown release
// lines must receive an explicit reviewed policy before they can produce a report.
internal sealed record ApiCompatibilityReleasePolicy(
    string Id,
    string ReportSchemaVersion,
    string LockSchemaVersion,
    bool MemoryIsNew,
    IReadOnlyList<string> BaselinePackageIds,
    IReadOnlyList<string> LibraryComparisonPackageIds,
    IReadOnlyList<string> CandidatePackageIds)
{
    public static ApiCompatibilityReleasePolicy ForBaseline(string baselineVersion)
    {
        var historical = baselineVersion == "0.8.0";
        if (!historical &&
            (!Version.TryParse(baselineVersion, out var version) ||
             version.Major != 0 || version.Minor != 9 || version.Build < 0 ||
             version.Revision != -1 || version.ToString() != baselineVersion))
        {
            throw new ArgumentException(
                "API reports support the historical 0.8.0 baseline or an explicitly locked stable 0.9 patch.",
                nameof(baselineVersion));
        }

        var baselineIds = PackageInspectionPolicy.PublicPackageIds
            .Where(id => !historical || id != PackageInspectionPolicy.MemoryPackageId)
            .ToArray();
        return new ApiCompatibilityReleasePolicy(
            historical ? "v0.9" : "v0.10",
            historical ? ApiCompatibilityReporter.SchemaVersion : "v0.10.api-compatibility-report.v1",
            historical ? ApiCompatibilityBaselineLock.SchemaVersion : ApiCompatibilityBaselineLock.V010SchemaVersion,
            historical,
            Array.AsReadOnly(baselineIds),
            Array.AsReadOnly(baselineIds.Where(id => id != PackageInspectionPolicy.CliPackageId).ToArray()),
            PackageInspectionPolicy.PublicPackageIds);
    }
}
