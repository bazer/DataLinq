// In: src/DataLinq.Tests/Core/PkIsFkTests.cs (New File)
using System.Linq;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Tests;
using Xunit;

namespace DataLinq.Tests.Core;

[Collection("MySQL Tests")]
public class PkIsFkTests : IClassFixture<MySqlPkIsFkFixture>
{
    private readonly MySqlPkIsFkFixture _fixture;

    public PkIsFkTests(MySqlPkIsFkFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void PkIsAlsoFk_ShouldBeRequiredInConstructor()
    {
        // Arrange
        var factory = new MetadataFromMySqlFactory(new MetadataFromDatabaseFactoryOptions());
        var dbDefinitionResult = factory.ParseDatabase(
            "PkFkDb", "PkFkDb", "TestNamespace",
            _fixture.TestDatabaseName, _fixture.TestConnection.ConnectionString.Original);

        Assert.True(dbDefinitionResult.HasValue);
        var dbDefinition = dbDefinitionResult.Value;

        var userProfileModel = dbDefinition.TableModels.SingleOrDefault(tm => tm.Table.DbName == "user_profile")?.Model;
        Assert.NotNull(userProfileModel);

        var userIdProperty = userProfileModel.ValueProperties.Values.SingleOrDefault(p => p.Column.DbName == "user_id");
        Assert.NotNull(userIdProperty);

        // This is where the bug is. The generator factory has the logic to determine required properties.
        var generatorFactory = new GeneratorFileFactory(new GeneratorFileFactoryOptions());

        // We need to use reflection to access the internal method, or make it public for testing.
        // Let's assume we can access it for this test.
        var methodInfo = typeof(GeneratorFileFactory).GetMethod("IsMutablePropertyRequired",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(methodInfo);

        // Act
        var isRequired = (bool)methodInfo.Invoke(generatorFactory, new object[] { userIdProperty });

        // Assert
        // THIS IS THE ASSERTION THAT WILL FAIL
        Assert.True(isRequired, "A property that is both a Primary Key and a Foreign Key should be a required constructor parameter.");
    }
}