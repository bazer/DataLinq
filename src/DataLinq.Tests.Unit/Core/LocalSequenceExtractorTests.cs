using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Linq;

namespace DataLinq.Tests.Unit.Core;

public class LocalSequenceExtractorTests
{
    [Test]
    public async Task Evaluate_ProjectedLocalSequence_ReturnsProjectedValues()
    {
        var ids = new[]
        {
            new LocalId(10),
            new LocalId(20),
            new LocalId(30)
        };
        Expression<Func<IEnumerable<int>>> expression = () => ids.Select(x => x.Id);

        var values = LocalSequenceExtractor.Evaluate(expression.Body);

        await Assert.That(values).IsEquivalentTo(new object?[] { 10, 20, 30 });
    }

    [Test]
    public async Task Evaluate_EmptyLocalSequence_ReturnsEmptyValues()
    {
        var ids = Array.Empty<int>();
        Expression<Func<IEnumerable<int>>> expression = () => ids;

        var values = LocalSequenceExtractor.Evaluate(expression.Body);

        await Assert.That(values).IsEmpty();
    }

    [Test]
    public async Task TryEvaluate_NonSequenceExpression_ReturnsFalse()
    {
        var value = 42;
        Expression<Func<int>> expression = () => value;

        var result = LocalSequenceExtractor.TryEvaluate(expression.Body, out var values);

        await Assert.That(result).IsFalse();
        await Assert.That(values).IsEmpty();
    }

    [Test]
    public async Task UnwrapQueryColumnAccess_RemovesNullableValueAndConversionWrappers()
    {
        Expression<Func<NullableIdRow, int>> expression = row => row.Id!.Value;

        var unwrapped = LocalSequenceExtractor.UnwrapQueryColumnAccess(expression.Body);

        await Assert.That(unwrapped).IsAssignableTo<MemberExpression>();
        await Assert.That(((MemberExpression)unwrapped).Member.Name).IsEqualTo(nameof(NullableIdRow.Id));
    }

    private sealed record LocalId(int Id);

    private sealed class NullableIdRow
    {
        public int? Id { get; init; }
    }
}
