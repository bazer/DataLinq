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
    public async Task LocalValueEvaluation_RejectsUserMethodsWithoutInvokingThem()
    {
        var probe = new LocalMethodProbe();
        Expression<Func<int>> expression = () => probe.GetEmployeeNumber();

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionLocalValueEvaluator.Evaluate(expression.Body));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Local method call 'GetEmployeeNumber'");
        await Assert.That(probe.InvocationCount).IsEqualTo(0);
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
}
