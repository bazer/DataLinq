// In: src/DataLinq.MySql.Tests/RecursiveRelationTests.cs (New File)
using System.Linq;
using System.Numerics;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Tests;
using DataLinq.Query;
using Xunit;

namespace DataLinq.Tests.Core;

[Collection("MySQL Tests")]
public class RecursiveRelationTests : IClassFixture<MySqlRecursiveRelationFixture>
{
    private readonly MySqlRecursiveRelationFixture _fixture;

    public RecursiveRelationTests(MySqlRecursiveRelationFixture fixture)
    {
        _fixture = fixture;
    }

    // In: src/DataLinq.MySql.Tests/RecursiveRelationTests.cs

    [Fact]
    public void ParseDatabase_WithRecursiveRelation_ShouldCreateCorrectRelations()
    {
        // Arrange
        var factory = new MetadataFromMySqlFactory(new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });

        // Act
        var dbDefinitionResult = factory.ParseDatabase(
            "RecursiveDb", "RecursiveDb", "TestNamespace",
            _fixture.TestDatabaseName, _fixture.TestConnection.ConnectionString.Original);

        // Assert: Parsing should succeed
        Assert.True(dbDefinitionResult.HasValue);
        var dbDefinition = dbDefinitionResult.Value;

        var employeeModel = dbDefinition.TableModels.SingleOrDefault(tm => tm.Table.DbName == "employee")?.Model;
        Assert.NotNull(employeeModel);

        Assert.Equal(2, employeeModel.RelationProperties.Count);

        // Assert: One property for the manager (many-to-one, single object).
        // The name should be derived from the 'manager_id' FK column -> "Manager".
        var managerProperty = employeeModel.RelationProperties.Values
            .SingleOrDefault(p => p.PropertyName == "Manager");
        Assert.NotNull(managerProperty);
        Assert.NotNull(managerProperty.RelationPart);
        Assert.Equal(RelationPartType.ForeignKey, managerProperty.RelationPart.Type);
        Assert.DoesNotContain("IImmutableRelation", managerProperty.CsType.Name);


        // Assert: One property for the subordinates (one-to-many, collection).
        // The name should be derived from the table name itself -> "Employee".
        var subordinatesProperty = employeeModel.RelationProperties.Values
            .SingleOrDefault(p => p.PropertyName == "Employee");
        Assert.NotNull(subordinatesProperty);
        Assert.NotNull(subordinatesProperty.RelationPart);
        Assert.Equal(RelationPartType.CandidateKey, subordinatesProperty.RelationPart.Type);
        Assert.Contains("IImmutableRelation", subordinatesProperty.CsType.Name);

        // Assert: Both properties should point to the same underlying RelationDefinition
        Assert.Same(managerProperty.RelationPart.Relation, subordinatesProperty.RelationPart.Relation);
        Assert.Equal("FK_Employee_Manager", managerProperty.RelationPart.Relation.ConstraintName);

        // Assert: They should point to each other
        Assert.Same(subordinatesProperty.RelationPart, managerProperty.RelationPart.GetOtherSide());
        Assert.Same(managerProperty.RelationPart, subordinatesProperty.RelationPart.GetOtherSide());
    }
}