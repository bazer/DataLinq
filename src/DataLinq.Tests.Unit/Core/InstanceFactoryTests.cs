using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class InstanceFactoryTests
{
    [Test]
    public async Task NewDatabase_DataSourceAccessConstructor_CreatesDatabaseModel()
    {
        var dataSource = new FakeDataSourceAccess();

        var database = InstanceFactory.NewDatabase<FactoryDatabase>(dataSource);

        await Assert.That(database.DataSource).IsSameReferenceAs(dataSource);
    }

    [Test]
    public async Task NewReadDatabase_LegacyModelAndSqlSource_UsesLegacyFactory()
    {
        var dataSource = new FakeDataSourceAccess();

        var database = InstanceFactory.NewReadDatabase<FactoryDatabase>(dataSource);

        await Assert.That(database.DataSource).IsSameReferenceAs(dataSource);
    }

    [Test]
    public async Task NewReadDatabase_LegacyModelAndNeutralSource_RequiresRegeneration()
    {
        var rowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("Factory must not run."));
        var readSource = new NeutralReadSource(rowData.Table.Database);

        var exception = Capture<InvalidOperationException>(() =>
            InstanceFactory.NewReadDatabase<FactoryDatabase>(readSource));

        await Assert.That(exception.Message).Contains("Generated read-source database factory not defined");
        await Assert.That(exception.Message).Contains(typeof(FactoryDatabase).FullName!);
        await Assert.That(exception.Message).Contains("Regenerate the database model root");
    }

    [Test]
    public async Task NewReadDatabase_NeutralFactory_UsesExactNeutralSource()
    {
        var rowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("Factory must not run."));
        var readSource = new NeutralReadSource(rowData.Table.Database);

        var database = InstanceFactory.NewReadDatabase<NeutralFactoryDatabase>(readSource);

        await Assert.That(database.ReadSource).IsSameReferenceAs(readSource);
        await Assert.That(database.ReadSource).IsNotAssignableTo<IDataSourceAccess>();
    }

    [Test]
    public async Task DbRead_NeutralSource_ConstructsRootAndDefersBackendRequirementUntilExecution()
    {
        var rowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("Factory must not run."));
        var readSource = new NeutralReadSource(rowData.Table.Database);

        var query = new DbRead<FactoryRow>(readSource);

        await Assert.That(query.Expression).IsNotNull();
        await Assert.That(query.Provider).IsNotNull();

        var exception = Capture<NotSupportedException>(() => query.ToList());

        await Assert.That(exception.Message).Contains(typeof(NeutralReadSource).FullName!);
        await Assert.That(exception.Message).Contains("does not yet provide query-plan execution");
    }

    [Test]
    public async Task Queryable_NeutralSource_RejectsTableFromDifferentMetadataGraph()
    {
        var firstRowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("Factory must not run."));
        var secondRowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("Factory must not run."));
        var readSource = new NeutralReadSource(firstRowData.Table.Database);

        var exception = Capture<ArgumentException>(() =>
            new Queryable<FactoryRow>(readSource, secondRowData.Table));

        await Assert.That(exception.ParamName).IsEqualTo("table");
        await Assert.That(exception.Message).Contains("Read source metadata does not own query-root table");
    }

    [Test]
    public async Task IDataSourceAccess_Metadata_UsesProviderMetadataByDefault()
    {
        var rowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("Factory must not run."));
        var provider = new MetadataOnlyDatabaseProvider(rowData.Table.Database);
        IDataSourceAccess dataSource = new FakeDataSourceAccess(provider);

        var metadata = ((IDataLinqReadSource)dataSource).Metadata;

        await Assert.That(metadata).IsSameReferenceAs(provider.Metadata);
    }

    [Test]
    public async Task IImmutableInstance_GetReadSource_DefaultsToLegacyDataSource()
    {
        var rowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("Factory must not run."));
        var dataSource = new FakeDataSourceAccess(new MetadataOnlyDatabaseProvider(rowData.Table.Database));
        IImmutableInstance instance = new TestImmutableInstance(rowData, dataSource);

        var readSource = instance.GetReadSource();

        await Assert.That(readSource).IsSameReferenceAs(dataSource);
    }

    [Test]
    public async Task NewReadSourceImmutableRow_NeutralFactory_UsesExactNeutralSourceOnce()
    {
        IRowData? capturedRowData = null;
        IDataLinqReadSource? capturedReadSource = null;
        var neutralFactoryCalls = 0;
        var legacyFactoryCalls = 0;
        var expected = new TestImmutableInstance();
        var rowData = CreateFactoryRowData(
            (_, _) =>
            {
                legacyFactoryCalls++;
                throw new InvalidOperationException("The neutral path must not invoke the SQL factory.");
            },
            (factoryRowData, factoryReadSource) =>
            {
                neutralFactoryCalls++;
                capturedRowData = factoryRowData;
                capturedReadSource = factoryReadSource;
                return expected;
            });
        var readSource = new NeutralReadSource(rowData.Table.Database);

        var actual = InstanceFactory.NewReadSourceImmutableRow(rowData, readSource);

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(neutralFactoryCalls).IsEqualTo(1);
        await Assert.That(legacyFactoryCalls).IsEqualTo(0);
        await Assert.That(capturedRowData).IsSameReferenceAs(rowData);
        await Assert.That(capturedReadSource).IsSameReferenceAs(readSource);
        await Assert.That(readSource).IsNotAssignableTo<IDataSourceAccess>();
    }

    [Test]
    public async Task NewReadSourceImmutableRow_BothFactoriesAndSqlSource_PrefersNeutralFactory()
    {
        var neutralFactoryCalls = 0;
        var legacyFactoryCalls = 0;
        var expected = new TestImmutableInstance();
        var rowData = CreateFactoryRowData(
            (_, _) =>
            {
                legacyFactoryCalls++;
                return new TestImmutableInstance();
            },
            (_, _) =>
            {
                neutralFactoryCalls++;
                return expected;
            });
        var dataSource = new FakeDataSourceAccess(new MetadataOnlyDatabaseProvider(rowData.Table.Database));

        var actual = InstanceFactory.NewReadSourceImmutableRow(rowData, dataSource);

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(neutralFactoryCalls).IsEqualTo(1);
        await Assert.That(legacyFactoryCalls).IsEqualTo(0);
    }

    [Test]
    public async Task NewReadSourceImmutableRow_LegacyOnlyFactoryAndSqlSource_UsesLegacyFactoryDirectly()
    {
        IRowData? capturedRowData = null;
        IDataSourceAccess? capturedDataSource = null;
        var legacyFactoryCalls = 0;
        var expected = new TestImmutableInstance();
        var rowData = CreateFactoryRowData(
            (factoryRowData, factoryDataSource) =>
            {
                legacyFactoryCalls++;
                capturedRowData = factoryRowData;
                capturedDataSource = factoryDataSource;
                return expected;
            });
        var dataSource = new FakeDataSourceAccess(new MetadataOnlyDatabaseProvider(rowData.Table.Database));

        var actual = InstanceFactory.NewReadSourceImmutableRow(rowData, dataSource);

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(legacyFactoryCalls).IsEqualTo(1);
        await Assert.That(capturedRowData).IsSameReferenceAs(rowData);
        await Assert.That(capturedDataSource).IsSameReferenceAs(dataSource);
    }

    [Test]
    public async Task NewReadSourceImmutableRow_LegacyOnlyFactoryAndNeutralSource_RequiresRegeneration()
    {
        var rowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("The SQL factory must not run."));
        var readSource = new NeutralReadSource(rowData.Table.Database);

        var exception = Capture<InvalidOperationException>(() =>
            InstanceFactory.NewReadSourceImmutableRow(rowData, readSource));

        await Assert.That(exception.Message).Contains("Generated read-source immutable factory not defined");
        await Assert.That(exception.Message).Contains(nameof(FactoryRow));
        await Assert.That(exception.Message).Contains("Update or regenerate");
        await Assert.That(exception.Message).Contains(nameof(IDataLinqReadSource));
    }

    [Test]
    public async Task NewReadSourceImmutableRow_MalformedNeutralFactory_ReportsExpectedDelegateShape()
    {
        var legacyFactoryCalls = 0;
        var rowData = CreateFactoryRowDataCore(
            (_, _) =>
            {
                legacyFactoryCalls++;
                return new TestImmutableInstance();
            },
            new Func<IRowData, IImmutableInstance>(_ => new TestImmutableInstance()));
        var dataSource = new FakeDataSourceAccess(new MetadataOnlyDatabaseProvider(rowData.Table.Database));

        var exception = Capture<InvalidOperationException>(() =>
            InstanceFactory.NewReadSourceImmutableRow(rowData, dataSource));

        await Assert.That(exception.Message).Contains("incompatible delegate shape");
        await Assert.That(exception.Message).Contains(nameof(IDataLinqReadSource));
        await Assert.That(exception.Message).Contains(nameof(IImmutableInstance));
        await Assert.That(legacyFactoryCalls).IsEqualTo(0);
    }

    [Test]
    public async Task NewReadSourceImmutableRow_NeutralFactoryReturningNull_ReportsModel()
    {
        var rowData = CreateFactoryRowData(
            (_, _) => throw new InvalidOperationException("The SQL factory must not run."),
            (_, _) => null!);
        var readSource = new NeutralReadSource(rowData.Table.Database);

        var exception = Capture<InvalidOperationException>(() =>
            InstanceFactory.NewReadSourceImmutableRow(rowData, readSource));

        await Assert.That(exception.Message).Contains("read-source immutable factory returned null");
        await Assert.That(exception.Message).Contains(nameof(FactoryRow));
    }

    private static IRowData CreateFactoryRowData(
        Func<IRowData, IDataSourceAccess, IImmutableInstance> legacyFactory,
        Func<IRowData, IDataLinqReadSource, IImmutableInstance>? readSourceFactory = null)
        => CreateFactoryRowDataCore(legacyFactory, readSourceFactory);

    private static IRowData CreateFactoryRowDataCore(
        Func<IRowData, IDataSourceAccess, IImmutableInstance> legacyFactory,
        Delegate? readSourceFactory)
    {
        var draft = new MetadataDatabaseDraft(
            "InstanceFactoryDb",
            new CsTypeDeclaration(typeof(FactoryDatabase)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(FactoryRow)))
                    {
                        ImmutableType = new CsTypeDeclaration(typeof(TestImmutableInstance)),
                        ImmutableFactory = legacyFactory,
                        ReadSourceImmutableFactory = readSourceFactory,
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "integer")]
                                })
                            {
                                CsSize = sizeof(int)
                            }
                        ]
                    },
                    new MetadataTableDraft("instance_factory_rows"))
            ]
        };

        var table = new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;

        return new TestRowData(table);
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected {typeof(TException).Name}.");
    }

    private sealed class FactoryDatabase : IDatabaseModel<FactoryDatabase>
    {
        public FactoryDatabase(DataSourceAccess dataSource)
        {
            DataSource = dataSource;
        }

        public DataSourceAccess DataSource { get; }

        public static MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>
            new("FactoryDatabase", new CsTypeDeclaration(typeof(FactoryDatabase)));

        public static void SetDataLinqGeneratedMetadata(DatabaseDefinition metadata)
        {
        }

        public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() => new([]);

        public static FactoryDatabase NewDataLinqDatabase(IDataSourceAccess dataSource) =>
            new((DataSourceAccess)dataSource);
    }

    private sealed class NeutralFactoryDatabase(IDataLinqReadSource readSource)
        : IDatabaseModel<NeutralFactoryDatabase>
    {
        public IDataLinqReadSource ReadSource { get; } = readSource;

        public static MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>
            new("NeutralFactoryDatabase", new CsTypeDeclaration(typeof(NeutralFactoryDatabase)));

        public static void SetDataLinqGeneratedMetadata(DatabaseDefinition metadata)
        {
        }

        public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() => new([]);

        public static NeutralFactoryDatabase NewDataLinqDatabase(IDataSourceAccess dataSource) =>
            new(dataSource);

        public static NeutralFactoryDatabase NewDataLinqReadDatabase(IDataLinqReadSource readSource) =>
            new(readSource);
    }

    private sealed class FactoryRow;

    private sealed class NeutralReadSource(DatabaseDefinition metadata) : IDataLinqReadSource
    {
        public DatabaseDefinition Metadata { get; } = metadata;
    }

    private sealed class TestRowData(TableDefinition table) : IRowData
    {
        public TableDefinition Table { get; } = table;
        public object? this[ColumnDefinition column] => throw new NotSupportedException();
        public object? this[int columnIndex] => throw new NotSupportedException();
        public object? GetValue(ColumnDefinition column) => throw new NotSupportedException();
        public object? GetValue(int columnIndex) => throw new NotSupportedException();
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns) => [];
    }

    private sealed class TestImmutableInstance(
        IRowData? rowData = null,
        IDataSourceAccess? dataSource = null) : IImmutableInstance
    {
        public object? this[string propertyName] => throw new NotSupportedException();
        public object? this[ColumnDefinition column] => throw new NotSupportedException();
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => [];
        public bool HasPrimaryKeysSet() => true;
        public ModelDefinition Metadata() => rowData?.Table.Model ?? throw new NotSupportedException();
        public DataLinqKey PrimaryKeys() => DataLinqKey.Null;
        public IRowData GetRowData() => rowData ?? throw new NotSupportedException();
        IRowData IModelInstance.GetRowData() => GetRowData();
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public IDataSourceAccess GetDataSource() => dataSource ?? throw new NotSupportedException();
    }

    private sealed class FakeDataSourceAccess : DataSourceAccess
    {
        public FakeDataSourceAccess()
            : base(null!)
        {
        }

        public FakeDataSourceAccess(IDatabaseProvider provider)
            : base(provider)
        {
        }

        public override IDatabaseAccess DatabaseAccess => throw new NotSupportedException();

        public override IEnumerable<T> GetFromQuery<T>(string query) => throw new NotSupportedException();

        public override IEnumerable<T> GetFromCommand<T>(IDbCommand dbCommand) => throw new NotSupportedException();
    }

    private sealed class MetadataOnlyDatabaseProvider(DatabaseDefinition metadata) : IDatabaseProvider
    {
        public string TelemetryInstanceId => "instance-factory-tests";
        public string DatabaseName => Metadata.DbName;
        public string ConnectionString => throw new NotSupportedException();
        public DatabaseDefinition Metadata { get; } = metadata;
        public DatabaseAccess DatabaseAccess => throw new NotSupportedException();
        public State State => throw new NotSupportedException();
        public IDatabaseProviderConstants Constants => throw new NotSupportedException();
        public ReadOnlyAccess ReadOnlyAccess => throw new NotSupportedException();
        public DatabaseType DatabaseType => DatabaseType.SQLite;
        public IDbCommand ToDbCommand(IQuery query) => throw new NotSupportedException();
        public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite) => throw new NotSupportedException();
        public DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) => throw new NotSupportedException();
        public DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) => throw new NotSupportedException();
        public string GetLastIdQuery() => throw new NotSupportedException();
        public string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public TableCache GetTableCache(TableDefinition table) => throw new NotSupportedException();
        public string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new NotSupportedException();
        public Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public bool FileOrServerExists() => throw new NotSupportedException();
        public IDataLinqDataWriter GetWriter() => throw new NotSupportedException();
        public Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public M Commit<M>(Func<Transaction, M> func) => throw new NotSupportedException();
        public void Commit(Action<Transaction> action) => throw new NotSupportedException();
        public bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public IDbConnection GetDbConnection() => throw new NotSupportedException();
        public Sql GetCreateSql() => throw new NotSupportedException();
        public void Dispose() { }
    }
}
