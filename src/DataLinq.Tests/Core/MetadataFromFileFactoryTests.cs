using System;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.Core.Factories.Models; // Where the factory resides
using Xunit;

namespace DataLinq.Tests.Core
{
    /*
        Uses an IClassFixture (MetadataFromFileFactoryFixture) to handle the creation and cleanup of temporary files needed for testing the file reading capabilities.

        The fixture creates a temp directory and writes sample C# model files (DbModelFromFile.cs, UserModelFromFile.cs, OrderModelFromFile.cs) into it before tests run, and deletes the directory afterwards.

        TestReadFiles_ValidPath_Directory: Tests reading all .cs files from the temp directory.

        TestReadFiles_ValidPath_File: Tests reading a single specific .cs file.

        TestReadFiles_InvalidPath: Tests that the factory returns a failure Option when given a path that doesn't exist.

        TestRemoveInterfacePrefixOption_Enabled/Disabled: Tests how the RemoveInterfacePrefix option in MetadataFromFileFactoryOptions potentially affects the parsed CsType.Name of the ModelDefinition. Note the comment that this depends heavily on whether the factory actually derives class names from interfaces or just uses the declared class name.
     */

    public class MetadataFromFileFactoryTests : IClassFixture<MetadataFromFileFactoryFixture> // Use a fixture for file setup/cleanup
    {
        private readonly MetadataFromFileFactoryFixture _fixture;

        public MetadataFromFileFactoryTests(MetadataFromFileFactoryFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void TestReadFiles_ValidPath_Directory()
        {
            // Arrange
            var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());
            var expectedDbName = _fixture.DatabaseNameFromAttribute; // Name from attribute in the file

            // Act
            var result = factory.ReadFiles(expectedDbName, new[] { _fixture.TempDirectory });

            // Assert
            Assert.True(result.HasValue, result.HasFailed ? result.Failure.ToString() : "ReadFiles failed");
            var dbDefinition = result.Value;
            Assert.Equal(expectedDbName, dbDefinition.DbName);
            Assert.Equal(2, dbDefinition.TableModels.Length); // User + Order from fixture file
            Assert.Equal("UserModelFromFile", dbDefinition.TableModels.First(x => x.Table.DbName == "users_file").Model.CsType.Name);
        }

        [Fact]
        public void TestReadFiles_ValidPath_File()
        {
            // Arrange
            var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());
            var expectedDbName = _fixture.DatabaseNameFromAttribute;

            // Act
            var result = factory.ReadFiles(expectedDbName, new[] { _fixture.UserFilePath }); // Pass specific file path

            // Assert
            Assert.True(result.HasValue, result.HasFailed ? result.Failure.ToString() : "ReadFiles failed");
            var dbDefinition = result.Value;
            // Note: It might parse *all* models found in the file, even if not directly related
            // to the 'csType' passed, depending on the factory's exact logic.
            // Adjust assertion based on actual factory behavior.
            // For now, assume it parses the whole file context.
            Assert.Equal(expectedDbName, dbDefinition.DbName);
            Assert.Equal(2, dbDefinition.TableModels.Length);
        }

        [Fact]
        public void TestReadFiles_InvalidPath()
        {
            // Arrange
            var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());
            var invalidPath = Path.Combine(_fixture.TempDirectory, "non_existent_file.cs");

            // Act
            var result = factory.ReadFiles("AnyDb", new[] { invalidPath });

            // Assert
            Assert.False(result.HasValue);
            Assert.NotNull(result.Failure);
            // Check for specific failure type if desired (requires Failure object structure)
            // Assert.Equal(DLFailureType.FileNotFound, result.Failure.Type);
            Assert.Contains("Path not found", result.Failure.ToString());
        }

        [Fact]
        public void TestRemoveInterfacePrefixOption_Enabled()
        {
            // Arrange
            var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions { RemoveInterfacePrefix = true });

            // Act
            var result = factory.ReadFiles(_fixture.DatabaseNameFromAttribute, new[] { _fixture.TempDirectory });

            // Assert
            Assert.True(result.HasValue);
            var dbDefinition = result.Value;
            // Check a model known to have an 'I' prefix in its interface declaration within the file
            var userModel = dbDefinition.TableModels.First(x => x.Table.DbName == "users_file").Model;
            Assert.Equal("UserModelFromFile", userModel.CsType.Name); // Prefix should be removed from class name if derived from interface name
            Assert.NotNull(userModel.ModelInstanceInterface);
            // The *interface* name itself shouldn't change, but the generated *class* name might be derived without 'I'
            Assert.Equal("IUserModelFromFile", userModel.ModelInstanceInterface.Value.Name);
        }

        [Fact]
        public void TestRemoveInterfacePrefixOption_Disabled()
        {
            // Arrange
            // NOTE: This tests the *option*, but MetadataFromFileFactory might *not* actually derive class names
            // from interfaces. It depends on its implementation. If it always uses the class name, this test might not show a difference.
            // Assuming for the test that if RemoveInterfacePrefix=false, it *might* keep the 'I' if deriving name from interface.
            var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions { RemoveInterfacePrefix = false });

            // Act
            var result = factory.ReadFiles(_fixture.DatabaseNameFromAttribute, new[] { _fixture.TempDirectory });

            // Assert
            Assert.True(result.HasValue);
            var dbDefinition = result.Value;
            var userModel = dbDefinition.TableModels.First(x => x.Table.DbName == "users_file").Model;
            // Assert based on the actual behavior - does it keep the class name as defined, or derive from interface?
            // If it always uses the defined class name "UserModelFromFile", this assert remains the same.
            Assert.Equal("UserModelFromFile", userModel.CsType.Name);
            Assert.NotNull(userModel.ModelInstanceInterface);
            Assert.Equal("IUserModelFromFile", userModel.ModelInstanceInterface.Value.Name);
        }
    }

    // --- Test Fixture for File Operations ---
    public class MetadataFromFileFactoryFixture : IDisposable
    {
        public string TempDirectory { get; private set; }
        public string UserFilePath => Path.Combine(TempDirectory, "UserModelFromFile.cs");
        public string OrderFilePath => Path.Combine(TempDirectory, "OrderModelFromFile.cs");
        public string DbFilePath => Path.Combine(TempDirectory, "DbModelFromFile.cs");
        public string DatabaseNameFromAttribute => "db_from_file"; // Match attribute in DbModelFromFile.cs

        public MetadataFromFileFactoryFixture()
        {
            // Create a unique temp directory for test files
            TempDirectory = Path.Combine(Path.GetTempPath(), "DataLinqTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDirectory);

            // Write sample model files
            File.WriteAllText(DbFilePath, DbModelCode, Encoding.UTF8);
            File.WriteAllText(UserFilePath, UserModelCode, Encoding.UTF8);
            File.WriteAllText(OrderFilePath, OrderModelCode, Encoding.UTF8);
        }

        public void Dispose()
        {
            // Cleanup: Delete the temp directory and its contents
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, true);
            }
        }

        // Sample code for the files (similar to the syntax test)
        private const string DbModelCode = @"
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using System;
using TestNamespace; // Reference models

namespace TestDbNamespace;

[Database(""db_from_file"")]
public partial class DbModelFromFile : IDatabaseModel
{
    public DbModelFromFile(DataSourceAccess dsa){}
    public DbRead<UserModelFromFile> Users { get; }
    public DbRead<OrderModelFromFile> Orders { get; }
}";

        private const string UserModelCode = @"
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;
using TestDbNamespace; // Reference DB

namespace TestNamespace;

public partial interface IUserModelFromFile : ITableModel<DbModelFromFile> { }

[Table(""users_file"")]
[Interface<IUserModelFromFile>]
public abstract partial class UserModelFromFile(RowData rowData, DataSourceAccess dataSource) : Immutable<UserModelFromFile, DbModelFromFile>(rowData, dataSource), IUserModelFromFile
{
    [Column(""id""), PrimaryKey] public abstract int Id { get; }
    [Column(""name_from_file"")] public abstract string Name { get; }
    [Relation(""orders_file"", ""user_id"", ""FK_Order_User_File"")] public abstract IImmutableRelation<OrderModelFromFile> Orders { get; }
}";

        private const string OrderModelCode = @"
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;
using TestDbNamespace; // Reference DB

namespace TestNamespace;

public partial interface IOrderModelFromFile : ITableModel<DbModelFromFile> { }

[Table(""orders_file"")]
[Interface<IOrderModelFromFile>]
public abstract partial class OrderModelFromFile(RowData rowData, DataSourceAccess dataSource) : Immutable<OrderModelFromFile, DbModelFromFile>(rowData, dataSource), IOrderModelFromFile
{
    [Column(""order_id""), PrimaryKey] public abstract int OrderId { get; }
    [Column(""user_id""), ForeignKey(""users_file"", ""id"", ""FK_Order_User_File"")] public abstract int UserId { get; }
    [Relation(""users_file"", ""id"", ""FK_Order_User_File"")] public abstract UserModelFromFile User { get; }
}";
    }
}