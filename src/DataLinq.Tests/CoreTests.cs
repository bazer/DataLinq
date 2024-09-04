using System;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.MySql.Models;
using DataLinq.Tests.Models.Employees;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataLinq.Tests;

public class CoreTests : BaseTests
{
    [Fact]
    public void TestMetadataFromFixture()
    {
        Assert.Equal(2, DatabaseDefinition.LoadedDatabases.Count);
        Assert.Contains(DatabaseDefinition.LoadedDatabases, x => x.Key == typeof(EmployeesDb));
        Assert.Contains(DatabaseDefinition.LoadedDatabases, x => x.Key == typeof(information_schema));
    }


    [Fact]
    public void TestMetadataFromFilesFactory()
    {
        var projectRoot = Path.Combine(Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.Parent.FullName, "DataLinq.Tests.Models");
        var srcPaths = DatabaseFixture.DataLinqConfig.Databases.Single(x => x.Name == "employees").SourceDirectories
            .Select(x => Path.Combine(projectRoot, x))
            .ToList();

        //var srcPaths = Fixture.DataLinqConfig.Databases.Single(x => x.Name == "employees").SourceDirectories.Select(x => Path.GetFullPath(x)).ToList();
        var metadata = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions { }).ReadFiles("", srcPaths);

        TestDatabaseAttributes(metadata);
        TestDatabase(metadata, false);
    }

    [Fact]
    public void TestMetadataFromInterfaceFactory()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb));

        TestDatabaseAttributes(metadata);
        TestDatabase(metadata, true);
    }

    [Theory]
    [MemberData(nameof(GetEmployeeConnections))]
    public void TestMetadataFromSqlFactory(DataLinqDatabaseConnection connection)
    {
        var factory = PluginHook.MetadataFromSqlFactories[connection.Type]
            .GetMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());

        var metadata = factory.ParseDatabase("employees", "Employees", "DataLinq.Tests.Models.Employees", connection.DataSourceName, connection.ConnectionString.Original);
        TestDatabase(metadata, false);
    }

    private void TestDatabaseAttributes(DatabaseDefinition database)
    {
        Assert.Equal(2, database.CacheLimits.Count);
        Assert.Equal(CacheLimitType.Megabytes, database.CacheLimits[0].limitType);
        Assert.Equal(200, database.CacheLimits[0].amount);
        Assert.Equal(CacheLimitType.Minutes, database.CacheLimits[1].limitType);
        Assert.Equal(60, database.CacheLimits[1].amount);

        Assert.Single(database.CacheCleanup);
        Assert.Equal(CacheCleanupType.Minutes, database.CacheCleanup[0].cleanupType);
        Assert.Equal(30, database.CacheCleanup[0].amount);

        Assert.Single(database.CacheCleanup);
    }

    private void TestDatabase(DatabaseDefinition database, bool testCsType)
    {
        Assert.NotEmpty(database.TableModels);
        Assert.Equal(8, database.TableModels.Length);
        Assert.Equal(2, database.TableModels.Count(x => x.Table.Type == TableType.View));
        Assert.Equal(12, database.TableModels.Sum(x => x.Model.RelationProperties.Count()));
        Assert.Contains(database.TableModels, x => x.Table.Columns.Any(y => y.ColumnIndices.Any(z => z.RelationParts.Any())));

        var employees = database.TableModels.Single(x => x.Table.DbName == "employees").Table;
        Assert.Same(employees, employees.Model.Table);
        Assert.Equal(7, employees.Columns.Length);

        var emp_no = employees.Columns.Single(x => x.DbName == "emp_no");
        Assert.True(emp_no.PrimaryKey);
        Assert.True(emp_no.AutoIncrement);

        Assert.NotEmpty(emp_no.DbTypes);
        
        if (emp_no.DbTypes.Any(x=> x.DatabaseType == DatabaseType.MySQL))
            Assert.Equal("int", emp_no.DbTypes.Single(x => x.DatabaseType == DatabaseType.MySQL).Name);
        
        if (emp_no.DbTypes.Any(x=> x.DatabaseType == DatabaseType.SQLite))
            Assert.Equal("integer", emp_no.DbTypes.Single(x => x.DatabaseType == DatabaseType.SQLite).Name);

        Assert.Equal("int", emp_no.ValueProperty.CsType.Name);

        var dept_name = database.TableModels.Single(x => x.Table.DbName == "departments").Table.Columns.Single(x => x.DbName == "dept_name");
        Assert.Equal("string", dept_name.ValueProperty.CsType.Name);
        Assert.False(dept_name.PrimaryKey);
        Assert.False(dept_name.AutoIncrement);
        Assert.Single(dept_name.ColumnIndices);
        Assert.Equal(IndexCharacteristic.Unique, dept_name.ColumnIndices.Single().Characteristic);
        Assert.Equal("dept_name", dept_name.ColumnIndices.Single().Name);
        Assert.Same(dept_name, dept_name.ColumnIndices.First().Columns.Single());

        if (testCsType)
        {
            Assert.Equal(typeof(int), emp_no.ValueProperty.CsType.Type);
            Assert.DoesNotContain(database.TableModels, x => x.Model.CsType.Type == null);
        }
    }
}