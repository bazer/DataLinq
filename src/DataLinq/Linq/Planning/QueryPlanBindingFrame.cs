using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal sealed class QueryPlanBindingFrame
{
    private readonly List<QueryPlanBinding> bindings = [];

    public IReadOnlyList<QueryPlanBinding> Bindings => new ReadOnlyCollection<QueryPlanBinding>(bindings);

    public QueryPlanCapturedValue CaptureScalar(object? value, Type? type = null)
    {
        var bindingType = type ?? value?.GetType() ?? typeof(object);
        var binding = Add(QueryPlanBindingKind.Scalar, bindingType, value, values: null, count: null);
        return new QueryPlanCapturedValue(binding.Id, binding.Type);
    }

    public QueryPlanLocalSequenceValue CaptureLocalSequence(object?[] values, Type? elementType = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        var bindingElementType = elementType ?? InferElementType(values);
        var binding = Add(QueryPlanBindingKind.LocalSequence, bindingElementType, value: null, values.ToArray(), values.Length);
        return new QueryPlanLocalSequenceValue(binding.Id, bindingElementType, values.Length);
    }

    private QueryPlanBinding Add(QueryPlanBindingKind kind, Type type, object? value, object?[]? values, int? count)
    {
        var binding = new QueryPlanBinding($"p{bindings.Count}", kind, type, value, values, count);
        bindings.Add(binding);
        return binding;
    }

    private static Type InferElementType(object?[] values)
    {
        foreach (var value in values)
        {
            if (value is not null)
                return value.GetType();
        }

        return typeof(object);
    }
}

internal sealed record QueryPlanBinding(
    string Id,
    QueryPlanBindingKind Kind,
    Type Type,
    object? Value,
    IReadOnlyList<object?>? Values,
    int? Count);

internal enum QueryPlanBindingKind
{
    Scalar,
    LocalSequence
}
