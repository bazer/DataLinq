using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.MySql;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MetadataFromFileFactoryTests
{
    [Test]
    public async Task ReadFiles_FromDirectory_ReturnsExpectedDatabaseDefinition()
    {
        using var fixture = new MetadataFromFileFactoryFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());

        var databaseDefinitions = factory.ReadFiles(
            fixture.DatabaseNameFromAttribute,
            [fixture.TempDirectory]).ValueOrException();

        await Assert.That(databaseDefinitions.Count).IsEqualTo(1);
        await Assert.That(databaseDefinitions[0].DbName).IsEqualTo(fixture.DatabaseNameFromAttribute);
        await Assert.That(databaseDefinitions[0].TableModels.Length).IsEqualTo(2);
        await Assert.That(databaseDefinitions[0].TableModels.First(x => x.Table.DbName == "users_file").Model.CsType.Name)
            .IsEqualTo("UserModelFromFile");
    }

    [Test]
    public async Task ReadFiles_FromDuplicateDirectory_ParsesFilesOnce()
    {
        using var fixture = new MetadataFromFileFactoryFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());

        var databaseDefinitions = factory.ReadFiles(
            fixture.DatabaseNameFromAttribute,
            [fixture.TempDirectory, fixture.TempDirectory]).ValueOrException();

        await Assert.That(databaseDefinitions.Count).IsEqualTo(1);
        await Assert.That(databaseDefinitions[0].DbName).IsEqualTo(fixture.DatabaseNameFromAttribute);
        await Assert.That(databaseDefinitions[0].TableModels.Length).IsEqualTo(2);
    }

    [Test]
    public async Task ReadFiles_FromOverlappingDirectoryAndFile_ParsesFileOnce()
    {
        using var fixture = new MetadataFromFileFactoryFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());

        var databaseDefinitions = factory.ReadFiles(
            fixture.DatabaseNameFromAttribute,
            [fixture.TempDirectory, fixture.DbFilePath]).ValueOrException();

        await Assert.That(databaseDefinitions.Count).IsEqualTo(1);
        await Assert.That(databaseDefinitions[0].DbName).IsEqualTo(fixture.DatabaseNameFromAttribute);
        await Assert.That(databaseDefinitions[0].TableModels.Length).IsEqualTo(2);
    }

    [Test]
    public async Task ReadFiles_FromExplicitFiles_ReturnsExpectedDatabaseDefinition()
    {
        using var fixture = new MetadataFromFileFactoryFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());

        var databaseDefinitions = factory.ReadFiles(
            fixture.DatabaseNameFromAttribute,
            [fixture.UserFilePath, fixture.DbFilePath]).ValueOrException();

        await Assert.That(databaseDefinitions.Count).IsEqualTo(1);
        await Assert.That(databaseDefinitions[0].DbName).IsEqualTo(fixture.DatabaseNameFromAttribute);
        await Assert.That(databaseDefinitions[0].TableModels.Length).IsEqualTo(2);
    }

    [Test]
    public async Task ReadFiles_InvalidPath_ReturnsFailure()
    {
        using var fixture = new MetadataFromFileFactoryFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());
        var invalidPath = Path.Combine(fixture.TempDirectory, "non_existent_file.cs");

        var result = factory.ReadFiles("AnyDb", [invalidPath]);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.Failure.Value).IsNotNull();
        await Assert.That(result.Failure.ToString()!).Contains("Path not found");
    }

    [Test]
    public async Task ReadFiles_InvalidPaths_ReturnsAllPathFailures()
    {
        using var fixture = new MetadataFromFileFactoryFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());
        var firstInvalidPath = Path.Combine(fixture.TempDirectory, "missing-one.cs");
        var secondInvalidPath = Path.Combine(fixture.TempDirectory, "missing-two.cs");

        var result = factory.ReadFiles("AnyDb", [firstInvalidPath, secondInvalidPath]);

        await Assert.That(result.HasValue).IsFalse();
        var issues = DataLinqDiagnosticIssue.FromFailure(result.Failure);
        await Assert.That(issues.Count).IsEqualTo(2);
        await Assert.That(issues[0].Message).Contains("missing-one.cs");
        await Assert.That(issues[1].Message).Contains("missing-two.cs");
    }

    [Test]
    public async Task ReadFiles_RemoveInterfacePrefixEnabled_PreservesModelAndInterfaceNames()
    {
        using var fixture = new MetadataFromFileFactoryFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions { RemoveInterfacePrefix = true });

        var databaseDefinitions = factory.ReadFiles(
            fixture.DatabaseNameFromAttribute,
            [fixture.TempDirectory]).ValueOrException();

        var userModel = databaseDefinitions[0].TableModels.First(x => x.Table.DbName == "users_file").Model;

        await Assert.That(userModel.CsType.Name).IsEqualTo("UserModelFromFile");
        await Assert.That(userModel.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(userModel.ModelInstanceInterface!.Value.Name).IsEqualTo("IUserModelFromFile");
    }

    [Test]
    public async Task ReadFiles_RemoveInterfacePrefixDisabled_PreservesModelAndInterfaceNames()
    {
        using var fixture = new MetadataFromFileFactoryFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions { RemoveInterfacePrefix = false });

        var databaseDefinitions = factory.ReadFiles(
            fixture.DatabaseNameFromAttribute,
            [fixture.TempDirectory]).ValueOrException();

        var userModel = databaseDefinitions[0].TableModels.First(x => x.Table.DbName == "users_file").Model;

        await Assert.That(userModel.CsType.Name).IsEqualTo("UserModelFromFile");
        await Assert.That(userModel.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(userModel.ModelInstanceInterface!.Value.Name).IsEqualTo("IUserModelFromFile");
    }

    [Test]
    public async Task ReadFiles_ModelDirectoryInsideProject_LoadsExternalEnumDeclarations()
    {
        using var fixture = new ExternalEnumProjectFixture();
        var factory = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions());

        var databaseDefinitions = factory.ReadFiles(
            "ExternalEnumDb",
            [fixture.ModelDirectory]).ValueOrException();

        var statusProperty = databaseDefinitions
            .Single()
            .TableModels.Single()
            .Model.ValueProperties["Status"];
        var defaultAttribute = (DefaultAttribute)statusProperty.GetDefaultAttribute()!;

        await Assert.That(statusProperty.EnumProperty.HasValue).IsTrue();
        await Assert.That(statusProperty.EnumProperty!.Value.CsEnumValues.Select(x => x.name).ToArray())
            .IsEquivalentTo(["Active", "Inactive"]);
        await Assert.That(statusProperty.EnumProperty!.Value.CsEnumValues.Select(x => x.value).ToArray())
            .IsEquivalentTo([1, 2]);
        await Assert.That(statusProperty.EnumProperty!.Value.DeclaredInModelFile).IsFalse();
        await Assert.That(defaultAttribute.Value).IsEqualTo("ExternalStatus.Inactive");
        await Assert.That(defaultAttribute.CodeExpression).IsEqualTo("ExternalStatus.Inactive");
    }

    [Test]
    public async Task ReadFiles_UnqualifiedGuidDefaultUuid_UsesProviderSpecificToolingTypes()
    {
        using var fixture = new SourceGuidFileFixture(
            "[Type(DatabaseType.Default, \"uuid\")]");
        var database = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions())
            .ReadFiles("source_guid", [fixture.TempDirectory])
            .ValueOrException()
            .Single();
        var column = database.TableModels.Single().Table.Columns.Single();
        var mySqlType = new SqlFromMySqlFactory().GetDbType(column);
        var mariaDbType = new SqlFromMariaDBFactory().GetDbType(column);

        await Assert.That(column.ProviderClrType).IsNull();
        await Assert.That(column.ProviderCsType.Name).IsEqualTo("Guid");
        await Assert.That(mySqlType.Name).IsEqualTo("binary");
        await Assert.That(mySqlType.Length).IsEqualTo(16UL);
        await Assert.That(mariaDbType.Name).IsEqualTo("uuid");
        await Assert.That(mariaDbType.Length).IsNull();
    }

    [Test]
    public async Task ReadFiles_UnqualifiedGuidBinaryType_IsAcceptedAsLegacyTransformPolicy()
    {
        using var fixture = new SourceGuidFileFixture(
            "[Type(DatabaseType.MySQL, \"binary\", 16)]");
        var source = new MetadataFromFileFactory(new MetadataFromFileFactoryOptions())
            .ReadFiles("source_guid", [fixture.TempDirectory])
            .ValueOrException()
            .Single();
        var destination = MetadataDefinitionSnapshot.Copy(source);
        var destinationColumn = destination.TableModels.Single().Table.Columns.Single();
        destinationColumn.SetScalarMappingCore(
            ColumnScalarMapping.Identity(new CsTypeDeclaration(typeof(Guid))));
        destinationColumn.SetUnresolvedGuidStorageProvidersCore(
            [DatabaseType.MySQL]);
        destination.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions())
            .TransformDatabaseSnapshot(source, destination);
        var transformedColumn = transformed.TableModels.Single().Table.Columns.Single();

        await Assert.That(transformedColumn.UnresolvedGuidStorageProviders).IsEmpty();
        await Assert.That(transformedColumn.ValueProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()).IsEmpty();
    }

    private sealed class MetadataFromFileFactoryFixture : IDisposable
    {
        public MetadataFromFileFactoryFixture()
        {
            TempDirectory = Path.Combine(Path.GetTempPath(), "DataLinqTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDirectory);

            File.WriteAllText(DbFilePath, DbModelCode, Encoding.UTF8);
            File.WriteAllText(UserFilePath, UserModelCode, Encoding.UTF8);
            File.WriteAllText(OrderFilePath, OrderModelCode, Encoding.UTF8);
        }

        public string TempDirectory { get; }
        public string UserFilePath => Path.Combine(TempDirectory, "UserModelFromFile.cs");
        public string OrderFilePath => Path.Combine(TempDirectory, "OrderModelFromFile.cs");
        public string DbFilePath => Path.Combine(TempDirectory, "DbModelFromFile.cs");
        public string DatabaseNameFromAttribute => "db_from_file";

        public void Dispose()
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, recursive: true);
        }

        private const string DbModelCode = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using TestNamespace;

namespace TestDbNamespace;

[Database("db_from_file")]
public partial class DbModelFromFile : IDatabaseModel
{
    public DbModelFromFile(DataSourceAccess dsa) {}
    public DbRead<UserModelFromFile> Users { get; }
    public DbRead<OrderModelFromFile> Orders { get; }
}
""";

        private const string UserModelCode = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial interface IUserModelFromFile { }

[Table("users_file")]
[Interface<IUserModelFromFile>]
public abstract partial class UserModelFromFile(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UserModelFromFile, DbModelFromFile>(rowData, dataSource), ITableModel<DbModelFromFile>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
    [Column("name_from_file")] public abstract string Name { get; }
    [Relation("orders_file", "user_id", "FK_Order_User_File")] public abstract IImmutableRelation<OrderModelFromFile> Orders { get; }
}
""";

        private const string OrderModelCode = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial interface IOrderModelFromFile { }

[Table("orders_file")]
[Interface<IOrderModelFromFile>]
public abstract partial class OrderModelFromFile(IRowData rowData, IDataSourceAccess dataSource) : Immutable<OrderModelFromFile, DbModelFromFile>(rowData, dataSource), ITableModel<DbModelFromFile>
{
    [Column("order_id"), PrimaryKey] public abstract int OrderId { get; }
    [Column("user_id"), ForeignKey("users_file", "id", "FK_Order_User_File")] public abstract int UserId { get; }
    [Relation("users_file", "id", "FK_Order_User_File")] public abstract UserModelFromFile User { get; }
}
""";
    }

    private sealed class ExternalEnumProjectFixture : IDisposable
    {
        public ExternalEnumProjectFixture()
        {
            ProjectDirectory = Path.Combine(Path.GetTempPath(), "DataLinqExternalEnumTest_" + Guid.NewGuid().ToString("N"));
            ModelDirectory = Path.Combine(ProjectDirectory, "DataLinq");
            Directory.CreateDirectory(ModelDirectory);

            File.WriteAllText(Path.Combine(ProjectDirectory, "ExternalEnumTest.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />", Encoding.UTF8);
            File.WriteAllText(Path.Combine(ProjectDirectory, "ExternalStatus.cs"), ExternalEnumCode, Encoding.UTF8);
            File.WriteAllText(Path.Combine(ModelDirectory, "ExternalEnumDb.cs"), DbModelCode, Encoding.UTF8);
            File.WriteAllText(Path.Combine(ModelDirectory, "ExternalEnumRow.cs"), RowModelCode, Encoding.UTF8);
        }

        private string ProjectDirectory { get; }
        public string ModelDirectory { get; }

        public void Dispose()
        {
            if (Directory.Exists(ProjectDirectory))
                Directory.Delete(ProjectDirectory, recursive: true);
        }

        private const string ExternalEnumCode = """
namespace ExternalEnumProject;

public enum ExternalStatus
{
    Active = 1,
    Inactive = 2
}
""";

        private const string DbModelCode = """
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace ExternalEnumProject;

[Database("external_enum")]
public partial class ExternalEnumDb : IDatabaseModel
{
    public ExternalEnumDb(DataSourceAccess dsa) {}
    public DbRead<ExternalEnumRow> Rows { get; }
}
""";

        private const string RowModelCode = """
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace ExternalEnumProject;

[Table("rows")]
public abstract partial class ExternalEnumRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<ExternalEnumRow, ExternalEnumDb>(rowData, dataSource), ITableModel<ExternalEnumDb>
{
    [Column("id"), PrimaryKey]
    public abstract int Id { get; }

    [Column("status"), Default(ExternalStatus.Inactive)]
    public abstract ExternalStatus Status { get; }
}
""";
    }

    private sealed class SourceGuidFileFixture : IDisposable
    {
        public SourceGuidFileFixture(string typeAttribute)
        {
            TempDirectory = Path.Combine(
                Path.GetTempPath(),
                "DataLinqSourceGuidTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDirectory);
            File.WriteAllText(
                Path.Combine(TempDirectory, "SourceGuidModels.cs"),
                SourceCode.Replace("%TYPE_ATTRIBUTE%", typeAttribute),
                Encoding.UTF8);
        }

        public string TempDirectory { get; }

        public void Dispose()
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, recursive: true);
        }

        private const string SourceCode = """
using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace SourceGuidFileTest;

[Database("source_guid")]
public partial class SourceGuidDb : IDatabaseModel
{
    public SourceGuidDb(DataSourceAccess dataSource) {}
    public DbRead<SourceGuidRow> Rows { get; }
}

[Table("source_guid_rows")]
public abstract partial class SourceGuidRow(
    IRowData rowData,
    IDataSourceAccess dataSource)
    : Immutable<SourceGuidRow, SourceGuidDb>(rowData, dataSource),
      ITableModel<SourceGuidDb>
{
    [PrimaryKey]
    [Column("id")]
    %TYPE_ATTRIBUTE%
    public abstract Guid Id { get; }
}
""";
    }
}
