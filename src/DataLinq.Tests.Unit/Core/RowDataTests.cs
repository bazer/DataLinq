using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class RowDataTests
{
    [Test]
    public async Task RowData_GetValueByColumnIndex_ReadsRequestedSubsetSlots()
    {
        var table = CreateRowDataTestTable();
        var idColumn = table.Columns.Single(column => column.DbName == "id");
        var nameColumn = table.Columns.Single(column => column.DbName == "name");
        using var reader = new FakeDataLinqDataReader(["Ada"]);

        var rowData = new RowData(reader, table, [nameColumn], hasIndexedColumns: true);

        await Assert.That(rowData.GetValue(nameColumn.Index)).IsEqualTo("Ada");
        await Assert.That(rowData[nameColumn.Index]).IsEqualTo("Ada");
        await Assert.That(rowData.GetValue(idColumn.Index)).IsNull();
    }

    [Test]
    public async Task MutableRowData_InterfaceGetValues_UsesColumnHandles()
    {
        var table = CreateRowDataTestTable();
        var idColumn = table.Columns.Single(column => column.DbName == "id");
        var nameColumn = table.Columns.Single(column => column.DbName == "name");
        using var reader = new FakeDataLinqDataReader([7, "Ada"]);
        var immutableRowData = new RowData(reader, table, [idColumn, nameColumn], hasIndexedColumns: true);
        var mutableRowData = new MutableRowData(immutableRowData);

        mutableRowData.SetValue(nameColumn, "Grace");

        var values = ((IRowData)mutableRowData).GetValues([idColumn, nameColumn]).ToArray();

        await Assert.That(values).IsEquivalentTo(new object?[] { 7, "Grace" });
        await Assert.That(((IRowData)mutableRowData).GetValue(nameColumn.Index)).IsEqualTo("Grace");
    }

    [Test]
    public async Task RowData_RuntimeEnumWithoutPrecomputedSize_UsesEnumSize()
    {
        var table = CreateRowDataEnumTestTable();
        var statusColumn = table.Columns.Single(column => column.DbName == "status");
        using var reader = new FakeDataLinqDataReader([RowDataNumericStatus.Active]);

        var rowData = new RowData(reader, table, [statusColumn], hasIndexedColumns: true);

        await Assert.That(rowData.GetValue(statusColumn)).IsEqualTo(RowDataNumericStatus.Active);
        await Assert.That(rowData.Size).IsEqualTo(sizeof(int));
    }

    [Test]
    public async Task CanonicalProviderValueRow_CopiesAndIndexesStrictProviderValues()
    {
        var table = CreateCanonicalProviderRowTestTable();
        var idColumn = table.GetColumnByDbName("id");
        var scoreColumn = table.GetColumnByDbName("score");
        var payloadColumn = table.GetColumnByDbName("payload");
        var payload = new byte[] { 1, 2, 3 };
        object?[] values = [42, "Ada", (int?)7, payload];

        var row = CanonicalProviderValueRow.Create(table, values);

        values[0] = 99;
        payload[0] = 9;
        var returnedPayload = (byte[])row[payloadColumn]!;
        returnedPayload[0] = 8;

        await Assert.That(row.Table).IsSameReferenceAs(table);
        await Assert.That(row.Count).IsEqualTo(table.ColumnCount);
        await Assert.That(row[idColumn]).IsEqualTo(42);
        await Assert.That(row[scoreColumn]).IsEqualTo(7);
        await Assert.That(((byte[])row[payloadColumn]!)[0]).IsEqualTo((byte)1);
        await Assert.That(row.EstimatedPayloadSize).IsEqualTo(21);
    }

    [Test]
    public async Task CanonicalProviderValueRow_RejectsMissingNullWireAndModelValues()
    {
        var table = CreateCanonicalProviderRowTestTable();
        var idColumn = table.GetColumnByDbName("id");

        var missing = Capture<ArgumentException>(() =>
            CanonicalProviderValueRow.Create(table, new object?[] { 42, "Ada", 7 }));
        var requiredNull = Capture<ArgumentException>(() =>
            CanonicalProviderValueRow.Create(table, new object?[] { null, "Ada", 7, Array.Empty<byte>() }));
        var dbNull = Capture<ArgumentException>(() =>
            CanonicalProviderValueRow.Create(table, new object?[] { 42, DBNull.Value, 7, Array.Empty<byte>() }));
        var modelValue = Capture<ArgumentException>(() =>
            CanonicalProviderValueRow.Create(table, new object?[] { new CanonicalProviderRowId(42), "Ada", 7, Array.Empty<byte>() }));
        var widenedValue = Capture<ArgumentException>(() =>
            CanonicalProviderValueRow.Create(table, new object?[] { 42L, "Ada", 7, Array.Empty<byte>() }));
        var mutableMetadata = Capture<InvalidOperationException>(() =>
            CanonicalProviderValueRow.Create(new TableDefinition("mutable_rows"), Array.Empty<object?>()));
        var nullableValue = CanonicalProviderValueRow.Create(
            table,
            new object?[] { 42, "Ada", null, Array.Empty<byte>() });

        await Assert.That(idColumn.ValueProperty.CsNullable).IsTrue();
        await Assert.That(idColumn.Nullable).IsFalse();
        await Assert.That(missing.Message).Contains("requires exactly 4");
        await Assert.That(missing.Message).Contains("Missing cells must not be represented as null");
        await Assert.That(requiredNull.Message).Contains("Non-nullable column 'canonical_provider_rows.id'");
        await Assert.That(dbNull.Message).Contains("DBNull.Value");
        await Assert.That(modelValue.Message).Contains(typeof(CanonicalProviderRowId).FullName!);
        await Assert.That(modelValue.Message).Contains(typeof(int).FullName!);
        await Assert.That(widenedValue.Message).Contains(typeof(long).FullName!);
        await Assert.That(mutableMetadata.Message).Contains("require frozen metadata");
        await Assert.That(nullableValue[table.GetColumnByDbName("score")]).IsNull();
    }

    [Test]
    public async Task CanonicalProviderValueRow_RejectsColumnsFromAnotherTable()
    {
        var table = CreateCanonicalProviderRowTestTable();
        var otherTable = CreateCanonicalProviderRowTestTable();
        var row = CanonicalProviderValueRow.Create(
            table,
            new object?[] { 42, "Ada", null, Array.Empty<byte>() });

        var exception = Capture<ArgumentException>(() => _ = row[otherTable.GetColumnByDbName("id")]);

        await Assert.That(exception.Message).Contains("does not belong to canonical provider row table");
    }

    [Test]
    public async Task RowData_CreateTrusted_PreservesModelValuesAndUsesCanonicalSizeFallback()
    {
        var table = CreateCanonicalProviderRowTestTable();
        var idColumn = table.GetColumnByDbName("id");
        var payloadColumn = table.GetColumnByDbName("payload");
        var providerRow = CanonicalProviderValueRow.Create(
            table,
            new object?[] { 42, "Ada", 7, new byte[] { 1, 2, 3 } });
        var modelPayload = new byte[] { 1, 2, 3 };
        object?[] modelValues = [new CanonicalProviderRowId(42), "Ada", 7, modelPayload];

        var rowData = RowData.CreateTrusted(providerRow, modelValues);

        modelValues[0] = new CanonicalProviderRowId(99);
        modelPayload[0] = 9;

        await Assert.That(providerRow[idColumn]).IsEqualTo(42);
        await Assert.That(rowData[idColumn]).IsEqualTo(new CanonicalProviderRowId(42));
        await Assert.That(((byte[])rowData[payloadColumn]!)[0]).IsEqualTo((byte)1);
        await Assert.That(rowData[table.GetColumnByDbName("name")]).IsEqualTo("Ada");
        await Assert.That(rowData[table.GetColumnByDbName("score")]).IsEqualTo(7);
        await Assert.That(rowData.Size).IsEqualTo(21);
    }

    [Test]
    public async Task RowData_CreateTrusted_RejectsProviderValuesOnTheModelSide()
    {
        var table = CreateCanonicalProviderRowTestTable();
        var providerRow = CanonicalProviderValueRow.Create(
            table,
            new object?[] { 42, "Ada", null, Array.Empty<byte>() });

        var exception = Capture<ArgumentException>(() => RowData.CreateTrusted(
            providerRow,
            new object?[] { 42, "Ada", null, Array.Empty<byte>() }));

        await Assert.That(exception.Message).Contains("requires model CLR type");
        await Assert.That(exception.Message).Contains(typeof(CanonicalProviderRowId).FullName!);
    }

    [Test]
    public async Task RowData_CreateTrusted_UsesModelNullabilityInsteadOfProviderNullability()
    {
        var table = CreateCanonicalProviderRowTestTable();
        var providerRow = CanonicalProviderValueRow.Create(
            table,
            new object?[] { 42, "Ada", null, Array.Empty<byte>() });

        var modelNullableRow = RowData.CreateTrusted(
            providerRow,
            new object?[] { null, "Ada", null, Array.Empty<byte>() });
        var requiredModelNull = Capture<ArgumentException>(() => RowData.CreateTrusted(
            providerRow,
            new object?[] { new CanonicalProviderRowId(42), null, null, Array.Empty<byte>() }));

        await Assert.That(modelNullableRow[table.GetColumnByDbName("id")]).IsNull();
        await Assert.That(requiredModelNull.Message).Contains("null model value");
        await Assert.That(requiredModelNull.Message).Contains("canonical_provider_rows.name");
    }

    [Test]
    public async Task RowData_CreateTrusted_AcceptsAssignableModelScalarImplementations()
    {
        var table = CreateAssignableModelProviderRowTestTable();
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });
        CanonicalProviderReferenceId modelValue = new DerivedCanonicalProviderReferenceId(42);

        var rowData = RowData.CreateTrusted(providerRow, new object?[] { modelValue });

        await Assert.That(rowData[0]).IsSameReferenceAs(modelValue);
        await Assert.That(rowData.Size).IsEqualTo(sizeof(int));
    }

    [Test]
    public async Task RowData_CreateTrusted_UsesCanonicalFallbackForTemporalIdentityValues()
    {
        var table = CreateTemporalCanonicalProviderRowTestTable();
        var duration = TimeSpan.FromMinutes(5);
        var timestamp = new DateTimeOffset(2026, 7, 10, 22, 30, 0, TimeSpan.Zero);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { duration, timestamp });

        var rowData = RowData.CreateTrusted(providerRow, new object?[] { duration, timestamp });

        await Assert.That(rowData[0]).IsEqualTo(duration);
        await Assert.That(rowData[1]).IsEqualTo(timestamp);
        await Assert.That(rowData.Size).IsEqualTo(IntPtr.Size * 4);
    }

    private static TableDefinition CreateRowDataTestTable()
    {
        var draft = new MetadataDatabaseDraft(
            "RowDataTestDb",
            new CsTypeDeclaration("RowDataTestDb", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("RowDataTestRow", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, "int")]
                                })
                            {
                                CsSize = sizeof(int)
                            },
                            new MetadataValuePropertyDraft(
                                "Name",
                                new CsTypeDeclaration(typeof(string)),
                                new MetadataColumnDraft("name")
                                {
                                    DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, "varchar", 32)]
                                })
                        ]
                    },
                    new MetadataTableDraft("row_data_test_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private static TableDefinition CreateRowDataEnumTestTable()
    {
        var draft = new MetadataDatabaseDraft(
            "RowDataEnumTestDb",
            new CsTypeDeclaration("RowDataEnumTestDb", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("RowDataEnumTestRow", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Status",
                                new CsTypeDeclaration(typeof(RowDataNumericStatus)),
                                new MetadataColumnDraft("status")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, "smallint", 5, signed: false)]
                                })
                        ]
                    },
                    new MetadataTableDraft("row_data_enum_test_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private static TableDefinition CreateCanonicalProviderRowTestTable()
    {
        var converter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(CanonicalProviderRowId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(CanonicalProviderRowIdConverter)),
            static () => new CanonicalProviderRowIdConverter())
        {
            Origin = ScalarConverterOrigin.Property
        };
        var draft = new MetadataDatabaseDraft(
            "CanonicalProviderRowDb",
            new CsTypeDeclaration("CanonicalProviderRowDb", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("CanonicalProviderRow", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(CanonicalProviderRowId)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    AutoIncrement = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "integer")]
                                })
                            {
                                CsNullable = true,
                                ScalarConverter = converter
                            },
                            new MetadataValuePropertyDraft(
                                "Name",
                                new CsTypeDeclaration(typeof(string)),
                                new MetadataColumnDraft("name")
                                {
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "text")]
                                }),
                            new MetadataValuePropertyDraft(
                                "Score",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("score")
                                {
                                    Nullable = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "integer")]
                                })
                            {
                                CsNullable = true,
                                CsSize = sizeof(int)
                            },
                            new MetadataValuePropertyDraft(
                                "Payload",
                                new CsTypeDeclaration(typeof(byte[])),
                                new MetadataColumnDraft("payload")
                                {
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "blob")]
                                })
                        ]
                    },
                    new MetadataTableDraft("canonical_provider_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private static TableDefinition CreateAssignableModelProviderRowTestTable()
    {
        var converter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(CanonicalProviderReferenceId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(CanonicalProviderReferenceIdConverter)),
            static () => new CanonicalProviderReferenceIdConverter())
        {
            Origin = ScalarConverterOrigin.Property
        };
        var draft = new MetadataDatabaseDraft(
            "AssignableModelProviderRowDb",
            new CsTypeDeclaration("AssignableModelProviderRowDb", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("AssignableModelProviderRow", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(CanonicalProviderReferenceId)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "integer")]
                                })
                            {
                                ScalarConverter = converter
                            }
                        ]
                    },
                    new MetadataTableDraft("assignable_model_provider_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private static TableDefinition CreateTemporalCanonicalProviderRowTestTable()
    {
        var draft = new MetadataDatabaseDraft(
            "TemporalCanonicalProviderRowDb",
            new CsTypeDeclaration("TemporalCanonicalProviderRowDb", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("TemporalCanonicalProviderRow", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Duration",
                                new CsTypeDeclaration(typeof(TimeSpan)),
                                new MetadataColumnDraft("duration")
                                {
                                    PrimaryKey = true
                                }),
                            new MetadataValuePropertyDraft(
                                "Timestamp",
                                new CsTypeDeclaration(typeof(DateTimeOffset)),
                                new MetadataColumnDraft("timestamp"))
                        ]
                    },
                    new MetadataTableDraft("temporal_canonical_provider_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
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

    private readonly record struct CanonicalProviderRowId(int Value);

    private sealed class CanonicalProviderRowIdConverter : DataLinqScalarConverter<CanonicalProviderRowId, int>
    {
        public override int ToProvider(CanonicalProviderRowId modelValue, in ScalarConversionContext context) =>
            modelValue.Value;

        public override CanonicalProviderRowId FromProvider(int providerValue, in ScalarConversionContext context) =>
            new(providerValue);
    }

    private abstract class CanonicalProviderReferenceId(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class DerivedCanonicalProviderReferenceId(int value) : CanonicalProviderReferenceId(value);

    private sealed class CanonicalProviderReferenceIdConverter : DataLinqScalarConverter<CanonicalProviderReferenceId, int>
    {
        public override int ToProvider(CanonicalProviderReferenceId modelValue, in ScalarConversionContext context) =>
            modelValue.Value;

        public override CanonicalProviderReferenceId FromProvider(int providerValue, in ScalarConversionContext context) =>
            new DerivedCanonicalProviderReferenceId(providerValue);
    }

    private sealed class FakeDataLinqDataReader(IReadOnlyList<object?> values) : IDataLinqDataReader
    {
        public object GetValue(int ordinal) => values[ordinal]!;
        public T? GetValue<T>(ColumnDefinition column) => (T?)values[column.Index];
        public T? GetValue<T>(ColumnDefinition column, int ordinal) => (T?)values[ordinal];
        public void Dispose()
        {
        }

        public int GetOrdinal(string name) => throw new NotSupportedException();
        public string GetString(int ordinal) => throw new NotSupportedException();
        public bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public int GetInt32(int ordinal) => throw new NotSupportedException();
        public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();
        public Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public byte[]? GetBytes(int ordinal) => throw new NotSupportedException();
        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
        public bool ReadNextRow() => throw new NotSupportedException();
        public bool IsDbNull(int ordinal) => values[ordinal] is null;
    }

    private enum RowDataNumericStatus : short
    {
        Unknown = 0,
        Active = 1
    }
}
