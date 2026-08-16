using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning.Expressions;

namespace DataLinq.Tests.Unit.Linq;

public class ExpressionLocalValueEvaluatorTests
{
    [Test]
    public async Task LocalValueEvaluation_AllowsArrayIndexAndStringMethods()
    {
        var ids = new[] { 10, 20 };
        var departmentName = "Sales";
        Expression<Func<int>> indexedValue = () => ids[1];
        Expression<Func<string>> stringValue = () => departmentName.ToUpper().Substring(1, 2);

        var actualIndexedValue = ExpressionLocalValueEvaluator.Evaluate(indexedValue.Body);
        var actualStringValue = ExpressionLocalValueEvaluator.Evaluate(stringValue.Body);

        await Assert.That(actualIndexedValue).IsEqualTo(20);
        await Assert.That(actualStringValue).IsEqualTo("AL");
    }

    [Test]
    public async Task LocalValueEvaluation_AllowsParameterIndependentCompatibilityMethods()
    {
        var probe = new LocalMethodProbe();
        Expression<Func<int>> expression = () => probe.GetEmployeeNumber();

        var actual = ExpressionLocalValueEvaluator.Evaluate(expression.Body);

        await Assert.That(actual).IsEqualTo(10001);
        await Assert.That(probe.InvocationCount).IsEqualTo(1);
    }

    [Test]
    public async Task LocalValueEvaluation_PreservesBuiltInNumericAndEnumConversions()
    {
        var widened = ExpressionLocalValueEvaluator.Evaluate(
            Expression.Convert(Expression.Constant(10), typeof(double)));
        var truncated = ExpressionLocalValueEvaluator.Evaluate(
            Expression.Convert(Expression.Constant(10.9d), typeof(int)));
        var wrapped = ExpressionLocalValueEvaluator.Evaluate(
            Expression.Convert(Expression.Constant(257), typeof(byte)));
        var enumValue = ExpressionLocalValueEvaluator.Evaluate(
            Expression.Convert(Expression.Constant(2), typeof(LocalConversionKind)));
        var enumStorage = ExpressionLocalValueEvaluator.Evaluate(
            Expression.Convert(Expression.Constant(LocalConversionKind.Second), typeof(byte)));
        var decimalTruncation = ExpressionLocalValueEvaluator.Evaluate(
            Expression.Convert(Expression.Constant(10.9m), typeof(int)),
            null,
            null,
            ExpressionLocalValueEvaluationOptions.AotStrict);

        await Assert.That(widened).IsTypeOf<double>();
        await Assert.That(widened).IsEqualTo(10d);
        await Assert.That(truncated).IsEqualTo(10);
        await Assert.That(wrapped).IsTypeOf<byte>();
        await Assert.That(wrapped).IsEqualTo((byte)1);
        await Assert.That(enumValue).IsEqualTo(LocalConversionKind.Second);
        await Assert.That(enumStorage).IsEqualTo((byte)2);
        await Assert.That(decimalTruncation).IsEqualTo(10);
    }

    [Test]
    public async Task LocalValueEvaluation_PreservesCheckedConversionOverflow()
    {
        var checkedConversion = Expression.ConvertChecked(
            Expression.Constant(long.MaxValue),
            typeof(int));

        var exception = Capture<OverflowException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(checkedConversion));

        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task LocalValueEvaluation_PreservesLiftedNullableConversions()
    {
        var value = Expression.Parameter(typeof(int?), "value");
        var lifted = Expression.Convert(value, typeof(long?));
        var unwrapped = Expression.Convert(value, typeof(int));

        var convertedValue = ExpressionLocalValueEvaluator.Evaluate(lifted, value, 10);
        var convertedNull = ExpressionLocalValueEvaluator.Evaluate(lifted, value, null);
        var unwrappedValue = ExpressionLocalValueEvaluator.Evaluate(unwrapped, value, 10);
        var nullException = Capture<InvalidOperationException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(unwrapped, value, null));

        await Assert.That(convertedValue).IsTypeOf<long>();
        await Assert.That(convertedValue).IsEqualTo(10L);
        await Assert.That(convertedNull).IsNull();
        await Assert.That(unwrappedValue).IsEqualTo(10);
        await Assert.That(nullException).IsNotNull();
        await Assert.That(nullException!.Message).Contains("Nullable object must have a value");
    }

    [Test]
    public async Task LocalValueEvaluation_ConvertsCompatibilityMethodArgumentsBeforeInvocation()
    {
        var source = 257;
        var probe = new LocalMethodProbe();
        Expression<Func<int>> expression = () => probe.AcceptByte(unchecked((byte)source));

        var actual = ExpressionLocalValueEvaluator.Evaluate(expression.Body);

        await Assert.That(actual).IsEqualTo(1);
        await Assert.That(probe.LastByte).IsEqualTo((byte)1);
    }

    [Test]
    public async Task AotStrictLocalValueEvaluation_RejectsUserDefinedConversionsWithoutInvokingThem()
    {
        var probe = new UserDefinedConversionProbe();
        var conversion = Expression.Convert(
            Expression.Constant(new UserDefinedValue(probe, 7)),
            typeof(int));

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(
                conversion,
                null,
                null,
                ExpressionLocalValueEvaluationOptions.AotStrict));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("requires compatibility method reflection");
        await Assert.That(probe.InvocationCount).IsEqualTo(0);
    }

    [Test]
    public async Task CompatibilityLocalValueEvaluation_AllowsUserDefinedConversions()
    {
        var probe = new UserDefinedConversionProbe();
        var conversion = Expression.Convert(
            Expression.Constant(new UserDefinedValue(probe, 7)),
            typeof(int));

        var compatibilityResult = ExpressionLocalValueEvaluator.Evaluate(conversion);

        await Assert.That(compatibilityResult).IsEqualTo(7);
        await Assert.That(probe.InvocationCount).IsEqualTo(1);
    }

    [Test]
    public async Task AotStrictLocalValueEvaluation_RejectsCompatibilityMethodsWithoutInvokingThem()
    {
        Expression<Func<int>> expression = () => ThrowIfInvokedEmployeeNumber();

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(
                expression.Body,
                null,
                null,
                ExpressionLocalValueEvaluationOptions.AotStrict));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Local method call 'ThrowIfInvokedEmployeeNumber' requires compatibility method reflection");
    }

    private static TException? Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }

    private sealed class LocalMethodProbe
    {
        public int InvocationCount { get; private set; }

        public byte LastByte { get; private set; }

        public int GetEmployeeNumber()
        {
            InvocationCount++;
            return 10001;
        }

        public int AcceptByte(byte value)
        {
            LastByte = value;
            return value;
        }
    }

    private sealed class UserDefinedConversionProbe
    {
        public int InvocationCount { get; private set; }

        public int Convert(int value)
        {
            InvocationCount++;
            return value;
        }
    }

    private readonly record struct UserDefinedValue(UserDefinedConversionProbe Probe, int Value)
    {
        public static explicit operator int(UserDefinedValue value)
            => value.Probe.Convert(value.Value);
    }

    private enum LocalConversionKind : byte
    {
        First = 1,
        Second = 2
    }

    private static int ThrowIfInvokedEmployeeNumber()
        => throw new InvalidOperationException("AOT-strict local method evaluation should reject before invocation.");
}
