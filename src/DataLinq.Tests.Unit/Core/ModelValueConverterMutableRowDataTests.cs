using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class ModelValueConverterTests
{
    [Test]
    public async Task MutableRowData_SetValue_RejectsCanonicalValueAndPreservesModelDomain()
    {
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value,
            static (value, _) => new MutationId((int)value!));
        var table = CreateSingleConvertedTable("mutable_provider_value_rows", converter);
        var column = table.Columns.Single();
        var rowData = new MutableRowData(table);

        var exception = Capture<ArgumentException>(() =>
            rowData.SetValue(column, 42));

        await Assert.That(exception.Message)
            .Contains("mutable_provider_value_rows.value");
        await Assert.That(exception.Message).Contains(typeof(MutationId).FullName!);
        await Assert.That(exception.Message).Contains(typeof(int).FullName!);
        await Assert.That(rowData.HasChanges()).IsFalse();
        await Assert.That(rowData.MutationVersion).IsEqualTo(0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(converter.ToProviderCalls).IsEmpty();
    }

    [Test]
    public async Task MutableRowData_SetValue_ExactModelValueAndNullBypassConverter()
    {
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value,
            static (value, _) => new MutationId((int)value!));
        var table = CreateSingleConvertedTable("mutable_model_rows", converter);
        var column = table.Columns.Single();
        var rowData = new MutableRowData(table);
        var modelValue = new MutationId(7);

        rowData.SetValue(column, modelValue);
        var storedModelValue = rowData[column];
        rowData.SetValue(column, null);

        await Assert.That(storedModelValue).IsSameReferenceAs(modelValue);
        await Assert.That(rowData[column]).IsNull();
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(converter.ToProviderCalls).IsEmpty();
        await Assert.That(rowData.MutationVersion).IsEqualTo(2);
    }

    [Test]
    public async Task MutableRowData_SetValue_WrongModelTypePreservesStateAndReportsBoundary()
    {
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value,
            static (value, _) => new MutationId((int)value!));
        var table = CreateSingleConvertedTable("mutable_invalid_rows", converter);
        var column = table.Columns.Single();
        var rowData = new MutableRowData(table);

        var exception = Capture<ArgumentException>(() =>
            rowData.SetValue(column, "not-a-mutation-id"));

        await Assert.That(exception.Message).Contains("mutable_invalid_rows.value");
        await Assert.That(exception.Message).Contains(typeof(MutationId).FullName!);
        await Assert.That(exception.Message).Contains(typeof(string).FullName!);
        await Assert.That(rowData.HasChanges()).IsFalse();
        await Assert.That(rowData.MutationVersion).IsEqualTo(0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(converter.ToProviderCalls).IsEmpty();
    }

    [Test]
    public async Task MutableRowData_SetValue_IdentityMappingRetainsPrimitiveConversion()
    {
        var table = CreateIdentityTable();
        var column = table.Columns.Single();
        var rowData = new MutableRowData(table);

        rowData.SetValue(column, "42");

        await Assert.That(rowData[column]).IsEqualTo(42);
        await Assert.That(rowData[column]).IsTypeOf<int>();
        await Assert.That(rowData.MutationVersion).IsEqualTo(1);
    }
}
