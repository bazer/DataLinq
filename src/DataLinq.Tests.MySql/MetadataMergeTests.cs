using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.MySql;
using DataLinq.Testing;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class MetadataMergeTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_AndMergeWithFileMetadata_PreservesModelNamesAndInterfaces(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_AndMergeWithFileMetadata_PreservesModelNamesAndInterfaces),
            """
            CREATE TABLE departments (
                dept_no VARCHAR(4) PRIMARY KEY,
                dept_name VARCHAR(40) NOT NULL UNIQUE
            );
            """,
            """
            CREATE TABLE employees (
                emp_no INT PRIMARY KEY AUTO_INCREMENT,
                birth_date DATE NOT NULL,
                first_name VARCHAR(14) NOT NULL,
                last_name VARCHAR(16) NOT NULL,
                gender ENUM('M', 'F') NOT NULL,
                hire_date DATE NOT NULL,
                dept_no VARCHAR(4) NULL,
                CONSTRAINT employees_ibfk_1 FOREIGN KEY (dept_no) REFERENCES departments(dept_no)
            );
            """);

        var sqlMetadata = schema.ParseDatabase("employees", "EmployeesDb", "DataLinq.Tests.Models.Employees");

        var repositoryRoot = RepositoryLayout.FindRepositoryRoot();
        var employeesModelRoot = Path.Combine(repositoryRoot, "src", "DataLinq.Tests.Models", "employees");
        var fileMetadata = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions())
            .ReadFiles("employees", [employeesModelRoot])
            .ValueOrException()
            .Single();

        var transformer = new MetadataTransformer(new MetadataTransformerOptions());
        var mergedMetadata = transformer.TransformDatabaseSnapshot(fileMetadata, sqlMetadata);

        var employees = mergedMetadata.TableModels.Single(x => x.Table.DbName == "employees").Table;
        var departments = mergedMetadata.TableModels.Single(x => x.Table.DbName == "departments").Table;

        await Assert.That(mergedMetadata.CsType.Name).IsEqualTo("EmployeesDb");
        await Assert.That(employees.Model.CsType.Name).IsEqualTo("Employee");
        await Assert.That(employees.Model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(employees.Model.ModelInstanceInterface!.Value.Name).IsEqualTo("IEmployee");
        await Assert.That(departments.Model.CsType.Name).IsEqualTo("Department");
        await Assert.That(departments.Model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(departments.Model.ModelInstanceInterface!.Value.Name).IsEqualTo("IDepartmentWithChangedName");
    }
}
