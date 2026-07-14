using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public sealed class SqlLocalProjectionExecutorTests
{
    [Test]
    public async Task ReadPrimaryKey_ConvertedInt64_UsesCanonicalDecoderAtSelectedOrdinal()
    {
        const int selectedOrdinal = 3;
        const long expectedKey = 5_000_000_101L;
        var table = CreateConvertedTable(
            typeof(Int64JoinedKey),
            typeof(long),
            typeof(Int64JoinedKeyConverter),
            new Int64JoinedKeyConverter(),
            typeof(Int64JoinedKeyRow),
            "int64_joined_key_rows");
        var source = new QueryPlanSourceSlot(
            "s0",
            "t0",
            table,
            typeof(Int64JoinedKeyRow),
            QueryPlanSourceKind.ExplicitJoin,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);
        var reader = new RecordingReader(selectedOrdinal, 5_000_000_101m);

        var key = SqlLocalProjectionExecutor.ReadPrimaryKey(
            reader,
            source,
            [selectedOrdinal]);

        await Assert.That(key).IsTypeOf<DataLinqKey>();
        var canonicalKey = (DataLinqKey)key;
        await Assert.That(canonicalKey.ValueCount).IsEqualTo(1);
        await Assert.That(canonicalKey.GetValue(0)).IsTypeOf<long>();
        await Assert.That(canonicalKey.GetValue(0)).IsEqualTo(expectedKey);
        await Assert.That(reader.RawOrdinals).IsEquivalentTo(new[] { selectedOrdinal });
        await Assert.That(reader.NullCheckOrdinals).IsEquivalentTo(new[] { selectedOrdinal });
        await Assert.That(reader.GenericColumnReads).IsEqualTo(0);
    }

    [Test]
    public async Task ReadPrimaryKey_ConvertedInt16_RemainsOnLegacyReaderPath()
    {
        const int selectedOrdinal = 2;
        var table = CreateConvertedTable(
            typeof(Int16JoinedKey),
            typeof(short),
            typeof(Int16JoinedKeyConverter),
            new Int16JoinedKeyConverter(),
            typeof(Int16JoinedKeyRow),
            "int16_joined_key_rows");
        var source = new QueryPlanSourceSlot(
            "s0",
            "t0",
            table,
            typeof(Int16JoinedKeyRow),
            QueryPlanSourceKind.ExplicitJoin,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);
        var reader = new RecordingReader(
            selectedOrdinal,
            rawValue: 42m,
            genericValue: (short)42);

        var key = SqlLocalProjectionExecutor.ReadPrimaryKey(
            reader,
            source,
            [selectedOrdinal]);

        await Assert.That(key).IsTypeOf<short>();
        await Assert.That(key).IsEqualTo((short)42);
        await Assert.That(reader.GenericColumnReads).IsEqualTo(1);
        await Assert.That(reader.GenericColumnOrdinals).IsEquivalentTo(new[] { selectedOrdinal });
        await Assert.That(reader.RawOrdinals).IsEmpty();
        await Assert.That(reader.NullCheckOrdinals).IsEmpty();
    }

    private static TableDefinition CreateConvertedTable(
        Type modelType,
        Type providerType,
        Type converterType,
        IDataLinqScalarConverter converter,
        Type rowType,
        string tableName)
    {
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(modelType),
            new CsTypeDeclaration(providerType),
            new CsTypeDeclaration(converterType),
            () => converter);
        var draft = new MetadataDatabaseDraft(
            "Int64JoinedKeyDb",
            new CsTypeDeclaration(typeof(SqlLocalProjectionExecutorTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(rowType))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(modelType),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "INTEGER")]
                                })
                            {
                                ScalarConverter = scalarConverter
                            }
                        ]
                    },
                    new MetadataTableDraft(tableName))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels.Single()
            .Table;
    }

    private readonly record struct Int64JoinedKey(long Value);
    private readonly record struct Int16JoinedKey(short Value);
    private sealed class Int64JoinedKeyRow;
    private sealed class Int16JoinedKeyRow;

    private sealed class Int64JoinedKeyConverter
        : DataLinqScalarConverter<Int64JoinedKey, long>
    {
        public override long ToProvider(
            Int64JoinedKey modelValue,
            in ScalarConversionContext context) =>
            modelValue.Value;

        public override Int64JoinedKey FromProvider(
            long providerValue,
            in ScalarConversionContext context) =>
            new(providerValue);
    }

    private sealed class Int16JoinedKeyConverter
        : DataLinqScalarConverter<Int16JoinedKey, short>
    {
        public override short ToProvider(
            Int16JoinedKey modelValue,
            in ScalarConversionContext context) =>
            modelValue.Value;

        public override Int16JoinedKey FromProvider(
            short providerValue,
            in ScalarConversionContext context) =>
            new(providerValue);
    }

    private sealed class RecordingReader(
        int selectedOrdinal,
        object rawValue,
        object? genericValue = null) : IDataLinqDataReader
    {
        public List<int> RawOrdinals { get; } = [];
        public List<int> NullCheckOrdinals { get; } = [];
        public List<int> GenericColumnOrdinals { get; } = [];
        public int GenericColumnReads { get; private set; }

        public object GetValue(int ordinal)
        {
            if (ordinal != selectedOrdinal)
                throw new InvalidOperationException($"Unexpected raw ordinal {ordinal}.");

            RawOrdinals.Add(ordinal);
            return rawValue;
        }

        public bool IsDbNull(int ordinal)
        {
            NullCheckOrdinals.Add(ordinal);
            return false;
        }

        public T? GetValue<T>(ColumnDefinition column, int ordinal)
        {
            GenericColumnReads++;
            GenericColumnOrdinals.Add(ordinal);
            if (genericValue is T value)
                return value;

            throw new InvalidOperationException("The legacy generic column reader path was used.");
        }

        public T? GetValue<T>(ColumnDefinition column) =>
            throw new NotSupportedException();

        public int GetOrdinal(string name) => throw new NotSupportedException();
        public string GetString(int ordinal) => throw new NotSupportedException();
        public bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public int GetInt32(int ordinal) => throw new NotSupportedException();
        public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();
        public Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public byte[]? GetBytes(int ordinal) => throw new NotSupportedException();
        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
        public bool ReadNextRow() => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }
}
