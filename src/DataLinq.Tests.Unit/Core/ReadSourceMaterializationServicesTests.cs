using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class ReadSourceMaterializationServicesTests
{
    [Test]
    [NotInParallel]
    public async Task DataSourceAccessServices_CommittedScopeReusesCanonicalIdentity()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;
        var factoryCalls = 0;
        var metadata = CreateMetadata(() => factoryCalls++);
        var table = metadata.TableModels.Single().Table;
        DatabaseDefinition.TryRemoveLoadedDatabase(typeof(MaterializationDatabaseModel), out _);

        try
        {
            using var provider = new CacheBackedProvider(metadata);
            var databaseCache = provider.State.Cache;
            var readSource = provider.ReadOnlyAccess;
            var readServices = (IDataLinqReadServices)readSource;

            await Assert.That(readServices.MaterializationServices)
                .IsSameReferenceAs(readServices.MaterializationServices);

            var first = readServices.MaterializationServices.GetOrMaterialize(
                CreateCanonicalRow(table, 42, "Ada"));
            var second = readServices.MaterializationServices.GetOrMaterialize(
                CreateCanonicalRow(table, 42, "Changed value must not rematerialize"));

            await Assert.That(second).IsSameReferenceAs(first);
            await Assert.That(first.GetReadSource()).IsSameReferenceAs(readSource);
            await Assert.That(factoryCalls).IsEqualTo(1);
            await Assert.That(databaseCache.GetTableCache(table).RowCount).IsEqualTo(1);
        }
        finally
        {
            DatabaseDefinition.TryRemoveLoadedDatabase(typeof(MaterializationDatabaseModel), out _);
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    [Test]
    [NotInParallel]
    public async Task DataSourceAccessServices_TransactionScopeDoesNotLeakCommittedIdentity()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;
        var factoryCalls = 0;
        var metadata = CreateMetadata(() => factoryCalls++);
        var table = metadata.TableModels.Single().Table;
        DatabaseDefinition.TryRemoveLoadedDatabase(typeof(MaterializationDatabaseModel), out _);

        try
        {
            using var provider = new CacheBackedProvider(metadata);
            var committedServices = (IDataLinqReadServices)provider.ReadOnlyAccess;
            var committed = committedServices.MaterializationServices.GetOrMaterialize(
                CreateCanonicalRow(table, 42, "Committed"));

            using var transaction = provider.StartTransaction(TransactionType.ReadAndWrite);
            var transactionServices = (IDataLinqReadServices)transaction;
            var pending = transactionServices.MaterializationServices.GetOrMaterialize(
                CreateCanonicalRow(table, 42, "Pending"));
            var pendingAgain = transactionServices.MaterializationServices.GetOrMaterialize(
                CreateCanonicalRow(table, 42, "Changed pending value"));
            var committedAgain = committedServices.MaterializationServices.GetOrMaterialize(
                CreateCanonicalRow(table, 42, "Changed committed value"));

            await Assert.That(pending).IsNotSameReferenceAs(committed);
            await Assert.That(pendingAgain).IsSameReferenceAs(pending);
            await Assert.That(committedAgain).IsSameReferenceAs(committed);
            await Assert.That(pending.GetReadSource()).IsSameReferenceAs(transaction);
            await Assert.That(committed.GetReadSource()).IsSameReferenceAs(provider.ReadOnlyAccess);
            await Assert.That(factoryCalls).IsEqualTo(2);
            await Assert.That(provider.State.Cache.GetTableCache(table).RowCount).IsEqualTo(1);
            await Assert.That(provider.State.Cache.GetTableCache(table).TransactionRowsCount).IsEqualTo(1);
        }
        finally
        {
            DatabaseDefinition.TryRemoveLoadedDatabase(typeof(MaterializationDatabaseModel), out _);
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    [Test]
    [NotInParallel]
    public async Task DataSourceAccessServices_DisabledCommittedCacheDoesNotPublishIdentity()
    {
        var previousBrowserRuntime = DatabaseCache.IsBrowserRuntime;
        DatabaseCache.IsBrowserRuntime = static () => true;
        var factoryCalls = 0;
        var metadata = CreateMetadata(() => factoryCalls++, useCache: false);
        var table = metadata.TableModels.Single().Table;
        DatabaseDefinition.TryRemoveLoadedDatabase(typeof(MaterializationDatabaseModel), out _);

        try
        {
            using var provider = new CacheBackedProvider(metadata);
            var services = ((IDataLinqReadServices)provider.ReadOnlyAccess).MaterializationServices;

            var first = services.GetOrMaterialize(CreateCanonicalRow(table, 42, "First"));
            var second = services.GetOrMaterialize(CreateCanonicalRow(table, 42, "Second"));

            await Assert.That(second).IsNotSameReferenceAs(first);
            await Assert.That(factoryCalls).IsEqualTo(2);
            await Assert.That(provider.State.Cache.GetTableCache(table).RowCount).IsEqualTo(0);
        }
        finally
        {
            DatabaseDefinition.TryRemoveLoadedDatabase(typeof(MaterializationDatabaseModel), out _);
            DatabaseCache.IsBrowserRuntime = previousBrowserRuntime;
        }
    }

    private static CanonicalProviderValueRow CreateCanonicalRow(
        TableDefinition table,
        int id,
        string name)
    {
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = id;
        values[table.GetColumnByDbName("name").Index] = name;
        return CanonicalProviderValueRow.Create(table, values);
    }

    private static DatabaseDefinition CreateMetadata(
        Action recordFactoryCall,
        bool useCache = true)
    {
        var model = new MetadataModelDraft(
            new CsTypeDeclaration(typeof(MaterializationRowModel)))
        {
            ReadSourceImmutableFactory =
                new Func<IRowData, IDataLinqReadSource, IImmutableInstance>(
                    (rowData, readSource) =>
                    {
                        recordFactoryCall();
                        return new TestImmutableInstance(rowData, readSource);
                    }),
            ValueProperties =
            [
                new MetadataValuePropertyDraft(
                    "Id",
                    new CsTypeDeclaration(typeof(int)),
                    new MetadataColumnDraft("id") { PrimaryKey = true })
                {
                    CsSize = sizeof(int)
                },
                new MetadataValuePropertyDraft(
                    "Name",
                    new CsTypeDeclaration(typeof(string)),
                    new MetadataColumnDraft("name"))
            ]
        };

        var draft = new MetadataDatabaseDraft(
            "ReadSourceMaterializationServicesDb",
            new CsTypeDeclaration(typeof(MaterializationDatabaseModel)))
        {
            UseCache = useCache,
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    model,
                    new MetadataTableDraft("materialization_rows") { UseCache = useCache })
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private sealed class MaterializationDatabaseModel;
    private sealed class MaterializationRowModel;

    private sealed class TestImmutableInstance(
        IRowData rowData,
        IDataLinqReadSource readSource) : IImmutableInstance
    {
        public object? this[string propertyName] =>
            rowData.Table.Model.ValueProperties[propertyName].Column is { } column
                ? rowData[column]
                : null;

        public object? this[ColumnDefinition column] => rowData[column];

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() =>
            rowData.GetColumnAndValues();

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(
            IEnumerable<ColumnDefinition> columns) =>
            rowData.GetColumnAndValues(columns);

        public bool HasPrimaryKeysSet() => true;
        public ModelDefinition Metadata() => rowData.Table.Model;
        public DataLinqKey PrimaryKeys() => KeyFactory.GetKey(rowData, rowData.Table.PrimaryKeyColumns);
        public IRowData GetRowData() => rowData;
        IRowData IModelInstance.GetRowData() => GetRowData();
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public IDataLinqReadSource GetReadSource() => readSource;
        public IDataSourceAccess GetDataSource() =>
            readSource as IDataSourceAccess ?? throw new NotSupportedException();
    }

    private sealed class CacheBackedProvider : DataLinq.DatabaseProvider
    {
        internal CacheBackedProvider(DatabaseDefinition metadata)
            : base(
                "Data Source=:memory:",
                typeof(MaterializationDatabaseModel),
                DatabaseType.SQLite,
                DataLinqLoggingConfiguration.NullConfiguration,
                metadataFactory: () => metadata)
        {
        }

        public override IDatabaseProviderConstants Constants => throw new NotSupportedException();
        public override DatabaseAccess DatabaseAccess => throw new NotSupportedException();
        public override IDbCommand ToDbCommand(IQuery query) => throw new NotSupportedException();
        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) =>
            new TestDatabaseTransaction(this, type);
        public override DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) => throw new NotSupportedException();
        public override string GetLastIdQuery() => throw new NotSupportedException();
        public override string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public override string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public override Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public override Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public override string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public override Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new NotSupportedException();
        public override Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public override bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public override bool FileOrServerExists() => throw new NotSupportedException();
        public override IDataLinqDataWriter GetWriter() => throw new NotSupportedException();
        public override Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public override bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public override IDbConnection GetDbConnection() => throw new NotSupportedException();
        public override Sql GetCreateSql() => throw new NotSupportedException();
    }

    private sealed class TestDatabaseTransaction : DatabaseTransaction
    {
        internal TestDatabaseTransaction(IDatabaseProvider provider, TransactionType type)
            : base(provider, type)
        {
            SetStatus(DatabaseTransactionStatus.Open);
        }

        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();

        public override void Rollback() => SetStatus(DatabaseTransactionStatus.RolledBack);
        public override void Commit() => SetStatus(DatabaseTransactionStatus.Committed);

        public override void Dispose()
        {
            if (Status == DatabaseTransactionStatus.Open)
                SetStatus(DatabaseTransactionStatus.RolledBack);
        }
    }
}
