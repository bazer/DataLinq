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
