using System.Collections.Generic;

namespace DataLinq.Testing.CLI;

internal sealed record TestSuiteParityManifest(
    IReadOnlyList<TestSuiteParityEntry> Entries);

internal sealed record TestSuiteParityEntry(
    string LegacyFile,
    string Status,
    IReadOnlyList<string> ReplacementFiles,
    string? Note = null);
