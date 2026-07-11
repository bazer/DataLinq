using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
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

public sealed partial class ModelValueConverterTests
{
    [Test]
    public async Task ToCanonicalProviderValue_IdentityMappingPreservesCanonicalPrimitive()
    {
        var table = CreateIdentityTable();

        var canonicalValue = ModelValueConverter.ToCanonicalProviderValue(
            table.Columns.Single(),
            42,
            "mutation.insert");

        await Assert.That(canonicalValue).IsEqualTo(42);
        await Assert.That(canonicalValue).IsTypeOf<int>();
    }

    [Test]
    public async Task ToCanonicalProviderValue_ConvertsOnceWithColumnContextAndOwnsBinaryResult()
    {
        var providerBytes = new byte[] { 1, 2, 3 };
        var converter = new RecordingScalarConverter(
            typeof(BinaryModelId),
            typeof(byte[]),
            static (value, _) => ((BinaryModelId)value!).Value);
        var table = CreateSingleConvertedTable("binary_mutation_rows", converter);
        var column = table.Columns.Single();
        var modelValue = new BinaryModelId(providerBytes);

        var canonicalValue = (byte[])ModelValueConverter.ToCanonicalProviderValue(
            column,
            modelValue,
            "mutation.insert")!;

        canonicalValue[0] = 9;

        await Assert.That(converter.ToProviderCalls.Count).IsEqualTo(1);
        await Assert.That(converter.ToProviderCalls[0].Value).IsSameReferenceAs(modelValue);
        await Assert.That(converter.ToProviderCalls[0].Context.Column).IsSameReferenceAs(column);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(providerBytes[0]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task ToCanonicalProviderValue_NullBypassesConverterForExplicitNullMutationValue()
    {
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value);
        var table = CreateSingleConvertedTable("unset_mutation_rows", converter);

        var canonicalValue = ModelValueConverter.ToCanonicalProviderValue(
            table.Columns.Single(),
            null,
            "mutation.insert");

        await Assert.That(canonicalValue).IsNull();
        await Assert.That(converter.ToProviderCalls).IsEmpty();
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ToCanonicalProviderValue_WrapsWrongConverterResultWithoutRenderingContents()
    {
        const string secretProviderValue = "wrong-secret-provider-result";
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (_, _) => secretProviderValue);
        var table = CreateSingleConvertedTable("wrong_mutation_rows", converter);

        var exception = Capture<ModelValueConversionException>(() =>
            ModelValueConverter.ToCanonicalProviderValue(
                table.Columns.Single(),
                new MutationId(42),
                "mutation.update.value"));

        await Assert.That(exception.Column).IsSameReferenceAs(table.Columns.Single());
        await Assert.That(exception.ConverterType).IsEqualTo(typeof(RecordingScalarConverter));
        await Assert.That(exception.SourceName).IsEqualTo("mutation.update.value");
        await Assert.That(exception.InnerException).IsTypeOf<ArgumentException>();
        await Assert.That(exception.Message).Contains(typeof(MutationId).FullName!);
        await Assert.That(exception.Message).Contains(typeof(int).FullName!);
        await Assert.That(exception.Message).Contains(typeof(RecordingScalarConverter).FullName!);
        await Assert.That(exception.Message).Contains(
            $"CLR type '{typeof(string).FullName}', length {secretProviderValue.Length}");
        await Assert.That(exception.Message).DoesNotContain(secretProviderValue);
    }

    [Test]
    public async Task ToCanonicalProviderValue_PreservesCancellationAndRejectsUnsafeSourceLabel()
    {
        var cancellation = new OperationCanceledException("cancelled");
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            (_, _) => throw cancellation);
        var table = CreateSingleConvertedTable("cancelled_mutation_rows", converter);
        var column = table.Columns.Single();

        var thrownCancellation = Capture<OperationCanceledException>(() =>
            ModelValueConverter.ToCanonicalProviderValue(
                column,
                new MutationId(42),
                "mutation.insert"));
        var sourceException = Capture<ArgumentException>(() =>
            ModelValueConverter.ToCanonicalProviderValue(
                column,
                new MutationId(42),
                "mutation\r\nsecret"));

        await Assert.That(thrownCancellation).IsSameReferenceAs(cancellation);
        await Assert.That(sourceException.Message).Contains("non-sensitive diagnostic source label");
    }

    [Test]
    public async Task ToCanonicalProviderValue_ReportsUnresolvedConverterMetadataWithColumnContext()
    {
        var table = CreateUnresolvedConverterTable();

        var exception = Capture<ModelValueConversionException>(() =>
            ModelValueConverter.ToCanonicalProviderValue(
                table.Columns.Single(),
                new MutationId(42),
                "mutation.insert"));

        await Assert.That(exception.Column).IsSameReferenceAs(table.Columns.Single());
        await Assert.That(exception.ConverterType).IsEqualTo(typeof(RecordingScalarConverter));
        await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception.InnerException!.Message).Contains("unresolved at runtime");
    }

    [Test]
    public async Task StateChange_AllCrudPathsConvertModelValuesBeforePhysicalWriter()
    {
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value);
        var table = CreateMutationTable(converter);
        var idColumn = table.GetColumnByDbName("id");
        var valueColumn = table.GetColumnByDbName("value");

        var expectedCalls = new Dictionary<TransactionChangeType, (ColumnDefinition Column, object? Value)[]>
        {
            [TransactionChangeType.Insert] =
            [
                (idColumn, 7),
                (valueColumn, 8)
            ],
            [TransactionChangeType.Update] =
            [
                (idColumn, 7),
                (valueColumn, 8)
            ],
            [TransactionChangeType.Delete] =
            [
                (idColumn, 7)
            ]
        };

        foreach (var type in expectedCalls.Keys)
        {
            var writer = new RecordingPhysicalWriter();
            var provider = new RecordingProvider(table.Database, writer);
            var transaction = new Transaction(provider, TransactionType.WriteOnly);
            var values = new Dictionary<ColumnDefinition, object?>
            {
                [idColumn] = new MutationId(7),
                [valueColumn] = new MutationId(8)
            };
            var changes = type == TransactionChangeType.Update
                ? new[] { new KeyValuePair<ColumnDefinition, object?>(valueColumn, values[valueColumn]) }
                : [];
            var model = new TestMutableInstance(table, values, changes);
            var stateChange = new StateChange(model, table, type);

            _ = stateChange.GetQuery(transaction);

            var expected = expectedCalls[type];
            await Assert.That(writer.Calls.Count).IsEqualTo(expected.Length);
            for (var index = 0; index < expected.Length; index++)
            {
                await Assert.That(writer.Calls[index].Column).IsSameReferenceAs(expected[index].Column);
                await Assert.That(writer.Calls[index].CanonicalValue).IsEqualTo(expected[index].Value);
                await Assert.That(writer.Calls[index].CanonicalValue).IsTypeOf<int>();
            }
        }

        await Assert.That(converter.ToProviderCalls.Count).IsEqualTo(8);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    private static TableDefinition CreateSingleConvertedTable(
        string tableName,
        RecordingScalarConverter converter)
    {
        var draft = CreateDatabaseDraft(
            tableName,
            new MetadataValuePropertyDraft(
                "Value",
                new CsTypeDeclaration(converter.ModelType),
                new MetadataColumnDraft("value") { PrimaryKey = true })
            {
                ScalarConverter = CreateConverterDraft(converter)
            });

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;
    }

    private static TableDefinition CreateMutationTable(RecordingScalarConverter converter)
    {
        var draft = CreateDatabaseDraft(
            "scalar_mutation_rows",
            new MetadataValuePropertyDraft(
                "Id",
                new CsTypeDeclaration(typeof(MutationId)),
                new MetadataColumnDraft("id") { PrimaryKey = true })
            {
                ScalarConverter = CreateConverterDraft(converter)
            },
            new MetadataValuePropertyDraft(
                "Value",
                new CsTypeDeclaration(typeof(MutationId)),
                new MetadataColumnDraft("value"))
            {
                ScalarConverter = CreateConverterDraft(converter)
            });

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;
    }

    private static TableDefinition CreateAutoIncrementMutationTable(
        RecordingScalarConverter converter)
    {
        var draft = CreateDatabaseDraft(
            "generated_scalar_rows",
            new MetadataValuePropertyDraft(
                "Id",
                new CsTypeDeclaration(typeof(MutationId)),
                new MetadataColumnDraft("id")
                {
                    PrimaryKey = true,
                    AutoIncrement = true
                })
            {
                CsNullable = true,
                ScalarConverter = CreateConverterDraft(converter)
            });

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;
    }

    private static TableDefinition CreateIdentityTable()
    {
        var draft = CreateDatabaseDraft(
            "identity_mutation_rows",
            new MetadataValuePropertyDraft(
                "Value",
                new CsTypeDeclaration(typeof(int)),
                new MetadataColumnDraft("value") { PrimaryKey = true })
            {
                CsSize = sizeof(int)
            });

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;
    }

    private static TableDefinition CreateUnresolvedConverterTable()
    {
        var database = new DatabaseDefinition(
            "UnresolvedModelValueConverterDb",
            new CsTypeDeclaration(typeof(ModelValueConverterTests)));
        var model = new ModelDefinition(new CsTypeDeclaration(typeof(MutationRowModel)));
        var table = new TableDefinition("unresolved_mutation_rows");
        var tableModel = new TableModel("Rows", database, model, table);
        var property = new ValueProperty(
            "Value",
            new CsTypeDeclaration(typeof(MutationId)),
            model,
            Array.Empty<Attribute>());
        var column = new ColumnDefinition("value", table);
        column.SetIndexCore(0);
        column.SetValuePropertyCore(property);
        column.SetScalarMappingCore(ColumnScalarMapping.Converted(
            new CsTypeDeclaration(typeof(MutationId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(RecordingScalarConverter)),
            converter: null,
            ScalarConverterOrigin.Property));
        column.SetPrimaryKeyCore();
        model.AddPropertyCore(property);
        table.SetColumnsCore([column]);
        database.SetTableModelsCore([tableModel]);
        database.Freeze();
        return table;
    }

    private static MetadataScalarConverterDraft CreateConverterDraft(
        RecordingScalarConverter converter) =>
        new(
            new CsTypeDeclaration(converter.ModelType),
            new CsTypeDeclaration(converter.ProviderType),
            new CsTypeDeclaration(typeof(RecordingScalarConverter)),
            () => converter)
        {
            Origin = ScalarConverterOrigin.Property
        };

    private static MetadataDatabaseDraft CreateDatabaseDraft(
        string tableName,
        params MetadataValuePropertyDraft[] properties) =>
        new("ModelValueConverterDb", new CsTypeDeclaration(typeof(ModelValueConverterTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(MutationRowModel)))
                    {
                        ValueProperties = properties
                    },
                    new MetadataTableDraft(tableName))
            ]
        };

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

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }

    private sealed record MutationId(int Value);
    private sealed record BinaryModelId(byte[] Value);
    private sealed class MutationRowModel;

    private sealed class RecordingScalarConverter(
        Type modelType,
        Type providerType,
        Func<object?, ScalarConversionContext, object?> toProvider,
        Func<object?, ScalarConversionContext, object?>? fromProvider = null) : IDataLinqScalarConverter
    {
        public Type ModelType { get; } = modelType;
        public Type ProviderType { get; } = providerType;
        public List<(object? Value, ScalarConversionContext Context)> ToProviderCalls { get; } = [];
        public int FromProviderCalls { get; private set; }

        public object? ToProviderObject(object? modelValue, in ScalarConversionContext context)
        {
            ToProviderCalls.Add((modelValue, context));
            return toProvider(modelValue, context);
        }

        public object? FromProviderObject(object? providerValue, in ScalarConversionContext context)
        {
            FromProviderCalls++;
            return fromProvider is null
                ? throw new InvalidOperationException("The mutation path must not invoke provider-to-model conversion.")
                : fromProvider(providerValue, context);
        }
    }

    private sealed class RecordingPhysicalWriter : IDataLinqDataWriter
    {
        public List<(ColumnDefinition Column, object? CanonicalValue)> Calls { get; } = [];

        public object? ConvertValue(ColumnDefinition column, object? value)
        {
            Calls.Add((column, value));
            return new PhysicalValue(value);
        }
    }

    private sealed record PhysicalValue(object? Value);

    private sealed class TestMutableInstance(
        TableDefinition table,
        Dictionary<ColumnDefinition, object?> values,
        IReadOnlyList<KeyValuePair<ColumnDefinition, object?>> changes) : IMutableInstance
    {
        private bool deleted;
        private readonly TestRowData rowData = new(table, values);

        public object? this[string propertyName]
        {
            get => this[table.Model.ValueProperties[propertyName].Column];
            set => this[table.Model.ValueProperties[propertyName].Column] = value;
        }

        public object? this[ColumnDefinition column]
        {
            get => values[column];
            set => values[column] = value;
        }

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => values;
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, values[column]));
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges() => changes;
        public bool HasPrimaryKeysSet() =>
            table.PrimaryKeyColumns.All(column =>
                values.TryGetValue(column, out var value) && value is not null);
        public ModelDefinition Metadata() => table.Model;
        public DataLinqKey PrimaryKeys() => HasPrimaryKeysSet()
            ? KeyFactory.GetKey(rowData, table.PrimaryKeyColumns)
            : DataLinqKey.Null;
        public MutableRowData GetRowData() => throw new NotSupportedException();
        IRowData IModelInstance.GetRowData() => rowData;
        public bool IsNew() => false;
        public bool IsDeleted() => deleted;
        public void SetDeleted() => deleted = true;
        public void Reset() { }
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public void SetLazy<V>(string name, V value) { }
    }

    private sealed class TestRowData(
        TableDefinition table,
        IReadOnlyDictionary<ColumnDefinition, object?> values) : IRowData
    {
        public TableDefinition Table { get; } = table;
        public object? this[ColumnDefinition column] => values[column];
        public object? this[int columnIndex] => values[Table.Columns[columnIndex]];
        public object? GetValue(ColumnDefinition column) => values[column];
        public object? GetValue(int columnIndex) => values[Table.Columns[columnIndex]];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => values[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() => values;
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, values[column]));
    }

    private sealed class RecordingProvider(
        DatabaseDefinition metadata,
        RecordingPhysicalWriter writer,
        object? generatedValue = null) : IDatabaseProvider
    {
        public string TelemetryInstanceId => "model-value-converter-tests";
        public string DatabaseName => metadata.DbName;
        public string ConnectionString => throw new NotSupportedException();
        public DatabaseDefinition Metadata => metadata;
        public DatabaseAccess DatabaseAccess => throw new NotSupportedException();
        public State State => throw new NotSupportedException();
        public IDatabaseProviderConstants Constants => throw new NotSupportedException();
        public ReadOnlyAccess ReadOnlyAccess => new(this);
        public DatabaseType DatabaseType => DatabaseType.SQLite;
        public IDbCommand ToDbCommand(IQuery query) => new TestDbCommand();
        public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite) =>
            new(this, transactionType);
        public DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) =>
            new RecordingDatabaseTransaction(this, type, generatedValue);
        public DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) =>
            throw new NotSupportedException();
        public string GetLastIdQuery() => throw new NotSupportedException();
        public string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) =>
            throw new NotSupportedException();
        public TableCache GetTableCache(TableDefinition table) => throw new NotSupportedException();
        public string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) =>
            throw new NotSupportedException();
        public Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public bool FileOrServerExists() => throw new NotSupportedException();
        public IDataLinqDataWriter GetWriter() => writer;
        public Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public M Commit<M>(Func<Transaction, M> func) => throw new NotSupportedException();
        public void Commit(Action<Transaction> action) => throw new NotSupportedException();
        public bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public IDbConnection GetDbConnection() => throw new NotSupportedException();
        public Sql GetCreateSql() => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class RecordingDatabaseTransaction(
        IDatabaseProvider provider,
        TransactionType type,
        object? generatedValue) : DatabaseTransaction(provider, type)
    {
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => generatedValue;
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
        public override void Rollback() { }
        public override void Commit() { }
        public override void Dispose() { }
    }

    private sealed class TestDbCommand : IDbCommand
    {
        [AllowNull]
        public string CommandText { get; set; } = string.Empty;
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; }
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters => throw new NotSupportedException();
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public void Cancel() => throw new NotSupportedException();
        public IDbDataParameter CreateParameter() => throw new NotSupportedException();
        public int ExecuteNonQuery() => throw new NotSupportedException();
        public IDataReader ExecuteReader() => throw new NotSupportedException();
        public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
        public object? ExecuteScalar() => throw new NotSupportedException();
        public void Prepare() => throw new NotSupportedException();
        public void Dispose() { }
    }
}
