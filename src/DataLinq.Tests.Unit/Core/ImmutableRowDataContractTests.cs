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

public sealed class ImmutableRowDataContractTests
{
    [Test]
    public async Task LegacyConstructor_NullRow_PreservesProjectionOnlyCompatibility()
    {
        var row = new ContractModel(null!);

        await Assert.That(row.GetRowData()).IsNull();
    }

    [Test]
    public async Task NonNullRowWithoutTable_ReportsContractViolationFromBothConstructors()
    {
        var rowData = new ContractRowData(null, _ => throw new InvalidOperationException());
        var table = CreateContractTable(hasPrimaryKey: true);
        var readSource = new ContractReadSource(table.Database);

        var legacyFailure = Capture<ArgumentException>(() => new ContractModel(rowData));
        var neutralFailure = Capture<ArgumentException>(() => new ContractModel(rowData, readSource));

        await AssertTableContractFailure(legacyFailure);
        await AssertTableContractFailure(neutralFailure);
    }

    [Test]
    public async Task NonNullLegacyRowWithUnreadablePrimaryKey_ReportsContractViolation()
    {
        var table = CreateContractTable(hasPrimaryKey: true);
        var readFailure = new NotSupportedException("The loose row does not implement value access.");
        var rowData = new ContractRowData(table, _ => throw readFailure);

        var exception = Capture<ArgumentException>(() => new ContractModel(rowData));

        await Assert.That(exception.ParamName).IsEqualTo("rowData");
        await Assert.That(exception.Message).Contains("IRowData contract violation");
        await Assert.That(exception.Message).Contains(typeof(ContractModel).FullName!);
        await Assert.That(exception.Message).Contains("contract_rows.id");
        await Assert.That(exception.Message).Contains("could not be read");
        await Assert.That(exception.InnerException).IsSameReferenceAs(readFailure);
    }

    [Test]
    public async Task NonNullRowWithIncompleteTableMetadata_ReportsContractViolation()
    {
        var table = new TableDefinition("incomplete_rows");
        var rowData = new ContractRowData(
            table,
            _ => throw new InvalidOperationException("Value access must not be attempted."));

        var exception = Capture<ArgumentException>(() => new ContractModel(rowData));

        await Assert.That(exception.ParamName).IsEqualTo("rowData");
        await Assert.That(exception.Message).Contains("IRowData.Table metadata is incomplete");
        await Assert.That(exception.Message).Contains("TableDefinition.TableModel is unavailable");
        await Assert.That(exception.Message).Contains("primary-key metadata cannot be validated");
        await Assert.That(exception.Message).Contains("incomplete_rows");
    }

    [Test]
    public async Task NonNullRowWithNullPrimaryKey_ReportsMissingRequiredValue()
    {
        var table = CreateContractTable(hasPrimaryKey: true);
        var rowData = new ContractRowData(table, _ => null);

        var exception = Capture<ArgumentException>(() => new ContractModel(rowData));

        await Assert.That(exception.ParamName).IsEqualTo("rowData");
        await Assert.That(exception.Message).Contains("IRowData contract violation");
        await Assert.That(exception.Message).Contains("contract_rows.id");
        await Assert.That(exception.Message).Contains("unavailable (null)");
    }

    [Test]
    public async Task NonNullCompleteRow_CapturesPrimaryKey()
    {
        var table = CreateContractTable(hasPrimaryKey: true);
        var rowData = new ContractRowData(table, _ => 42);

        var row = new ContractModel(rowData);

        await Assert.That(row.PrimaryKeys()).IsEqualTo(DataLinqKey.FromValue(42));
    }

    [Test]
    public async Task RowForModelWithoutPrimaryKey_PreservesNullIdentity()
    {
        var table = CreateContractTable(hasPrimaryKey: false);
        var rowData = new ContractRowData(
            table,
            _ => throw new InvalidOperationException("No primary-key value should be requested."));

        var row = new ContractModel(rowData);

        await Assert.That(row.PrimaryKeys()).IsEqualTo(DataLinqKey.Null);
    }

    private static async Task AssertTableContractFailure(ArgumentException exception)
    {
        await Assert.That(exception.ParamName).IsEqualTo("rowData");
        await Assert.That(exception.Message).Contains("IRowData contract violation");
        await Assert.That(exception.Message).Contains(typeof(ContractModel).FullName!);
        await Assert.That(exception.Message).Contains("IRowData.Table is unavailable");
        await Assert.That(exception.Message).DoesNotContain("NullReferenceException");
    }

    private static TableDefinition CreateContractTable(bool hasPrimaryKey)
    {
        var draft = new MetadataDatabaseDraft(
            "ImmutableContractDb",
            new CsTypeDeclaration(typeof(ContractDatabase)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(ContractModel)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = hasPrimaryKey,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "integer")]
                                })
                            {
                                CsSize = sizeof(int)
                            }
                        ]
                    },
                    new MetadataTableDraft("contract_rows")
                    {
                        Type = hasPrimaryKey ? TableType.Table : TableType.View,
                        Definition = hasPrimaryKey ? null : "select 1 as id"
                    })
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
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

    private sealed class ContractModel :
        Immutable<ContractModel, ContractDatabase>,
        IModel
    {
        internal ContractModel(IRowData rowData)
            : base(rowData, (IDataSourceAccess)null!)
        {
        }

        internal ContractModel(IRowData rowData, IDataLinqReadSource readSource)
            : base(rowData, readSource)
        {
        }
    }

    private sealed class ContractDatabase : IDatabaseModel;

    private sealed class ContractReadSource(DatabaseDefinition metadata) : IDataLinqReadSource
    {
        public DatabaseDefinition Metadata { get; } = metadata;
    }

    private sealed class ContractRowData(
        TableDefinition? table,
        Func<ColumnDefinition, object?> readValue) : IRowData
    {
        public TableDefinition Table => table!;

        public object? this[ColumnDefinition column] => GetValue(column);
        public object? this[int columnIndex] => GetValue(columnIndex);

        public object? GetValue(ColumnDefinition column) => readValue(column);
        public object? GetValue(int columnIndex) => readValue(Table.Columns[columnIndex]);
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(GetValue);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(column, GetValue(column)));
    }
}
