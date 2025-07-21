using System;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.MariaDB.information_schema;
using DataLinq.Metadata;
using DataLinq.MySql.information_schema;
using DataLinq.Tests.Models.Employees;
using Microsoft.CodeAnalysis;
using ThrowAway.Extensions;
using Xunit;

namespace DataLinq.Tests;

public class CoreTests : BaseTests
{
    private static DatabaseDefinition GetDatabaseDefinitionFromFiles(string databaseName, string dir = "DataLinq.Tests.Models")
    {
        var projectRoot = Path.Combine(Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.Parent.FullName, dir);
        var srcPaths = DatabaseFixture.DataLinqConfig.Databases.Single(x => x.Name == databaseName).SourceDirectories
            .Select(x => Path.Combine(projectRoot, x))
            .ToList();

        var metadata = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions { }).ReadFiles("", srcPaths).ValueOrException();

        return metadata.Single();
    }

    [Fact]
    public void TestMetadataFromFixture()
    {
        Assert.Equal(3, DatabaseDefinition.LoadedDatabases.Count);
        Assert.Contains(DatabaseDefinition.LoadedDatabases, x => x.Key == typeof(EmployeesDb));
        Assert.Contains(DatabaseDefinition.LoadedDatabases, x => x.Key == typeof(MariaDBInformationSchema));
        Assert.Contains(DatabaseDefinition.LoadedDatabases, x => x.Key == typeof(MySQLInformationSchema));
    }

    [Fact]
    public void TestMetadataFromFilesFactory()
    {
        var metadata = GetDatabaseDefinitionFromFiles("employees");

        var employees = metadata.TableModels.Single(x => x.Table.DbName == "employees").Table;
        Assert.Equal("Employee", employees.Model.CsType.Name);
        Assert.Single(employees.Model.OriginalInterfaces);
        Assert.DoesNotContain(employees.Model.OriginalInterfaces, x => x.Name == "IEmployee");
        Assert.Contains(employees.Model.OriginalInterfaces, x => x.Name == "ITableModel<EmployeesDb>");
        Assert.DoesNotContain(employees.Model.OriginalInterfaces, x => x.Name.StartsWith("Immutable"));

        Assert.NotNull(employees.Model.ModelInstanceInterface);
        Assert.Equal("IEmployee", employees.Model.ModelInstanceInterface.Value.Name);

        var departments = metadata.TableModels.Single(x => x.Table.DbName == "departments").Table;
        Assert.Equal("Department", departments.Model.CsType.Name);
        Assert.NotNull(departments.Model.ModelInstanceInterface);
        Assert.Equal("IDepartmentWithChangedName", departments.Model.ModelInstanceInterface.Value.Name);

        TestDatabaseAttributes(metadata);
        TestDatabase(metadata, false);
    }

    [Fact]
    public void TestMetadataFromInterfaceFactory()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();

        var employees = metadata.TableModels.Single(x => x.Table.DbName == "employees").Table;
        Assert.Equal("Employee", employees.Model.CsType.Name);
        Assert.True(employees.Model.OriginalInterfaces.Length > 1);
        Assert.DoesNotContain(employees.Model.OriginalInterfaces, x => x.Name == "IEmployee");
        Assert.Contains(employees.Model.OriginalInterfaces, x => x.Name == "ITableModel<EmployeesDb>");
        Assert.DoesNotContain(employees.Model.OriginalInterfaces, x => x.Name.StartsWith("Immutable"));

        Assert.NotNull(employees.Model.ModelInstanceInterface);
        Assert.Equal("IEmployee", employees.Model.ModelInstanceInterface.Value.Name);

        //var departments = metadata.TableModels.Single(x => x.Table.DbName == "departments").Table;
        //Assert.Equal("Department", departments.Model.CsType.Name);
        //Assert.Single(departments.Model.ModelInstanceInterfaces);
        //Assert.Equal("IDepartmentWithChangedName", departments.Model.ModelInstanceInterfaces.Single().Name);

        TestDatabaseAttributes(metadata);
        TestDatabase(metadata, true);
    }

    [Theory]
    [MemberData(nameof(GetEmployeeConnections))]
    public void TestMetadataFromSqlFactory(DataLinqDatabaseConnection connection)
    {
        var factory = PluginHook.MetadataFromSqlFactories[connection.Type]
            .GetMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());

        var metadata = factory.ParseDatabase("employees", "Employees", "DataLinq.Tests.Models.Employees", connection.DataSourceName, connection.ConnectionString.Original).Value;

        var employees = metadata.TableModels.Single(x => x.Table.DbName == "employees").Table;
        Assert.Equal("employees", employees.Model.CsType.Name);

        TestDatabase(metadata, false);
    }

    [Theory]
    [MemberData(nameof(GetEmployeeConnections))]
    public void TestMetadataFromSqlAndMergeWithFiles(DataLinqDatabaseConnection connection)
    {
        var factory = PluginHook.MetadataFromSqlFactories[connection.Type]
            .GetMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());

        var metadataDB = factory.ParseDatabase("employees", "Employees", "DataLinq.Tests.Models.Employees", connection.DataSourceName, connection.ConnectionString.Original).Value;

        var metadataFiles = GetDatabaseDefinitionFromFiles("employees");

        var transformer = new MetadataTransformer(new MetadataTransformerOptions());
        transformer.TransformDatabase(metadataFiles, metadataDB);

        Assert.Equal("EmployeesDb", metadataDB.CsType.Name);
        Assert.Equal("Department", metadataDB.TableModels.Single(x => x.CsPropertyName == "Departments").Model.CsType.Name);

        var employees = metadataDB.TableModels.Single(x => x.Table.DbName == "employees").Table;
        Assert.Equal("Employee", employees.Model.CsType.Name);

        TestDatabase(metadataDB, false);
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
        Assert.Equal("employees", employees.DbName);
        Assert.Equal(9, employees.Columns.Length);
        Assert.Equal(1, employees.Columns.Count(x => x.PrimaryKey));
        Assert.Equal(1, employees.Columns.Count(x => x.AutoIncrement));


        var emp_no = employees.Columns.Single(x => x.DbName == "emp_no");
        Assert.True(emp_no.PrimaryKey);
        Assert.True(emp_no.AutoIncrement);

        Assert.NotEmpty(emp_no.DbTypes);

        if (emp_no.DbTypes.Any(x => x.DatabaseType == DatabaseType.MySQL))
            Assert.Equal("int", emp_no.DbTypes.Single(x => x.DatabaseType == DatabaseType.MySQL).Name);

        if (emp_no.DbTypes.Any(x => x.DatabaseType == DatabaseType.SQLite))
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