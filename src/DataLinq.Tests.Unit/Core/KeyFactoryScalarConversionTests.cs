using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class KeyFactoryScalarConversionTests
{
    [Test]
    public async Task GetKey_RowAndModel_NormalizeTypedScalarToCanonicalProviderIdentity()
    {
        var converter = new RecordingKeyConverter(
            typeof(TypedKeyId),
            typeof(int),
            static value => ((TypedKeyId)value!).Value);
        var table = CreateScalarTable(converter);
        var column = table.PrimaryKeyColumns.Single();
        var modelValue = new TypedKeyId(42);
        var values = new Dictionary<ColumnDefinition, object?> { [column] = modelValue };
        var row = new TestRowData(table, values);
        var model = new TestModelInstance(row);

        var rowKey = KeyFactory.GetKey(row, table.PrimaryKeyColumns);
        var modelKey = KeyFactory.GetKey(model, table.PrimaryKeyColumns);
        var publicModelKey = KeyFactory.CreateKeyFromModelValue(modelValue, column);
        var canonicalKey = DataLinqKey.FromValue(42);
        var rawUnscopedKey = KeyFactory.CreateKeyFromValue(modelValue);
        var cache = new Dictionary<DataLinqKey, string> { [canonicalKey] = "cached" };

        await Assert.That(rowKey).IsEqualTo(canonicalKey);
        await Assert.That(modelKey).IsEqualTo(canonicalKey);
        await Assert.That(publicModelKey).IsEqualTo(canonicalKey);
        await Assert.That(rowKey.GetValue(0)).IsTypeOf<int>();
        await Assert.That(modelKey.GetValue(0)).IsTypeOf<int>();
        await Assert.That(cache.TryGetValue(rowKey, out var rowHit)).IsTrue();
        await Assert.That(rowHit).IsEqualTo("cached");
        await Assert.That(cache.TryGetValue(modelKey, out var modelHit)).IsTrue();
        await Assert.That(modelHit).IsEqualTo("cached");
        await Assert.That(rawUnscopedKey).IsNotEqualTo(canonicalKey);
        await Assert.That(rawUnscopedKey.GetValue(0)).IsSameReferenceAs(modelValue);
        await Assert.That(converter.ToProviderCalls.Count).IsEqualTo(3);
        await Assert.That(converter.ToProviderCalls.All(call => ReferenceEquals(call.Context.Column, column))).IsTrue();
        await Assert.That(table.PrimaryKeyShape.HasScalarConverter).IsTrue();
        await Assert.That(table.PrimaryKeyShape[0].ProviderStoreKind).IsEqualTo(TableKeyComponentStoreKind.Unsupported);
    }

    [Test]
    public async Task GetKey_CompositeModelValues_NormalizesConvertedComponentsAndOwnsBinaryIdentity()
    {
        var idConverter = new RecordingKeyConverter(
            typeof(TypedKeyId),
            typeof(int),
            static value => ((TypedKeyId)value!).Value);
        var binaryConverter = new BinaryRecordingKeyConverter(
            static value => ((BinaryKeyId)value!).Value);
        var table = CreateCompositeTable(idConverter, binaryConverter);
        var idColumn = table.GetColumnByDbName("id");
        var tenantColumn = table.GetColumnByDbName("tenant");
        var binaryColumn = table.GetColumnByDbName("binary_id");
        var binaryBytes = new byte[] { 1, 2, 3 };
        var values = new Dictionary<ColumnDefinition, object?>
        {
            [idColumn] = new TypedKeyId(42),
            [tenantColumn] = "tenant-1",
            [binaryColumn] = new BinaryKeyId(binaryBytes)
        };
        var row = new TestRowData(table, values);

        var key = KeyFactory.GetKey(row, table.PrimaryKeyColumns);
        var publicKey = KeyFactory.CreateKeyFromModelValues(
            [values[idColumn], values[tenantColumn], values[binaryColumn]],
            table.PrimaryKeyColumns);
        var canonicalKey = DataLinqKey.FromValues([42, "tenant-1", new byte[] { 1, 2, 3 }]);

        binaryBytes[0] = 9;
        var exposedBytes = (byte[])key.GetValue(2)!;
        exposedBytes[1] = 9;

        await Assert.That(key).IsEqualTo(canonicalKey);
        await Assert.That(publicKey).IsEqualTo(canonicalKey);
        await Assert.That(key.GetValue(0)).IsEqualTo(42);
        await Assert.That(key.GetValue(1)).IsEqualTo("tenant-1");
        await Assert.That((byte[])key.GetValue(2)!).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(idConverter.ToProviderCalls.Count).IsEqualTo(2);
        await Assert.That(binaryConverter.ToProviderCalls.Count).IsEqualTo(2);
        await Assert.That(idConverter.ToProviderCalls[0].Context.Column).IsSameReferenceAs(idColumn);
        await Assert.That(binaryConverter.ToProviderCalls[0].Context.Column).IsSameReferenceAs(binaryColumn);
    }

    [Test]
    public async Task GetKey_UnsetConvertedAutoIncrementValue_RemainsNullWithoutConverterCall()
    {
        var converter = new RecordingKeyConverter(
            typeof(TypedKeyId),
            typeof(int),
            static value => ((TypedKeyId)value!).Value);
        var table = CreateScalarTable(converter, autoIncrement: true);
        var column = table.PrimaryKeyColumns.Single();
        var row = new TestRowData(
            table,
            new Dictionary<ColumnDefinition, object?> { [column] = null });

        var key = KeyFactory.GetKey(row, table.PrimaryKeyColumns);

        await Assert.That(key).IsEqualTo(DataLinqKey.Null);
        await Assert.That(converter.ToProviderCalls).IsEmpty();
    }

    [Test]
    public async Task GetKey_ConversionFailureIdentifiesRowOrModelSourceWithoutProducingKey()
    {
        var converter = new RecordingKeyConverter(
            typeof(TypedKeyId),
            typeof(int),
            static _ => "wrong-provider-type");
        var table = CreateScalarTable(converter);
        var column = table.PrimaryKeyColumns.Single();
        var values = new Dictionary<ColumnDefinition, object?>
        {
            [column] = new TypedKeyId(42)
        };
        var row = new TestRowData(table, values);
        var model = new TestModelInstance(row);

        var rowException = Capture<ModelValueConversionException>(() =>
            KeyFactory.GetKey(row, table.PrimaryKeyColumns));
        var modelException = Capture<ModelValueConversionException>(() =>
            KeyFactory.GetKey(model, table.PrimaryKeyColumns));

        await Assert.That(rowException.SourceName).IsEqualTo("key.row");
        await Assert.That(modelException.SourceName).IsEqualTo("key.model");
        await Assert.That(rowException.Column).IsSameReferenceAs(column);
        await Assert.That(modelException.Column).IsSameReferenceAs(column);
    }

    [Test]
    public async Task GetKey_SameClrTypeConverterStillRunsExactlyOnce()
    {
        var converter = new RecordingKeyConverter(
            typeof(int),
            typeof(int),
            static value => (int)value! + 100);
        var table = CreateSameTypeScalarTable(converter);
        var column = table.PrimaryKeyColumns.Single();
        var row = new TestRowData(
            table,
            new Dictionary<ColumnDefinition, object?> { [column] = 42 });

        var key = KeyFactory.GetKey(row, table.PrimaryKeyColumns);

        await Assert.That(key).IsEqualTo(DataLinqKey.FromValue(142));
        await Assert.That(converter.ToProviderCalls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CreateKeyFromModelValues_RejectsMismatchedValueAndColumnCounts()
    {
        var converter = new RecordingKeyConverter(
            typeof(TypedKeyId),
            typeof(int),
            static value => ((TypedKeyId)value!).Value);
        var table = CreateScalarTable(converter);

        var exception = Capture<ArgumentException>(() =>
            KeyFactory.CreateKeyFromModelValues(
                [new TypedKeyId(42), new TypedKeyId(43)],
                table.PrimaryKeyColumns));

        await Assert.That(exception.ParamName).IsEqualTo("modelValues");
        await Assert.That(converter.ToProviderCalls).IsEmpty();
    }

    private static TableDefinition CreateScalarTable(
        RecordingKeyConverter converter,
        bool autoIncrement = false)
    {
        var draft = CreateDatabaseDraft(
            "scalar_key_rows",
            new MetadataValuePropertyDraft(
                "Id",
                new CsTypeDeclaration(typeof(TypedKeyId)),
                new MetadataColumnDraft("id")
                {
                    PrimaryKey = true,
                    AutoIncrement = autoIncrement
                })
            {
                CsNullable = autoIncrement,
                ScalarConverter = CreateConverterDraft(converter)
            });

        return BuildTable(draft);
    }

    private static TableDefinition CreateCompositeTable(
        RecordingKeyConverter idConverter,
        RecordingKeyConverter binaryConverter)
    {
        var draft = CreateDatabaseDraft(
            "composite_key_rows",
            new MetadataValuePropertyDraft(
                "Id",
                new CsTypeDeclaration(typeof(TypedKeyId)),
                new MetadataColumnDraft("id") { PrimaryKey = true })
            {
                ScalarConverter = CreateConverterDraft(idConverter)
            },
            new MetadataValuePropertyDraft(
                "Tenant",
                new CsTypeDeclaration(typeof(string)),
                new MetadataColumnDraft("tenant") { PrimaryKey = true }),
            new MetadataValuePropertyDraft(
                "BinaryId",
                new CsTypeDeclaration(typeof(BinaryKeyId)),
                new MetadataColumnDraft("binary_id") { PrimaryKey = true })
            {
                ScalarConverter = CreateConverterDraft(binaryConverter)
            });

        return BuildTable(draft);
    }

    private static TableDefinition CreateSameTypeScalarTable(
        RecordingKeyConverter converter)
    {
        var draft = CreateDatabaseDraft(
            "same_type_key_rows",
            new MetadataValuePropertyDraft(
                "Id",
                new CsTypeDeclaration(typeof(int)),
                new MetadataColumnDraft("id") { PrimaryKey = true })
            {
                ScalarConverter = CreateConverterDraft(converter)
            });

        return BuildTable(draft);
    }

    private static MetadataScalarConverterDraft CreateConverterDraft(
        RecordingKeyConverter converter) =>
        new(
            new CsTypeDeclaration(converter.ModelType),
            new CsTypeDeclaration(converter.ProviderType),
            new CsTypeDeclaration(converter.GetType()),
            () => converter)
        {
            Origin = ScalarConverterOrigin.Property
        };

    private static MetadataDatabaseDraft CreateDatabaseDraft(
        string tableName,
        params MetadataValuePropertyDraft[] properties) =>
        new("KeyFactoryScalarDb", new CsTypeDeclaration(typeof(KeyFactoryScalarConversionTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(KeyFactoryScalarRow)))
                    {
                        ValueProperties = properties
                    },
                    new MetadataTableDraft(tableName))
            ]
        };

    private static TableDefinition BuildTable(MetadataDatabaseDraft draft) =>
        new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table;

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

    private sealed record TypedKeyId(int Value);
    private sealed record BinaryKeyId(byte[] Value);
    private sealed class KeyFactoryScalarRow;

    private class RecordingKeyConverter(
        Type modelType,
        Type providerType,
        Func<object?, object?> toProvider) : IDataLinqScalarConverter
    {
        public Type ModelType { get; } = modelType;
        public Type ProviderType { get; } = providerType;
        public List<(object? Value, ScalarConversionContext Context)> ToProviderCalls { get; } = [];

        public object? ToProviderObject(object? modelValue, in ScalarConversionContext context)
        {
            ToProviderCalls.Add((modelValue, context));
            return toProvider(modelValue);
        }

        public object? FromProviderObject(object? providerValue, in ScalarConversionContext context) =>
            throw new NotSupportedException();
    }

    private sealed class BinaryRecordingKeyConverter(Func<object?, object?> toProvider)
        : RecordingKeyConverter(typeof(BinaryKeyId), typeof(byte[]), toProvider);

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

    private sealed class TestModelInstance(TestRowData rowData) : IModelInstance
    {
        public object? this[string propertyName] =>
            rowData[rowData.Table.Model.ValueProperties[propertyName].Column];
        public object? this[ColumnDefinition column] => rowData[column];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() =>
            rowData.GetColumnAndValues();
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) =>
            rowData.GetColumnAndValues(columns);
        public bool HasPrimaryKeysSet() => !PrimaryKeys().IsNull;
        public ModelDefinition Metadata() => rowData.Table.Model;
        public DataLinqKey PrimaryKeys() => KeyFactory.GetKey(rowData, rowData.Table.PrimaryKeyColumns);
        public IRowData GetRowData() => rowData;
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
    }
}
