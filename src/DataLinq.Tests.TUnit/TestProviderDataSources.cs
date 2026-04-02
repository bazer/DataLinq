using System;
using System.Collections.Generic;
using DataLinq.Testing;

namespace DataLinq.Tests.TUnit;

public static class TestProviderDataSources
{
    public static IEnumerable<Func<TestProviderDescriptor>> AllProviders()
    {
        foreach (var provider in TestProviderMatrix.All)
            yield return () => provider with { };
    }

    public static IEnumerable<Func<TestProviderDescriptor>> ServerBackedProviders()
    {
        foreach (var provider in TestProviderMatrix.ServerBacked)
            yield return () => provider with { };
    }
}
