using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Metadata;
using DataLinq.Query;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public sealed class QueryPlanSqlColumnValueNormalizerTests
{
    [Test]
    public async Task ComparisonOperands_KeepCanonicalIdentityAndBindPhysicalValueOnce_InBothOrders()
    {
        var converter = new RecordingConverter(
            typeof(TypedQueryId),
            typeof(Guid),
            static value => ((TypedQueryId)value!).Value);
        var column = CreateColumn(typeof(TypedQueryId), converter);
        var firstId = new TypedQueryId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        var secondId = new TypedQueryId(Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"));

        var (_, right) = QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(
            QueryPlanComparisonOperator.Equal,
            Operand.Column(column, "q"),
            Operand.Value(firstId));
        var (left, _) = QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(
            QueryPlanComparisonOperator.Equal,
            Operand.Value(secondId),
            Operand.Column(column, "q"));

        var rightValue = (CanonicalColumnValueOperand)right;
        var leftValue = (CanonicalColumnValueOperand)left;
        await Assert.That(rightValue.Values.Single()).IsEqualTo(firstId.Value);
        await Assert.That(leftValue.Values.Single()).IsEqualTo(secondId.Value);
        await Assert.That(converter.ToProviderCalls.Count).IsEqualTo(2);
        await Assert.That(converter.ToProviderCalls[0]).IsEqualTo(firstId);
        await Assert.That(converter.ToProviderCalls[1]).IsEqualTo(secondId);

        var writer = new RecordingPhysicalWriter(static value => new PhysicalValue(value));
        var firstParameters = rightValue.GetParameterValues(() => writer);
        var repeatedParameters = rightValue.GetParameterValues(() => writer);

        await Assert.That(ReferenceEquals(firstParameters, repeatedParameters)).IsFalse();
        await Assert.That(firstParameters.Single()).IsEqualTo(new PhysicalValue(firstId.Value));
        await Assert.That(writer.Calls.Count).IsEqualTo(1);
        await Assert.That(writer.Calls[0].CanonicalValue).IsEqualTo(firstId.Value);
    }

    [Test]
    public async Task LocalSequence_NormalizesEveryTypedValueInOrder_AndMemoizesPhysicalEncoding()
    {
        var converter = new RecordingConverter(
            typeof(TypedQueryId),
            typeof(Guid),
            static value => ((TypedQueryId)value!).Value);
        var column = CreateColumn(typeof(TypedQueryId), converter);
        var ids = new[]
        {
            new TypedQueryId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")),
            new TypedQueryId(Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100")),
            new TypedQueryId(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"))
        };

        var operand = (CanonicalColumnValueOperand)
            QueryPlanSqlColumnValueNormalizer.NormalizeLocalSequenceValues(column, ids.Cast<object?>().ToArray());

        await Assert.That(operand.Values.Length).IsEqualTo(ids.Length);
        await Assert.That(operand.Values.Cast<Guid>().SequenceEqual(ids.Select(static id => id.Value))).IsTrue();
        await Assert.That(converter.ToProviderCalls.SequenceEqual(ids.Cast<object?>())).IsTrue();

        var writer = new RecordingPhysicalWriter(static value => new PhysicalValue(value));
        var firstParameters = operand.GetParameterValues(() => writer);
        var repeatedParameters = operand.GetParameterValues(() => writer);

        await Assert.That(ReferenceEquals(firstParameters, repeatedParameters)).IsFalse();
        await Assert.That(firstParameters.Cast<PhysicalValue>().Select(static value => (Guid)value.Value!).SequenceEqual(ids.Select(static id => id.Value))).IsTrue();
        await Assert.That(writer.Calls.Count).IsEqualTo(ids.Length);
    }

    [Test]
    public async Task IdentityGuid_RemainsCanonicalUntilColumnWriterBinding()
    {
        var column = CreateColumn(typeof(Guid));
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var (_, normalized) = QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(
            QueryPlanComparisonOperator.Equal,
            Operand.Column(column),
            Operand.Value(id));
        var operand = (CanonicalColumnValueOperand)normalized;
        var writer = new RecordingPhysicalWriter(static value => ((Guid)value!).ToByteArray());

        await Assert.That(operand.Values.Single()).IsEqualTo(id);
        await Assert.That(writer.Calls).IsEmpty();

        var parameter = (byte[])operand.GetParameterValues(() => writer).Single()!;

        await Assert.That(parameter.SequenceEqual(id.ToByteArray())).IsTrue();
        await Assert.That(writer.Calls.Count).IsEqualTo(1);
        await Assert.That(writer.Calls[0].CanonicalValue).IsEqualTo(id);
    }

    [Test]
    public async Task SameClrTypeConverter_IsNotMistakenForIdentityMapping()
    {
        var converter = new RecordingConverter(
            typeof(int),
            typeof(int),
            static value => (int)value! + 1);
        var column = CreateColumn(typeof(int), converter);
        var (_, normalized) = QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(
            QueryPlanComparisonOperator.Equal,
            Operand.Column(column),
            Operand.Value(41));
        var operand = (CanonicalColumnValueOperand)normalized;

        await Assert.That(operand.Values.Single()).IsEqualTo(42);
        await Assert.That(converter.ToProviderCalls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task IdentityChar_PreservesExpressionPromotionCompatibility()
    {
        var column = CreateColumn(typeof(char));
        var (_, normalized) = QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(
            QueryPlanComparisonOperator.Equal,
            Operand.Column(column),
            Operand.Value((int)'Q'));
        var operand = (CanonicalColumnValueOperand)normalized;

        await Assert.That(operand.Values.Single()).IsEqualTo('Q');
    }

    [Test]
    public async Task ConverterBackedChar_NormalizesExpressionPromotionBeforeScalarConversion()
    {
        var converter = new RecordingConverter(
            typeof(char),
            typeof(string),
            static value => value!.ToString());
        var column = CreateColumn(typeof(char), converter);
        var (_, normalized) = QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(
            QueryPlanComparisonOperator.Equal,
            Operand.Column(column),
            Operand.Value((int)'Q'));
        var operand = (CanonicalColumnValueOperand)normalized;

        await Assert.That(operand.Values.Single()).IsEqualTo("Q");
        await Assert.That(converter.ToProviderCalls.Single()).IsEqualTo('Q');
    }

    [Test]
    public async Task ConverterBackedOrdering_IsRejectedWithoutAnOrderingContract()
    {
        var converter = new RecordingConverter(
            typeof(int),
            typeof(int),
            static value => -(int)value!);
        var column = CreateColumn(typeof(int), converter);

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(
                QueryPlanComparisonOperator.GreaterThan,
                Operand.Column(column),
                Operand.Value(41)));

        await Assert.That(exception.Message).Contains("do not declare whether they preserve ordering");
        await Assert.That(exception.Message).Contains(column.Table.DbName);
        await Assert.That(exception.Message).Contains(column.DbName);
        await Assert.That(converter.ToProviderCalls).IsEmpty();
    }

    [Test]
    public async Task BinaryParameterBinding_CannotMutateCanonicalIdentityOrMemoizedPhysicalValue()
    {
        var converter = new RecordingConverter(
            typeof(BinaryQueryId),
            typeof(byte[]),
            static value => ((BinaryQueryId)value!).Value);
        var column = CreateColumn(typeof(BinaryQueryId), converter);
        var sourceBytes = new byte[] { 1, 2, 3 };
        var (_, normalized) = QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(
            QueryPlanComparisonOperator.Equal,
            Operand.Column(column),
            Operand.Value(new BinaryQueryId(sourceBytes)));
        var operand = (CanonicalColumnValueOperand)normalized;
        var writer = new RecordingPhysicalWriter(static value =>
        {
            var bytes = (byte[])value!;
            bytes[0] = 9;
            return bytes;
        });

        sourceBytes[2] = 7;
        var firstParameters = operand.GetParameterValues(() => writer);
        ((byte[])firstParameters.Single()!)[1] = 8;
        var repeatedParameters = operand.GetParameterValues(() => writer);

        await Assert.That(((byte[])operand.Values.Single()!).SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
        await Assert.That(((byte[])repeatedParameters.Single()!).SequenceEqual(new byte[] { 9, 2, 3 })).IsTrue();
        await Assert.That(writer.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OrdinaryValueOperand_RemainsAnAlreadyPhysicalManualOperand()
    {
        var physicalValue = new PhysicalValue("already encoded");
        var operand = Operand.Value(physicalValue);
        var writerFactoryCalls = 0;

        var parameterValues = operand.GetParameterValues(() =>
        {
            writerFactoryCalls++;
            throw new InvalidOperationException("Ordinary manual operands must not request a provider writer.");
        });

        await Assert.That(ReferenceEquals(parameterValues, operand.Values)).IsTrue();
        await Assert.That(parameterValues.Single()).IsSameReferenceAs(physicalValue);
        await Assert.That(writerFactoryCalls).IsEqualTo(0);
    }

    private static ColumnDefinition CreateColumn(
        Type modelType,
        RecordingConverter? converter = null)
    {
        var property = new MetadataValuePropertyDraft(
            "Value",
            new CsTypeDeclaration(modelType),
            new MetadataColumnDraft("value") { PrimaryKey = true })
        {
            ScalarConverter = converter is null
                ? null
                : new MetadataScalarConverterDraft(
                new CsTypeDeclaration(converter.ModelType),
                new CsTypeDeclaration(converter.ProviderType),
                new CsTypeDeclaration(converter.GetType()),
                () => converter)
            {
                Origin = ScalarConverterOrigin.Property
            }
        };

        var draft = new MetadataDatabaseDraft(
            "QueryPlanScalarDb",
            new CsTypeDeclaration(typeof(QueryPlanSqlColumnValueNormalizerTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(QueryValueRow)))
                    {
                        ValueProperties = [property]
                    },
                    new MetadataTableDraft("query_plan_scalar_rows"))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels
            .Single()
            .Table
            .Columns
            .Single();
    }

    private readonly record struct TypedQueryId(Guid Value);
    private sealed record BinaryQueryId(byte[] Value);
    private sealed class QueryValueRow;
    private sealed record PhysicalValue(object? Value);

    private sealed class RecordingConverter(
        Type modelType,
        Type providerType,
        Func<object?, object?> toProvider) : IDataLinqScalarConverter
    {
        public Type ModelType { get; } = modelType;
        public Type ProviderType { get; } = providerType;
        public List<object?> ToProviderCalls { get; } = [];

        public object? ToProviderObject(object? modelValue, in ScalarConversionContext context)
        {
            ToProviderCalls.Add(modelValue);
            return toProvider(modelValue);
        }

        public object? FromProviderObject(object? providerValue, in ScalarConversionContext context) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingPhysicalWriter(Func<object?, object?> encode) : IDataLinqDataWriter
    {
        public List<(ColumnDefinition Column, object? CanonicalValue)> Calls { get; } = [];

        public object? ConvertValue(ColumnDefinition column, object? value)
        {
            Calls.Add((column, value));
            return encode(value);
        }
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
}
