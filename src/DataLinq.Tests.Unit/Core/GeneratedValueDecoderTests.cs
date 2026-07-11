using System;
using System.Globalization;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class GeneratedValueDecoderTests
{
    private static readonly IntegralBoundaryCase[] IntegralBoundaryCases =
    [
        new(typeof(sbyte), (long)sbyte.MinValue, sbyte.MinValue, (ulong)sbyte.MaxValue, sbyte.MaxValue, (long)sbyte.MaxValue + 1),
        new(typeof(byte), 0L, (byte)0, (ulong)byte.MaxValue, byte.MaxValue, -1L),
        new(typeof(short), (long)short.MinValue, short.MinValue, (ulong)short.MaxValue, short.MaxValue, (long)short.MaxValue + 1),
        new(typeof(ushort), 0L, (ushort)0, (ulong)ushort.MaxValue, ushort.MaxValue, -1L),
        new(typeof(int), (long)int.MinValue, int.MinValue, (ulong)int.MaxValue, int.MaxValue, long.MaxValue),
        new(typeof(uint), 0L, 0U, (ulong)uint.MaxValue, uint.MaxValue, -1L),
        new(typeof(long), long.MinValue, long.MinValue, (ulong)long.MaxValue, long.MaxValue, ulong.MaxValue),
        new(typeof(ulong), 0L, 0UL, ulong.MaxValue, ulong.MaxValue, -1L)
    ];

    [Test]
    public async Task DecodeAutoIncrementValue_NormalizesLongAndUlongBoundariesForEveryIntegralTarget()
    {
        foreach (var testCase in IntegralBoundaryCases)
        {
            var column = CreateColumn(testCase.TargetType, autoIncrement: true);

            var minimum = GeneratedValueDecoder.DecodeAutoIncrementValue(
                column,
                testCase.MinimumRaw,
                "sql.generated");
            var maximum = GeneratedValueDecoder.DecodeAutoIncrementValue(
                column,
                testCase.MaximumRaw,
                "sql.generated");
            var exactRaw = Convert.ChangeType(42, testCase.TargetType, CultureInfo.InvariantCulture)!;
            var exact = GeneratedValueDecoder.DecodeAutoIncrementValue(
                column,
                exactRaw,
                "sql.generated");
            var overflow = Capture<GeneratedValueDecodingException>(() =>
                GeneratedValueDecoder.DecodeAutoIncrementValue(
                    column,
                    testCase.OverflowRaw,
                    "sql.generated"));

            await Assert.That(minimum).IsEqualTo(testCase.MinimumExpected);
            await Assert.That(minimum.GetType()).IsEqualTo(testCase.TargetType);
            await Assert.That(maximum).IsEqualTo(testCase.MaximumExpected);
            await Assert.That(maximum.GetType()).IsEqualTo(testCase.TargetType);
            await Assert.That(exact).IsEqualTo(exactRaw);
            await Assert.That(exact.GetType()).IsEqualTo(testCase.TargetType);
            await Assert.That(overflow.InnerException).IsTypeOf<OverflowException>();
            await Assert.That(overflow.Column).IsSameReferenceAs(column);
        }
    }

    [Test]
    public async Task DecodeAutoIncrementValue_RejectsMissingAndNonIntegralPhysicalValuesWithSafeContext()
    {
        var column = CreateColumn(typeof(int), autoIncrement: true);
        object?[] invalidValues = [null, DBNull.Value, 42m, 42d, "42", true];

        foreach (var invalidValue in invalidValues)
        {
            var exception = Capture<GeneratedValueDecodingException>(() =>
                GeneratedValueDecoder.DecodeAutoIncrementValue(
                    column,
                    invalidValue,
                    "sql.generated"));

            await Assert.That(exception.Column).IsSameReferenceAs(column);
            await Assert.That(exception.SourceName).IsEqualTo("sql.generated");
            await Assert.That(exception.Message).Contains("Physical value context:");
            if (invalidValue is string text)
            {
                await Assert.That(exception.Message).Contains($"length {text.Length}");
                await Assert.That(exception.Message).DoesNotContain(text);
            }
        }
    }

    [Test]
    public async Task DecodeAutoIncrementValue_RejectsNonIntegralCanonicalTargetAndNonAutoIncrementColumn()
    {
        var stringColumn = CreateColumn(typeof(string), autoIncrement: true);
        var targetException = Capture<GeneratedValueDecodingException>(() =>
            GeneratedValueDecoder.DecodeAutoIncrementValue(
                stringColumn,
                42L,
                "sql.generated"));
        var ordinaryColumn = CreateColumn(typeof(int), autoIncrement: false);
        var columnException = Capture<ArgumentException>(() =>
            GeneratedValueDecoder.DecodeAutoIncrementValue(
                ordinaryColumn,
                42L,
                "sql.generated"));

        await Assert.That(targetException.InnerException).IsTypeOf<NotSupportedException>();
        await Assert.That(targetException.Message).Contains(typeof(string).FullName!);
        await Assert.That(columnException.ParamName).IsEqualTo("column");
        await Assert.That(columnException.Message).Contains("is not an auto-increment column");
    }

    private static ColumnDefinition CreateColumn(Type type, bool autoIncrement)
    {
        var draft = new MetadataDatabaseDraft(
            "GeneratedValueDecoderDb",
            new CsTypeDeclaration(typeof(GeneratedValueDecoderTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(GeneratedValueRow)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(type),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    AutoIncrement = autoIncrement
                                })
                            {
                                CsNullable = autoIncrement
                            }
                        ]
                    },
                    new MetadataTableDraft("generated_value_rows"))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels[0]
            .Table
            .Columns[0];
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

    private sealed record IntegralBoundaryCase(
        Type TargetType,
        object MinimumRaw,
        object MinimumExpected,
        object MaximumRaw,
        object MaximumExpected,
        object OverflowRaw);

    private sealed class GeneratedValueRow;
}
