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
    public async Task LocalValueEvaluation_EvaluatesCompatibilityMethodReceiversExactlyOnce()
    {
        var probe = new ReceiverProbe();
        Expression<Func<int>> expression = () => probe.GetReceiver().GetValue();

        var actual = ExpressionLocalValueEvaluator.Evaluate(expression.Body);

        await Assert.That(actual).IsEqualTo(10001);
        await Assert.That(probe.ReceiverFactoryInvocationCount).IsEqualTo(1);
        await Assert.That(probe.Receiver.GetValueInvocationCount).IsEqualTo(1);
    }

    [Test]
    public async Task LocalValueEvaluation_EvaluatesCompatibilityReceiverPropertiesExactlyOnce()
    {
        var probe = new ReceiverProbe();
        Expression<Func<int>> expression = () => probe.ReceiverProperty.GetValue();

        var actual = ExpressionLocalValueEvaluator.Evaluate(expression.Body);

        await Assert.That(actual).IsEqualTo(10001);
        await Assert.That(probe.ReceiverPropertyGetterCount).IsEqualTo(1);
        await Assert.That(probe.Receiver.GetValueInvocationCount).IsEqualTo(1);
    }

    [Test]
    public async Task LocalValueEvaluation_EvaluatesSupportedStringOperandsExactlyOnce()
    {
        var probe = new ReceiverProbe();
        Expression<Func<string>> expression = () => probe.GetText().Substring(probe.GetStart(), probe.GetLength());

        var actual = ExpressionLocalValueEvaluator.Evaluate(expression.Body);

        await Assert.That(actual).IsEqualTo("al");
        await Assert.That(probe.TextInvocationCount).IsEqualTo(1);
        await Assert.That(probe.StartInvocationCount).IsEqualTo(1);
        await Assert.That(probe.LengthInvocationCount).IsEqualTo(1);
    }

    [Test]
    public async Task LocalValueEvaluation_EvaluatesSupportedStringArgumentsBeforeRejectingNullReceiver()
    {
        string? text = null;
        var probe = new ReceiverProbe();
        Expression<Func<string>> expression = () => text!.Substring(probe.GetStart());

        var exception = Capture<NullReferenceException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(expression.Body));

        await Assert.That(exception).IsNotNull();
        await Assert.That(probe.StartInvocationCount).IsEqualTo(1);
    }

    [Test]
    public async Task LocalValueEvaluation_EvaluatesUnsupportedStringOperandsExactlyOnce()
    {
        var probe = new ReceiverProbe();
        Expression<Func<string>> expression = () => probe.GetText().PadLeft(probe.GetWidth());

        var actual = ExpressionLocalValueEvaluator.Evaluate(expression.Body);

        await Assert.That(actual).IsEqualTo("  Sales");
        await Assert.That(probe.TextInvocationCount).IsEqualTo(1);
        await Assert.That(probe.WidthInvocationCount).IsEqualTo(1);
    }

    [Test]
    public async Task AotStrictLocalValueEvaluation_RejectsBeforeEvaluatingCompatibilityOperands()
    {
        var probe = new ReceiverProbe();
        var text = Expression.Call(
            Expression.Constant(probe),
            nameof(ReceiverProbe.GetText),
            Type.EmptyTypes);
        var width = Expression.Call(
            Expression.Constant(probe),
            nameof(ReceiverProbe.GetWidth),
            Type.EmptyTypes);
        var expression = Expression.Call(
            text,
            nameof(string.PadLeft),
            Type.EmptyTypes,
            width);

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(
                expression,
                null,
                null,
                ExpressionLocalValueEvaluationOptions.AotStrict));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Local method call 'PadLeft' requires compatibility method reflection");
        await Assert.That(probe.TextInvocationCount).IsEqualTo(0);
        await Assert.That(probe.WidthInvocationCount).IsEqualTo(0);
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

    private sealed class ReceiverProbe
    {
        public LocalReceiver Receiver { get; } = new();

        public int ReceiverFactoryInvocationCount { get; private set; }

        public int ReceiverPropertyGetterCount { get; private set; }

        public int TextInvocationCount { get; private set; }

        public int StartInvocationCount { get; private set; }

        public int LengthInvocationCount { get; private set; }

        public int WidthInvocationCount { get; private set; }

        public LocalReceiver ReceiverProperty
        {
            get
            {
                ReceiverPropertyGetterCount++;
                return Receiver;
            }
        }

        public LocalReceiver GetReceiver()
        {
            ReceiverFactoryInvocationCount++;
            return Receiver;
        }

        public string GetText()
        {
            TextInvocationCount++;
            return "Sales";
        }

        public int GetStart()
        {
            StartInvocationCount++;
            return 1;
        }

        public int GetLength()
        {
            LengthInvocationCount++;
            return 2;
        }

        public int GetWidth()
        {
            WidthInvocationCount++;
            return 7;
        }
    }

    private sealed class LocalReceiver
    {
        public int GetValueInvocationCount { get; private set; }

        public int GetValue()
        {
            GetValueInvocationCount++;
            return 10001;
        }
    }

    private static int ThrowIfInvokedEmployeeNumber()
        => throw new InvalidOperationException("AOT-strict local method evaluation should reject before invocation.");
}
