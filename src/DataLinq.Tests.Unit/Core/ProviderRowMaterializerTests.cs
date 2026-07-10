using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class ProviderRowMaterializerTests
{
    [Test]
    public async Task Materialize_ConvertsOnceAndPreservesIdentityNullAndBinaryOwnership()
    {
        var converter = new RecordingScalarConverter(
            typeof(MaterializedReferenceId),
            typeof(int),
            static (value, _) => new DerivedMaterializedReferenceId((int)value!));
        var table = CreateMixedTable(converter);
        var identityColumn = table.GetColumnByDbName("identity_value");
        var convertedColumn = table.GetColumnByDbName("converted_value");
        var nullableColumn = table.GetColumnByDbName("nullable_converted_value");
        var payloadColumn = table.GetColumnByDbName("payload");
        var payload = new byte[] { 1, 2, 3 };
        var providerRow = CanonicalProviderValueRow.Create(
            table,
            new object?[] { 7, 42, null, payload });

        payload[0] = 9;
        var rowData = ProviderRowMaterializer.Materialize(providerRow, "memory");
        var borrowedProviderPayload = (byte[])providerRow[payloadColumn]!;
        borrowedProviderPayload[0] = 8;
        var modelPayload = (byte[])rowData[payloadColumn]!;
        modelPayload[0] = 6;

        await Assert.That(rowData[identityColumn]).IsEqualTo(7);
        await Assert.That(rowData[convertedColumn]).IsTypeOf<DerivedMaterializedReferenceId>();
        await Assert.That(((MaterializedReferenceId)rowData[convertedColumn]!).Value).IsEqualTo(42);
        await Assert.That(rowData[nullableColumn]).IsNull();
        await Assert.That(((byte[])providerRow[payloadColumn]!)[0]).IsEqualTo((byte)1);
        await Assert.That(converter.FromProviderCalls.Count).IsEqualTo(1);
        await Assert.That(converter.FromProviderCalls[0].Value).IsEqualTo(42);
        await Assert.That(converter.FromProviderCalls[0].Context.Column).IsSameReferenceAs(convertedColumn);
        await Assert.That(converter.ToProviderCalls).IsEqualTo(0);
        await Assert.That(convertedColumn.ScalarConverter).IsSameReferenceAs(nullableColumn.ScalarConverter);
    }

    [Test]
    public async Task Materialize_AllowsConverterNullOnlyForNullableModelColumn()
    {
        var nullableConverter = new RecordingScalarConverter(
            typeof(MaterializedReferenceId),
            typeof(int),
            static (_, _) => null);
        var nullableTable = CreateSingleConvertedTable(
            "nullable_model_rows",
            nullableConverter,
            providerNullable: false,
            modelNullable: true);
        var nullableProviderRow = CanonicalProviderValueRow.Create(nullableTable, new object?[] { 42 });

        var nullableRow = ProviderRowMaterializer.Materialize(nullableProviderRow, "memory");

        var requiredConverter = new RecordingScalarConverter(
            typeof(MaterializedReferenceId),
            typeof(int),
            static (_, _) => null);
        var requiredTable = CreateSingleConvertedTable(
            "required_model_rows",
            requiredConverter,
            providerNullable: false,
            modelNullable: false);
        var requiredProviderRow = CanonicalProviderValueRow.Create(requiredTable, new object?[] { 42 });
        var exception = Capture<ProviderValueMaterializationException>(() =>
            ProviderRowMaterializer.Materialize(requiredProviderRow, "memory"));

        await Assert.That(nullableRow[0]).IsNull();
        await Assert.That(nullableConverter.FromProviderCalls.Count).IsEqualTo(1);
        await Assert.That(requiredConverter.FromProviderCalls.Count).IsEqualTo(1);
        await Assert.That(exception.InnerException).IsTypeOf<ArgumentException>();
        await Assert.That(exception.Message).Contains("Produced model value context: null");
        await Assert.That(exception.Message).Contains("required_model_rows.value");
    }

    [Test]
    public async Task Materialize_WrapsWrongConverterResultWithoutRenderingContents()
    {
        const string secretResult = "wrong-secret-result";
        var converter = new RecordingScalarConverter(
            typeof(MaterializedReferenceId),
            typeof(int),
            static (_, _) => secretResult);
        var table = CreateSingleConvertedTable("wrong_result_rows", converter);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });

        var exception = Capture<ProviderValueMaterializationException>(() =>
            ProviderRowMaterializer.Materialize(providerRow, "sql"));

        await Assert.That(exception.Column).IsSameReferenceAs(table.Columns.Single());
        await Assert.That(exception.ConverterType).IsEqualTo(typeof(RecordingScalarConverter));
        await Assert.That(exception.SourceName).IsEqualTo("sql");
        await Assert.That(exception.InnerException).IsTypeOf<ArgumentException>();
        await Assert.That(exception.Message).Contains(typeof(int).FullName!);
        await Assert.That(exception.Message).Contains(typeof(MaterializedReferenceId).FullName!);
        await Assert.That(exception.Message).Contains(typeof(RecordingScalarConverter).FullName!);
        await Assert.That(exception.Message).Contains($"CLR type '{typeof(string).FullName}', length {secretResult.Length}");
        await Assert.That(exception.Message).DoesNotContain(secretResult);
    }

    [Test]
    public async Task Materialize_WrapsConverterFailureWithSafeProviderContext()
    {
        const string secretProviderValue = "provider-secret";
        var expectedInner = new InvalidOperationException("converter failed");
        var converter = new RecordingScalarConverter(
            typeof(SecretMaterializedValue),
            typeof(string),
            (_, _) => throw expectedInner);
        var table = CreateSingleConvertedTable("throwing_converter_rows", converter);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { secretProviderValue });

        var exception = Capture<ProviderValueMaterializationException>(() =>
            ProviderRowMaterializer.Materialize(providerRow, "memory"));

        await Assert.That(exception.InnerException).IsSameReferenceAs(expectedInner);
        await Assert.That(exception.Message).Contains($"CLR type '{typeof(string).FullName}', length {secretProviderValue.Length}");
        await Assert.That(exception.Message).Contains("Produced model value context: not produced");
        await Assert.That(exception.Message).DoesNotContain(secretProviderValue);
    }

    [Test]
    public async Task Materialize_ReportsUnresolvedConverterMetadataWithColumnContext()
    {
        var table = CreateUnresolvedConverterTable();
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });

        var exception = Capture<ProviderValueMaterializationException>(() =>
            ProviderRowMaterializer.Materialize(providerRow, "memory"));

        await Assert.That(exception.Column).IsSameReferenceAs(table.Columns.Single());
        await Assert.That(exception.ConverterType).IsEqualTo(typeof(RecordingScalarConverter));
        await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception.InnerException!.Message).Contains("unresolved at runtime");
    }

    [Test]
    public async Task Materialize_RejectsUnsafeSourceLabelsAndPreservesCancellation()
    {
        var identityTable = CreateIdentityTable();
        var identityRow = CanonicalProviderValueRow.Create(identityTable, new object?[] { 42 });
        var sourceException = Capture<ArgumentException>(() =>
            ProviderRowMaterializer.Materialize(identityRow, "sql\r\nconnection=secret"));

        var cancellation = new OperationCanceledException("cancelled");
        var converter = new RecordingScalarConverter(
            typeof(MaterializedReferenceId),
            typeof(int),
            (_, _) => throw cancellation);
        var convertedTable = CreateSingleConvertedTable("cancelled_converter_rows", converter);
        var convertedRow = CanonicalProviderValueRow.Create(convertedTable, new object?[] { 42 });
        var thrownCancellation = Capture<OperationCanceledException>(() =>
            ProviderRowMaterializer.Materialize(convertedRow, "memory"));

        await Assert.That(sourceException.Message).Contains("non-sensitive diagnostic source label");
        await Assert.That(thrownCancellation).IsSameReferenceAs(cancellation);
    }

    private static TableDefinition CreateMixedTable(RecordingScalarConverter converter)
    {
        var mapping = CreateConverterDraft(converter);
        var draft = CreateDatabaseDraft(
            "mixed_materializer_rows",
            new MetadataValuePropertyDraft(
                "IdentityValue",
                new CsTypeDeclaration(typeof(int)),
                new MetadataColumnDraft("identity_value") { PrimaryKey = true })
            {
                CsSize = sizeof(int)
            },
            new MetadataValuePropertyDraft(
                "ConvertedValue",
                new CsTypeDeclaration(typeof(MaterializedReferenceId)),
                new MetadataColumnDraft("converted_value"))
            {
                ScalarConverter = mapping
            },
            new MetadataValuePropertyDraft(
                "NullableConvertedValue",
                new CsTypeDeclaration(typeof(MaterializedReferenceId)),
                new MetadataColumnDraft("nullable_converted_value") { Nullable = true })
            {
                CsNullable = true,
                ScalarConverter = mapping
            },
            new MetadataValuePropertyDraft(
                "Payload",
                new CsTypeDeclaration(typeof(byte[])),
                new MetadataColumnDraft("payload")));

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private static TableDefinition CreateSingleConvertedTable(
        string tableName,
        RecordingScalarConverter converter,
        bool providerNullable = false,
        bool modelNullable = false)
    {
        var draft = CreateDatabaseDraft(
            tableName,
            new MetadataValuePropertyDraft(
                "Value",
                new CsTypeDeclaration(converter.ModelType),
                new MetadataColumnDraft("value")
                {
                    PrimaryKey = true,
                    Nullable = providerNullable
                })
            {
                CsNullable = modelNullable,
                ScalarConverter = CreateConverterDraft(converter)
            });

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private static TableDefinition CreateIdentityTable()
    {
        var draft = CreateDatabaseDraft(
            "identity_materializer_rows",
            new MetadataValuePropertyDraft(
                "Value",
                new CsTypeDeclaration(typeof(int)),
                new MetadataColumnDraft("value") { PrimaryKey = true })
            {
                CsSize = sizeof(int)
            });

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private static MetadataScalarConverterDraft CreateConverterDraft(RecordingScalarConverter converter) =>
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
        new("ProviderRowMaterializerDb", new CsTypeDeclaration(typeof(ProviderRowMaterializerTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(MaterializerRowModel)))
                    {
                        ValueProperties = properties
                    },
                    new MetadataTableDraft(tableName))
            ]
        };

    private static TableDefinition CreateUnresolvedConverterTable()
    {
        var database = new DatabaseDefinition(
            "UnresolvedMaterializerDb",
            new CsTypeDeclaration(typeof(ProviderRowMaterializerTests)));
        var model = new ModelDefinition(new CsTypeDeclaration(typeof(MaterializerRowModel)));
        var table = new TableDefinition("unresolved_converter_rows");
        var tableModel = new TableModel("Rows", database, model, table);
        var property = new ValueProperty(
            "Value",
            new CsTypeDeclaration(typeof(MaterializedReferenceId)),
            model,
            Array.Empty<Attribute>());
        var column = new ColumnDefinition("value", table);
        column.SetIndexCore(0);
        column.SetValuePropertyCore(property);
        column.SetScalarMappingCore(ColumnScalarMapping.Converted(
            new CsTypeDeclaration(typeof(MaterializedReferenceId)),
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

    private sealed class RecordingScalarConverter(
        Type modelType,
        Type providerType,
        Func<object?, ScalarConversionContext, object?> fromProvider) : IDataLinqScalarConverter
    {
        public Type ModelType { get; } = modelType;
        public Type ProviderType { get; } = providerType;
        public List<(object? Value, ScalarConversionContext Context)> FromProviderCalls { get; } = [];
        public int ToProviderCalls { get; private set; }

        public object? ToProviderObject(object? modelValue, in ScalarConversionContext context)
        {
            ToProviderCalls++;
            return modelValue;
        }

        public object? FromProviderObject(object? providerValue, in ScalarConversionContext context)
        {
            FromProviderCalls.Add((providerValue, context));
            return fromProvider(providerValue, context);
        }
    }

    private abstract class MaterializedReferenceId(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class DerivedMaterializedReferenceId(int value) : MaterializedReferenceId(value);

    private sealed record SecretMaterializedValue(string Value);

    private sealed class MaterializerRowModel;
}
