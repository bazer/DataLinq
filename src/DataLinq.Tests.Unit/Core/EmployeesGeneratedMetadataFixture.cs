using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

internal static class EmployeesGeneratedMetadataFixture
{
    static EmployeesGeneratedMetadataFixture()
    {
        var metadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel(typeof(EmployeesDb))
            .ValueOrException();
        EmployeesDb.SetDataLinqGeneratedMetadata(metadata);
        DatabaseDefinition.TryAddLoadedDatabase(typeof(EmployeesDb), metadata);
    }

    internal static void EnsureInitialized()
    {
    }
}
