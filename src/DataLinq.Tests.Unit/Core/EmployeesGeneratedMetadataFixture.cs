using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

internal static class EmployeesGeneratedMetadataFixture
{
    static EmployeesGeneratedMetadataFixture()
    {
        _ = DatabaseDefinition.ResolveLoadedDatabase(
            typeof(EmployeesDb),
            () => MetadataFromTypeFactory
                .ParseDatabaseFromDatabaseModel(typeof(EmployeesDb))
                .ValueOrException(),
            EmployeesDb.SetDataLinqGeneratedMetadata);
    }

    internal static void EnsureInitialized()
    {
    }
}
