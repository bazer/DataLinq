using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.SQLite;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.TUnit;

public class SQLiteInMemoryBehaviorTests
{
    [Test]
    public async Task SQLiteInMemory_AnonymousDatabasesAreIsolatedPerDatabaseInstance()
    {
        const string connectionString = "Data Source=:memory:";
        const int employeeNumber = 900001;

        using var firstDatabase = new SQLiteDatabase<EmployeesDb>(connectionString);
        using var secondDatabase = new SQLiteDatabase<EmployeesDb>(connectionString);

        var firstCreateResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            firstDatabase.Provider.Metadata,
            firstDatabase.Provider.DatabaseName,
            connectionString,
            true);
        var secondCreateResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            secondDatabase.Provider.Metadata,
            secondDatabase.Provider.DatabaseName,
            connectionString,
            true);

        await Assert.That(firstCreateResult.HasFailed).IsFalse();
        await Assert.That(secondCreateResult.HasFailed).IsFalse();
        await Assert.That(firstDatabase.Provider.ConnectionString == secondDatabase.Provider.ConnectionString).IsFalse();

        firstDatabase.Insert(new MutableEmployee
        {
            birth_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-30)),
            emp_no = employeeNumber,
            first_name = "Test",
            last_name = "Employee",
            gender = Employee.Employeegender.M,
            hire_date = DateOnly.FromDateTime(DateTime.Today)
        });

        await Assert.That(firstDatabase.Query().Employees.Any(x => x.emp_no == employeeNumber)).IsTrue();
        await Assert.That(secondDatabase.Query().Employees.Any(x => x.emp_no == employeeNumber)).IsFalse();
    }

    [Test]
    public async Task SQLiteInMemory_MetadataFactoryCanReadNamedInMemoryDatabaseSchema()
    {
        var databaseName = $"sqlite_metadata_{Guid.NewGuid():N}";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseName,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ConnectionString;

        using var database = new SQLiteDatabase<EmployeesDb>(connectionString, databaseName);

        var createResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            database.Provider.Metadata,
            databaseName,
            connectionString,
            true);

        await Assert.That(createResult.HasFailed).IsFalse();

        var factory = new MetadataFromSQLiteFactory(new MetadataFromDatabaseFactoryOptions());
        var metadata = factory.ParseDatabase(
            "employees",
            "Employees",
            "DataLinq.Tests.Models.Employees",
            databaseName,
            connectionString);

        await Assert.That(metadata.HasValue).IsTrue();
        await Assert.That(metadata.Value.TableModels.Any(x => x.Table.DbName == "employees")).IsTrue();
    }

    [Test]
    public async Task SQLiteInMemory_SqlQueryGuidPredicateMatchesRawTextGuid()
    {
        using var databaseScope = TemporaryModelTestDatabase<SQLiteGuidTextDb>.Create(
            TestProviderMatrix.SQLiteInMemory,
            nameof(SQLiteInMemory_SqlQueryGuidPredicateMatchesRawTextGuid));

        var guid = Guid.NewGuid();
        var inserted = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            $"INSERT INTO sqliteguidrows (guid) VALUES ('{guid:D}')");

        var keys = new SqlQuery<SQLiteGuidRow>(databaseScope.Database.Provider.ReadOnlyAccess)
            .Where("guid")
            .EqualTo(guid)
            .SelectQuery()
            .ReadKeys()
            .ToArray();

        await Assert.That(inserted).IsEqualTo(1);
        await Assert.That(keys.Length).IsEqualTo(1);
    }

    [Test]
    public async Task SQLiteInMemory_LinqGuidPredicateMatchesRawTextGuid()
    {
        using var databaseScope = TemporaryModelTestDatabase<SQLiteGuidTextDb>.Create(
            TestProviderMatrix.SQLiteInMemory,
            nameof(SQLiteInMemory_LinqGuidPredicateMatchesRawTextGuid));

        var guid = Guid.NewGuid();
        var inserted = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            $"INSERT INTO sqliteguidrows (guid) VALUES ('{guid:D}')");

        var row = databaseScope.Database.Query().GuidRows.SingleOrDefault(x => x.Guid == guid);

        await Assert.That(inserted).IsEqualTo(1);
        await Assert.That(row).IsNotNull();
        await Assert.That(row!.Guid).IsEqualTo(guid);
    }
}

[Database("sqliteguidtext")]
public sealed partial class SQLiteGuidTextDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<SQLiteGuidRow> GuidRows { get; } = new(dataSource);
}

[Table("sqliteguidrows")]
public abstract partial class SQLiteGuidRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<SQLiteGuidRow, SQLiteGuidTextDb>(rowData, dataSource), ITableModel<SQLiteGuidTextDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("id")]
    public abstract int? Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Column("guid")]
    public abstract Guid Guid { get; }
}
