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
    public async Task LocalValueEvaluation_AllowsBuiltInNumericNegation()
    {
        var days = 10;
        var unsignedDays = 10U;
        var longDays = 10L;
        var singleDays = 10.5F;
        var doubleDays = 10.5D;
        var decimalDays = 10.5M;
        int? nullableDays = null;
        var origin = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        Expression<Func<int>> integerNegation = () => -days;
        Expression<Func<long>> unsignedNegation = () => -unsignedDays;
        Expression<Func<long>> longNegation = () => -longDays;
        Expression<Func<float>> singleNegation = () => -singleDays;
        Expression<Func<double>> doubleNegation = () => -doubleDays;
        Expression<Func<decimal>> decimalNegation = () => -decimalDays;
        Expression<Func<int?>> nullableNegation = () => -nullableDays;
        Expression<Func<DateTime>> compatibilityArgument = () => origin.AddDays(-days);

        var actualInteger = ExpressionLocalValueEvaluator.Evaluate(integerNegation.Body);
        var actualUnsigned = ExpressionLocalValueEvaluator.Evaluate(unsignedNegation.Body);
        var actualLong = ExpressionLocalValueEvaluator.Evaluate(longNegation.Body);
        var actualSingle = ExpressionLocalValueEvaluator.Evaluate(singleNegation.Body);
        var actualDouble = ExpressionLocalValueEvaluator.Evaluate(doubleNegation.Body);
        var actualDecimal = ExpressionLocalValueEvaluator.Evaluate(decimalNegation.Body);
        var actualNullable = ExpressionLocalValueEvaluator.Evaluate(nullableNegation.Body);
        var actualDate = ExpressionLocalValueEvaluator.Evaluate(compatibilityArgument.Body);

        await Assert.That(actualInteger).IsEqualTo(-10);
        await Assert.That(actualUnsigned).IsEqualTo(-10L);
        await Assert.That(actualLong).IsEqualTo(-10L);
        await Assert.That(actualSingle).IsEqualTo(-10.5F);
        await Assert.That(actualDouble).IsEqualTo(-10.5D);
        await Assert.That(actualDecimal).IsEqualTo(-10.5M);
        await Assert.That(actualNullable).IsNull();
        await Assert.That(actualDate).IsEqualTo(origin.AddDays(-days));
    }

    [Test]
    public async Task LocalValueEvaluation_PreservesCheckedAndUncheckedNegationOverflow()
    {
        var uncheckedNegation = Expression.Negate(Expression.Constant(int.MinValue));
        var checkedNegation = Expression.NegateChecked(Expression.Constant(int.MinValue));

        var uncheckedResult = ExpressionLocalValueEvaluator.Evaluate(uncheckedNegation);
        var checkedException = Capture<OverflowException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(checkedNegation));

        await Assert.That(uncheckedResult).IsEqualTo(int.MinValue);
        await Assert.That(checkedException).IsNotNull();
    }

    [Test]
    public async Task LocalValueEvaluation_PreservesInt16NegationTypeAndOverflow()
    {
        var uncheckedNegation = Expression.Negate(Expression.Constant((short)7));
        var checkedNegation = Expression.NegateChecked(Expression.Constant((short)7));
        var uncheckedOverflow = Expression.Negate(Expression.Constant(short.MinValue));
        var checkedOverflow = Expression.NegateChecked(Expression.Constant(short.MinValue));

        var uncheckedResult = ExpressionLocalValueEvaluator.Evaluate(uncheckedNegation);
        var checkedResult = ExpressionLocalValueEvaluator.Evaluate(checkedNegation);
        var uncheckedOverflowResult = ExpressionLocalValueEvaluator.Evaluate(uncheckedOverflow);
        var checkedOverflowException = Capture<OverflowException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(checkedOverflow));

        await Assert.That(uncheckedResult).IsTypeOf<short>();
        await Assert.That(uncheckedResult).IsEqualTo((short)-7);
        await Assert.That(checkedResult).IsTypeOf<short>();
        await Assert.That(checkedResult).IsEqualTo((short)-7);
        await Assert.That(uncheckedOverflowResult).IsTypeOf<short>();
        await Assert.That(uncheckedOverflowResult).IsEqualTo(short.MinValue);
        await Assert.That(checkedOverflowException).IsNotNull();
    }

    [Test]
    public async Task LocalValueEvaluation_RejectsUserDefinedNegationWithoutInvokingIt()
    {
        var value = new UserDefinedNumber(10);
        Expression<Func<UserDefinedNumber>> expression = () => -value;

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(expression.Body));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("user-defined unary operator");
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

        public int GetEmployeeNumber()
        {
            InvocationCount++;
            return 10001;
        }
    }

    private readonly record struct UserDefinedNumber(int Value)
    {
        public static UserDefinedNumber operator -(UserDefinedNumber value)
            => throw new InvalidOperationException("User-defined negation must not be invoked by local evaluation.");
    }

    private static int ThrowIfInvokedEmployeeNumber()
        => throw new InvalidOperationException("AOT-strict local method evaluation should reject before invocation.");
}
