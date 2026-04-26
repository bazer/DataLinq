using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Core.Factories.Models;
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
}
