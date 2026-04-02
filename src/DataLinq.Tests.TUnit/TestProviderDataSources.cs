using System;
using System.Collections.Generic;
using DataLinq.Testing;

namespace DataLinq.Tests.TUnit;

public static class TestProviderDataSources
{
    public static IEnumerable<Func<TestProviderDescriptor>> ActiveProviders()
    {
        var settings = PodmanTestEnvironmentSettings.FromEnvironment();
        foreach (var provider in TestProviderMatrix.ForActiveProfile(settings))
            yield return () => provider with { };
    }

    public static IEnumerable<Func<TestProviderDescriptor>> AllLtsServerProviders()
    {
        foreach (var provider in TestProviderMatrix.AllLtsServerProviders)
            yield return () => provider with { };
    }

    public static IEnumerable<Func<TestProviderDescriptor>> SqliteProviders()
    {
        foreach (var provider in TestProviderMatrix.SQLiteOnly)
            yield return () => provider with { };
    }
}
