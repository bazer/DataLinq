using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Testing;

namespace DataLinq.Tests.MySql;

public static class TestProviderDataSources
{
    public static IEnumerable<Func<TestProviderDescriptor>> ActiveServerProviders()
    {
        var settings = PodmanTestEnvironmentSettings.FromEnvironment();
        foreach (var provider in TestProviderMatrix.ForCurrentRun(settings).Where(static x => x.ServerTarget is not null))
            yield return () => provider with { };
    }

    public static IEnumerable<Func<TestProviderDescriptor>> MySqlProviders()
    {
        foreach (var provider in ActiveServerProviders()
                     .Select(static factory => factory())
                     .Where(static x => x.DatabaseType == DataLinq.DatabaseType.MySQL))
        {
            yield return () => provider with { };
        }
    }

    public static IEnumerable<Func<TestProviderDescriptor>> MariaDbProviders()
    {
        foreach (var provider in ActiveServerProviders()
                     .Select(static factory => factory())
                     .Where(static x => x.DatabaseType == DataLinq.DatabaseType.MariaDB))
        {
            yield return () => provider with { };
        }
    }
}
