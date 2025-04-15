using System;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees; // Import your actual test models
using ThrowAway.Extensions;
using Xunit;

namespace DataLinq.Tests.Core
{

    /*
        Using typeof(): We get the System.Type object for your actual compiled test models (Employee, Department, EmployeesDb).

        ParseModel Test (Indirect): Since ParseModel might be internal, TestParseModel_Employee and TestParseModel_Department first call ParseDatabaseFromDatabaseModel to get the full definition and then extract the specific ModelDefinition they want to test from the result. This implicitly tests the model parsing logic.

        Assertions:

            We assert basic properties like CsType.Name, Table.DbName, Table.Type.

            We use helper methods (AssertValueProperty, AssertRelationProperty) for common checks on properties.

            We check specific attributes (PrimaryKeyAttribute, ColumnAttribute, EnumAttribute, IndexAttribute, InterfaceAttribute, etc.) are present on the reflected properties/types.

            For enums, we verify the EnumProperty struct is populated correctly based on the C# enum definition.

        TestParseDatabase_EmployeesDb: This test verifies the top-level DatabaseDefinition parsing:

            Checks database name and attributes (UseCache, CacheLimit, etc.).

            Confirms all expected TableModel instances (for tables and views) are present.

            Performs a detailed check on a specific relationship (Employee <-> Dept_emp) to ensure ParseRelations linked everything correctly (correct RelationPart types, shared RelationDefinition, correct columns involved).

            Performs a check on a known index (departments.dept_name) to ensure ParseIndices linked it correctly.
     */

    public class MetadataFromTypeFactoryTests
    {
        // Helper for assertions (can be expanded)
        private void AssertValueProperty(ModelDefinition model, string propName, string expectedCsTypeName, bool? expectedNullable = null)
        {
            Assert.True(model.ValueProperties.ContainsKey(propName), $"ValueProperty '{propName}' not found.");
            var prop = model.ValueProperties[propName];
            Assert.Equal(expectedCsTypeName, prop.CsType.Name);
            if (expectedNullable.HasValue)
            {
                Assert.Equal(expectedNullable.Value, prop.CsNullable);
            }
            Assert.NotNull(prop.Column); // Should be linked after parsing
            Assert.Same(prop, prop.Column.ValueProperty); // Back-link check
        }

        private void AssertRelationProperty(ModelDefinition model, string propName, string expectedCsTypeName)
        {
            Assert.True(model.RelationProperties.ContainsKey(propName), $"RelationProperty '{propName}' not found.");
            var prop = model.RelationProperties[propName];
            // Relation property CsType might be IImmutableRelation<T> or just T
            Assert.True(prop.CsType.Name == expectedCsTypeName || prop.CsType.Name.Contains($"<{expectedCsTypeName}>"),
                $"Expected relation type '{expectedCsTypeName}' or generic containing it, but got '{prop.CsType.Name}'");
            // Full RelationPart linking is tested in ParseDatabase test
        }


        [Fact]
        public void TestParseModel_Employee()
        {
            // Arrange
            var modelType = typeof(Employee);

            // Act
            // We need a dummy DatabaseDefinition context for ParseModel implicitly,
            // let's create one. MetadataFromTypeFactory might need adjustment if
            // ParseModel isn't directly exposed or callable this way.
            // Assuming we can simulate the context needed or if ParseModel was public static.
            // If ParseModel is internal/private, we test it via ParseDatabaseFromDatabaseModel.
            // Let's assume for this test we can get a ModelDefinition somehow (e.g., via ParseDatabase).
            var dbDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
            var employeeModel = dbDefinition.TableModels.Single(tm => tm.Model.CsType.Type == modelType).Model;

            // Assert
            Assert.NotNull(employeeModel);
            Assert.Equal("Employee", employeeModel.CsType.Name);
            Assert.Equal(typeof(Employee), employeeModel.CsType.Type);
            Assert.Equal(TableType.Table, employeeModel.Table.Type);
            Assert.Equal("employees", employeeModel.Table.DbName); // Check TableAttribute

            // Check Value Properties
            AssertValueProperty(employeeModel, "emp_no", "int", true); // Nullable due to AutoIncrement? Check your definition. Assuming nullable int? based on code.
            AssertValueProperty(employeeModel, "birth_date", "DateOnly", false);
            AssertValueProperty(employeeModel, "first_name", "string", false);
            AssertValueProperty(employeeModel, "last_name", "string", false);
            AssertValueProperty(employeeModel, "gender", "Employeegender", false); // Enum type
            AssertValueProperty(employeeModel, "hire_date", "DateOnly", false);
            AssertValueProperty(employeeModel, "IsDeleted", "bool", true); // Nullable bool?

            // Check specific attributes on properties
            var empNoProp = employeeModel.ValueProperties["emp_no"];
            Assert.Contains(empNoProp.Attributes, a => a is PrimaryKeyAttribute);
            Assert.Contains(empNoProp.Attributes, a => a is AutoIncrementAttribute);
            Assert.Contains(empNoProp.Attributes, a => a is ColumnAttribute c && c.Name == "emp_no");
            Assert.Contains(empNoProp.Attributes, a => a is TypeAttribute); // Check if Type attributes are present

            var genderProp = employeeModel.ValueProperties["gender"];
            Assert.Contains(genderProp.Attributes, a => a is EnumAttribute);
            Assert.NotNull(genderProp.EnumProperty);
            Assert.Equal(2, genderProp.EnumProperty.Value.CsEnumValues.Count); // M, F
            Assert.Equal("M", genderProp.EnumProperty.Value.CsEnumValues[0].name);


            // Check Relation Properties (existence and type)
            AssertRelationProperty(employeeModel, "dept_emp", "Dept_emp"); // Expects IImmutableRelation<Dept_emp>
            AssertRelationProperty(employeeModel, "dept_manager", "Manager");
            AssertRelationProperty(employeeModel, "salaries", "Salaries");
            AssertRelationProperty(employeeModel, "titles", "Titles");

            // Check Model-level attributes (like Interface)
            Assert.NotNull(employeeModel.ModelInstanceInterface);
            Assert.Equal("IEmployee", employeeModel.ModelInstanceInterface.Value.Name);
        }

        [Fact]
        public void TestParseModel_Department()
        {
            // Arrange
            var modelType = typeof(Department);
            var dbDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
            var deptModel = dbDefinition.TableModels.Single(tm => tm.Model.CsType.Type == modelType).Model;

            // Assert
            Assert.NotNull(deptModel);
            Assert.Equal("Department", deptModel.CsType.Name);
            Assert.Equal("departments", deptModel.Table.DbName);

            AssertValueProperty(deptModel, "DeptNo", "string", false);
            AssertValueProperty(deptModel, "Name", "string", false); // Changed name via partial

            var deptNoProp = deptModel.ValueProperties["DeptNo"];
            Assert.Contains(deptNoProp.Attributes, a => a is PrimaryKeyAttribute);
            Assert.Contains(deptNoProp.Attributes, a => a is ColumnAttribute c && c.Name == "dept_no");

            var nameProp = deptModel.ValueProperties["Name"];
            Assert.Contains(nameProp.Attributes, a => a is ColumnAttribute c && c.Name == "dept_name");
            Assert.Contains(nameProp.Attributes, a => a is IndexAttribute ia && ia.Name == "dept_name" && ia.Characteristic == IndexCharacteristic.Unique);

            AssertRelationProperty(deptModel, "DepartmentEmployees", "Dept_emp"); // Name changed via partial? Check actual name. Assuming relation property name matches field.
            AssertRelationProperty(deptModel, "Managers", "Manager");

            Assert.NotNull(deptModel.ModelInstanceInterface);
            Assert.Equal("IDepartmentWithChangedName", deptModel.ModelInstanceInterface.Value.Name); // Custom interface name
        }

        [Fact]
        public void TestParseDatabase_EmployeesDb()
        {
            // Arrange
            var dbType = typeof(EmployeesDb);

            // Act
            var dbDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(dbType).ValueOrException();

            // Assert
            // Database level
            Assert.NotNull(dbDefinition);
            Assert.Equal("EmployeesDb", dbDefinition.CsType.Name);
            Assert.Equal("employees", dbDefinition.DbName); // From DatabaseAttribute
            Assert.True(dbDefinition.UseCache); // From UseCacheAttribute
            Assert.Contains(dbDefinition.CacheLimits, cl => cl.limitType == CacheLimitType.Megabytes && cl.amount == 200);
            Assert.Contains(dbDefinition.CacheLimits, cl => cl.limitType == CacheLimitType.Minutes && cl.amount == 60);
            Assert.Contains(dbDefinition.CacheCleanup, cc => cc.cleanupType == CacheCleanupType.Minutes && cc.amount == 30);

            // Check TableModels presence
            Assert.Equal(8, dbDefinition.TableModels.Length); // 6 tables + 2 views
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "employees" && tm.Model.CsType.Type == typeof(Employee));
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "departments" && tm.Model.CsType.Type == typeof(Department));
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "dept-emp" && tm.Model.CsType.Type == typeof(Dept_emp));
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "dept_manager" && tm.Model.CsType.Type == typeof(Manager));
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "salaries" && tm.Model.CsType.Type == typeof(Salaries));
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "titles" && tm.Model.CsType.Type == typeof(Titles));
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "current_dept_emp" && tm.Model.CsType.Type == typeof(current_dept_emp) && tm.Table.Type == TableType.View);
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "dept_emp_latest_date" && tm.Model.CsType.Type == typeof(dept_emp_latest_date) && tm.Table.Type == TableType.View);

            // Check Relation Linking (Example: Employee <-> Dept_emp)
            var employeeModel = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "employees").Model;
            var deptEmpModel = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "dept-emp").Model;

            var employeeToDeptEmpRel = employeeModel.RelationProperties["dept_emp"]; // One-to-Many
            var deptEmpToEmployeeRel = deptEmpModel.RelationProperties["employees"]; // Many-to-One

            Assert.NotNull(employeeToDeptEmpRel.RelationPart);
            Assert.NotNull(deptEmpToEmployeeRel.RelationPart);
            Assert.Same(employeeToDeptEmpRel.RelationPart.Relation, deptEmpToEmployeeRel.RelationPart.Relation); // Should share the same RelationDefinition
            Assert.Equal(RelationPartType.CandidateKey, employeeToDeptEmpRel.RelationPart.Type);
            Assert.Equal(RelationPartType.ForeignKey, deptEmpToEmployeeRel.RelationPart.Type);
            Assert.Same(employeeToDeptEmpRel.RelationPart, deptEmpToEmployeeRel.RelationPart.GetOtherSide());
            Assert.Same(deptEmpToEmployeeRel.RelationPart, employeeToDeptEmpRel.RelationPart.GetOtherSide());

            // Check Index linking (Example: departments.dept_name unique index)
            var deptTable = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "departments").Table;
            var nameCol = deptTable.Columns.Single(c => c.DbName == "dept_name");
            var uniqueIndex = deptTable.ColumnIndices.SingleOrDefault(idx => idx.Name == "dept_name");
            Assert.NotNull(uniqueIndex);
            Assert.Equal(IndexCharacteristic.Unique, uniqueIndex.Characteristic);
            Assert.Single(uniqueIndex.Columns);
            Assert.Same(nameCol, uniqueIndex.Columns[0]);
        }
    }
}   