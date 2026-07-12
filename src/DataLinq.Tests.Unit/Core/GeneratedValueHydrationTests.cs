using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class ModelValueConverterTests
{
    [Test]
    public async Task StateChange_AutoIncrementHydrationDecodesCanonicalValueBeforeModelConversion()
    {
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value,
            static (value, _) => new MutationId((int)value!));
        var table = CreateAutoIncrementMutationTable(converter);
        var idColumn = table.PrimaryKeyColumns.Single();
        var writer = new RecordingPhysicalWriter();
        var provider = new RecordingProvider(table.Database, writer, generatedValue: 42L);
        var transaction = new Transaction(provider, TransactionType.WriteOnly);
        var values = new Dictionary<Metadata.ColumnDefinition, object?>
        {
            [idColumn] = null
        };
        var model = new TestMutableInstance(table, values, [], isNew: true);
        var stateChange = new StateChange(model, table, TransactionChangeType.Insert);

        stateChange.ExecutePreflightedQuery(transaction);

        var canonicalKey = stateChange.PrimaryKeys;

        await Assert.That(model[idColumn]).IsEqualTo(new MutationId(42));
        await Assert.That(model[idColumn]).IsTypeOf<MutationId>();
        await Assert.That(canonicalKey.GetValue(0)).IsEqualTo(42);
        await Assert.That(canonicalKey.GetValue(0)).IsTypeOf<int>();
        await Assert.That(converter.ToProviderCalls.Count).IsEqualTo(1);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
        await Assert.That(writer.Calls).IsEmpty();
    }

    [Test]
    public async Task StateChange_AutoIncrementDecodeFailureDoesNotMutateModel()
    {
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value,
            static (value, _) => new MutationId((int)value!));
        var table = CreateAutoIncrementMutationTable(converter);
        var idColumn = table.PrimaryKeyColumns.Single();
        var writer = new RecordingPhysicalWriter();
        var provider = new RecordingProvider(table.Database, writer, generatedValue: long.MaxValue);
        var transaction = new Transaction(provider, TransactionType.WriteOnly);
        var values = new Dictionary<Metadata.ColumnDefinition, object?>
        {
            [idColumn] = null
        };
        var model = new TestMutableInstance(table, values, [], isNew: true);
        var stateChange = new StateChange(model, table, TransactionChangeType.Insert);

        var exception = Capture<GeneratedValueDecodingException>(() =>
            stateChange.ExecutePreflightedQuery(transaction));

        await Assert.That(exception.Column).IsSameReferenceAs(idColumn);
        await Assert.That(exception.SourceName).IsEqualTo("sql.generated");
        await Assert.That(exception.InnerException).IsTypeOf<OverflowException>();
        await Assert.That(model[idColumn]).IsNull();
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    [Test]
    public async Task StateChange_AutoIncrementModelConversionFailureDoesNotMutateModel()
    {
        var expectedInner = new InvalidOperationException("generated ID converter failed");
        var converter = new RecordingScalarConverter(
            typeof(MutationId),
            typeof(int),
            static (value, _) => ((MutationId)value!).Value,
            (_, _) => throw expectedInner);
        var table = CreateAutoIncrementMutationTable(converter);
        var idColumn = table.PrimaryKeyColumns.Single();
        var writer = new RecordingPhysicalWriter();
        var provider = new RecordingProvider(table.Database, writer, generatedValue: 42UL);
        var transaction = new Transaction(provider, TransactionType.WriteOnly);
        var values = new Dictionary<Metadata.ColumnDefinition, object?>
        {
            [idColumn] = null
        };
        var model = new TestMutableInstance(table, values, [], isNew: true);
        var stateChange = new StateChange(model, table, TransactionChangeType.Insert);

        var exception = Capture<ProviderValueMaterializationException>(() =>
            stateChange.ExecutePreflightedQuery(transaction));

        await Assert.That(exception.Column).IsSameReferenceAs(idColumn);
        await Assert.That(exception.SourceName).IsEqualTo("sql.generated");
        await Assert.That(exception.InnerException).IsSameReferenceAs(expectedInner);
        await Assert.That(model[idColumn]).IsNull();
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
    }
}
