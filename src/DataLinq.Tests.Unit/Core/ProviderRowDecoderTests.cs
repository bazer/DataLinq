using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class ProviderRowDecoderTests
{
    [Test]
    public async Task DecodeFullRow_ProducesCanonicalValuesBeforeScalarMaterialization()
    {
        var converter = new RecordingIdConverter();
        var table = CreateTable(converter);
        var reader = new RecordingReader([42, "Ada"]);

        var canonicalRow = ProviderRowDecoder.DecodeFullRow(reader, table, "sql:test");

        await Assert.That(canonicalRow[table.GetColumnByDbName("id")]).IsEqualTo(42);
        await Assert.That(canonicalRow[table.GetColumnByDbName("id")]).IsTypeOf<int>();
        await Assert.That(canonicalRow[table.GetColumnByDbName("name")]).IsEqualTo("Ada");
        await Assert.That(reader.Int32Reads).IsEqualTo(1);
        await Assert.That(reader.GenericColumnReads).IsEqualTo(1);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);

        var modelRow = ProviderRowMaterializer.Materialize(canonicalRow, "sql:test");

        await Assert.That(modelRow[table.GetColumnByDbName("id")]).IsEqualTo(new ModelId(42));
        await Assert.That(modelRow[table.GetColumnByDbName("name")]).IsEqualTo("Ada");
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
    }

    [Test]
    public async Task DecodeFullRow_WrapsPhysicalFailuresWithoutValueContent()
    {
        var converter = new RecordingIdConverter();
        var table = CreateTable(converter);
        var physicalFailure = new FormatException("secret physical payload");
        var reader = new RecordingReader([42, "Ada"])
        {
            Int32Failure = physicalFailure
        };

        var exception = Capture<ProviderValueDecodingException>(() =>
            ProviderRowDecoder.DecodeFullRow(reader, table, "sql:test"));

        await Assert.That(exception.InnerException).IsSameReferenceAs(physicalFailure);
        await Assert.That(exception.Column).IsSameReferenceAs(table.GetColumnByDbName("id"));
        await Assert.That(exception.SourceName).IsEqualTo("sql:test");
        await Assert.That(exception.Message).Contains("materialization_rows.id");
        await Assert.That(exception.Message).Contains("canonical provider CLR type 'System.Int32'");
        await Assert.That(exception.Message).Contains("Decoded value context: not decoded");
        await Assert.That(exception.Message).DoesNotContain("secret physical payload");
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DecodeFullRow_DoesNotWrapCancellationOrFatalFailures()
    {
        var table = CreateTable(new RecordingIdConverter());
        var cancellation = new OperationCanceledException(
            "cancelled",
            innerException: null,
            new CancellationToken(canceled: true));
        var reader = new RecordingReader([42, "Ada"])
        {
            Int32Failure = cancellation
        };

        var thrown = Capture<OperationCanceledException>(() =>
            ProviderRowDecoder.DecodeFullRow(reader, table, "sql:test"));

        await Assert.That(thrown).IsSameReferenceAs(cancellation);
    }

    [Test]
    public async Task DecodeFullRow_UsesColumnAwareReadForConvertedNonPrimaryGuid()
    {
        var converter = new RecordingGuidIdConverter();
        var table = CreateGuidTable(converter, guidIsPrimaryKey: false);
        var expected = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var reader = new RecordingReader([7, expected]);

        var canonicalRow = ProviderRowDecoder.DecodeFullRow(reader, table, "sql:test");
        var guidColumn = table.GetColumnByDbName("external_id");

        await Assert.That(canonicalRow[guidColumn]).IsEqualTo(expected);
        await Assert.That(canonicalRow[guidColumn]).IsTypeOf<Guid>();
        await Assert.That(reader.GenericColumnReads).IsEqualTo(2);
        await Assert.That(reader.GuidReads).IsEqualTo(0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);

        var modelRow = ProviderRowMaterializer.Materialize(canonicalRow, "sql:test");

        await Assert.That(modelRow[guidColumn]).IsEqualTo(new GuidModelId(expected));
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
    }

    [Test]
    public async Task DecodeCanonicalValue_KeepsMetadataFreeGuidReadOutsideFullRows()
    {
        var converter = new RecordingGuidIdConverter();
        var table = CreateGuidTable(converter, guidIsPrimaryKey: false);
        var expected = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var reader = new RecordingReader([7, expected]);
        var guidColumn = table.GetColumnByDbName("external_id");

        var canonicalValue = ProviderRowDecoder.DecodeCanonicalValue(
            reader,
            guidColumn,
            ordinal: 1,
            sourceName: "projection:test");

        await Assert.That(canonicalValue).IsEqualTo(expected);
        await Assert.That(reader.GenericColumnReads).IsEqualTo(0);
        await Assert.That(reader.GuidReads).IsEqualTo(1);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DecodeFullRow_UsesColumnAwareGuidReadForConvertedPrimaryKey()
    {
        var converter = new RecordingGuidIdConverter();
        var table = CreateGuidTable(converter, guidIsPrimaryKey: true);
        var expected = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var reader = new RecordingReader([expected, "Ada"]);

        var canonicalRow = ProviderRowDecoder.DecodeFullRow(reader, table, "sql:test");
        var guidColumn = table.GetColumnByDbName("external_id");

        await Assert.That(canonicalRow[guidColumn]).IsEqualTo(expected);
        await Assert.That(reader.GuidReads).IsEqualTo(0);
        await Assert.That(reader.GenericColumnReads).IsEqualTo(2);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DecodeFullRow_NullConvertedGuidBypassesReaderAndConverter()
    {
        var converter = new RecordingGuidIdConverter();
        var table = CreateGuidTable(converter, guidIsPrimaryKey: false);
        var reader = new RecordingReader([7, null]);
        var guidColumn = table.GetColumnByDbName("external_id");

        var canonicalRow = ProviderRowDecoder.DecodeFullRow(reader, table, "sql:test");
        var modelRow = ProviderRowMaterializer.Materialize(canonicalRow, "sql:test");

        await Assert.That(canonicalRow[guidColumn]).IsNull();
        await Assert.That(modelRow[guidColumn]).IsNull();
        await Assert.That(reader.GenericColumnReads).IsEqualTo(1);
        await Assert.That(reader.GuidReads).IsEqualTo(0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    [Test]
    public async Task KeyFactory_ScalarConvertedReaderKeyStaysCanonicalWithoutModelConversion()
    {
        var converter = new RecordingIdConverter();
        var table = CreateTable(converter, primaryKeyAfterName: true);
        var reader = new RecordingReader([42]);

        var key = KeyFactory.GetKey(reader, table.PrimaryKeyColumns);

        await Assert.That(key.GetValue(0)).IsEqualTo(42);
        await Assert.That(key.GetValue(0)).IsTypeOf<int>();
        await Assert.That(reader.Int32Reads).IsEqualTo(1);
        await Assert.That(reader.GenericColumnReads).IsEqualTo(0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(table.PrimaryKeyColumns[0].Index).IsEqualTo(1);
    }

    [Test]
    public async Task KeyFactory_ScalarConvertedGuidReaderKeyUsesColumnAwareDecoding()
    {
        var converter = new RecordingGuidIdConverter();
        var table = CreateGuidTable(converter, guidIsPrimaryKey: true);
        var expected = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var reader = new RecordingReader([expected]);

        var key = KeyFactory.GetKey(reader, table.PrimaryKeyColumns);

        await Assert.That(key.GetValue(0)).IsEqualTo(expected);
        await Assert.That(key.GetValue(0)).IsTypeOf<Guid>();
        await Assert.That(reader.GenericColumnReads).IsEqualTo(1);
        await Assert.That(reader.GuidReads).IsEqualTo(0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    [Test]
    public async Task KeyFactory_ScalarConvertedReaderFailureKeepsKeySelectionContext()
    {
        var converter = new RecordingIdConverter();
        var table = CreateTable(converter, primaryKeyAfterName: true);
        var physicalFailure = new FormatException("secret key payload");
        var reader = new RecordingReader([42])
        {
            Int32Failure = physicalFailure
        };

        var exception = Capture<ProviderValueDecodingException>(() =>
            KeyFactory.GetKey(reader, table.PrimaryKeyColumns));

        await Assert.That(exception.InnerException).IsSameReferenceAs(physicalFailure);
        await Assert.That(exception.SourceName).IsEqualTo("reader.key-selection");
        await Assert.That(exception.Column).IsSameReferenceAs(table.PrimaryKeyColumns[0]);
        await Assert.That(exception.Message).DoesNotContain("secret key payload");
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    private static TableDefinition CreateTable(
        RecordingIdConverter converter,
        bool primaryKeyAfterName = false)
    {
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(ModelId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(RecordingIdConverter)),
            () => converter)
        {
            Origin = ScalarConverterOrigin.Property
        };
        var idProperty = new MetadataValuePropertyDraft(
            "Id",
            new CsTypeDeclaration(typeof(ModelId)),
            new MetadataColumnDraft("id") { PrimaryKey = true })
        {
            ScalarConverter = scalarConverter
        };
        var nameProperty = new MetadataValuePropertyDraft(
            "Name",
            new CsTypeDeclaration(typeof(string)),
            new MetadataColumnDraft("name"));
        var draft = new MetadataDatabaseDraft(
            "ProviderRowDecoderDb",
            new CsTypeDeclaration(typeof(ProviderRowDecoderTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(DecoderRowModel)))
                    {
                        ValueProperties = primaryKeyAfterName
                            ? [nameProperty, idProperty]
                            : [idProperty, nameProperty]
                    },
                    new MetadataTableDraft("materialization_rows"))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels[0]
            .Table;
    }

    private static TableDefinition CreateGuidTable(
        RecordingGuidIdConverter converter,
        bool guidIsPrimaryKey)
    {
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(GuidModelId)),
            new CsTypeDeclaration(typeof(Guid)),
            new CsTypeDeclaration(typeof(RecordingGuidIdConverter)),
            () => converter)
        {
            Origin = ScalarConverterOrigin.Property
        };
        var guidProperty = new MetadataValuePropertyDraft(
            "ExternalId",
            new CsTypeDeclaration(typeof(GuidModelId)),
            new MetadataColumnDraft("external_id")
            {
                PrimaryKey = guidIsPrimaryKey,
                Nullable = !guidIsPrimaryKey
            })
        {
            CsNullable = !guidIsPrimaryKey,
            ScalarConverter = scalarConverter
        };
        var idProperty = new MetadataValuePropertyDraft(
            "Id",
            new CsTypeDeclaration(typeof(int)),
            new MetadataColumnDraft("id") { PrimaryKey = true });
        var nameProperty = new MetadataValuePropertyDraft(
            "Name",
            new CsTypeDeclaration(typeof(string)),
            new MetadataColumnDraft("name"));
        var draft = new MetadataDatabaseDraft(
            "ProviderRowDecoderGuidDb",
            new CsTypeDeclaration(typeof(ProviderRowDecoderTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(DecoderRowModel)))
                    {
                        ValueProperties = guidIsPrimaryKey
                            ? [guidProperty, nameProperty]
                            : [idProperty, guidProperty]
                    },
                    new MetadataTableDraft("guid_materialization_rows"))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels[0]
            .Table;
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

    private sealed record ModelId(int Value);
    private readonly record struct GuidModelId(Guid Value);
    private sealed class DecoderRowModel;

    private sealed class RecordingIdConverter : DataLinqScalarConverter<ModelId, int>
    {
        public int FromProviderCalls { get; private set; }

        public override int ToProvider(ModelId modelValue, in ScalarConversionContext context) =>
            modelValue.Value;

        public override ModelId FromProvider(int providerValue, in ScalarConversionContext context)
        {
            FromProviderCalls++;
            return new ModelId(providerValue);
        }
    }

    private sealed class RecordingGuidIdConverter : DataLinqScalarConverter<GuidModelId, Guid>
    {
        public int FromProviderCalls { get; private set; }

        public override Guid ToProvider(GuidModelId modelValue, in ScalarConversionContext context) =>
            modelValue.Value;

        public override GuidModelId FromProvider(Guid providerValue, in ScalarConversionContext context)
        {
            FromProviderCalls++;
            return new GuidModelId(providerValue);
        }
    }

    private sealed class RecordingReader(object?[] values) : IDataLinqDataReader
    {
        public Exception? Int32Failure { get; init; }
        public int Int32Reads { get; private set; }
        public int GuidReads { get; private set; }
        public int GenericColumnReads { get; private set; }

        public object GetValue(int ordinal) => values[ordinal]!;
        public int GetOrdinal(string name) => throw new NotSupportedException();
        public string GetString(int ordinal) => (string)values[ordinal]!;
        public bool GetBoolean(int ordinal) => (bool)values[ordinal]!;

        public int GetInt32(int ordinal)
        {
            Int32Reads++;
            if (Int32Failure is not null)
                throw Int32Failure;

            return Convert.ToInt32(values[ordinal]);
        }

        public DateOnly GetDateOnly(int ordinal) => (DateOnly)values[ordinal]!;
        public Guid GetGuid(int ordinal)
        {
            GuidReads++;
            return (Guid)values[ordinal]!;
        }
        public byte[]? GetBytes(int ordinal) => (byte[]?)values[ordinal];
        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();

        public T? GetValue<T>(ColumnDefinition column)
        {
            GenericColumnReads++;
            return (T?)values[column.Index];
        }

        public T? GetValue<T>(ColumnDefinition column, int ordinal)
        {
            GenericColumnReads++;
            return (T?)values[ordinal];
        }

        public bool ReadNextRow() => throw new NotSupportedException();
        public bool IsDbNull(int ordinal) => values[ordinal] is null or DBNull;
        public void Dispose() { }
    }
}
