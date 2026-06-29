using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DataLinq.Linq.Planning;

internal interface IQueryPlanBindingLookup
{
    bool TryGet(string id, out QueryPlanBinding binding);
}

internal sealed class QueryPlanBindingFrame : IQueryPlanBindingLookup
{
    private readonly List<QueryPlanBinding> bindings = [];
    private readonly ReadOnlyCollection<QueryPlanBinding> bindingView;

    public QueryPlanBindingFrame()
    {
        bindingView = bindings.AsReadOnly();
    }

    public IReadOnlyList<QueryPlanBinding> Bindings => bindingView;

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
        var binding = Add(QueryPlanBindingKind.LocalSequence, bindingElementType, value: null, CopyValues(values), values.Length);
        return new QueryPlanLocalSequenceValue(binding.Id, bindingElementType, values.Length);
    }

    public QueryPlanBindings Freeze() => QueryPlanBindings.From(bindings);

    public bool TryGet(string id, out QueryPlanBinding binding)
    {
        for (var index = 0; index < bindings.Count; index++)
        {
            var candidate = bindings[index];
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                binding = candidate;
                return true;
            }
        }

        binding = null!;
        return false;
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

    private static object?[] CopyValues(IReadOnlyList<object?> values)
    {
        var copy = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
            copy[index] = values[index];

        return copy;
    }
}

internal sealed class QueryPlanBindings : IQueryPlanBindingLookup
{
    public static QueryPlanBindings Empty { get; } = new([], []);

    private readonly QueryPlanBinding[] bindings;
    private readonly IReadOnlyList<QueryPlanBinding> bindingView;
    private readonly Dictionary<string, QueryPlanBinding> bindingsById;

    private QueryPlanBindings(QueryPlanBinding[] bindings, Dictionary<string, QueryPlanBinding> bindingsById)
    {
        this.bindings = bindings;
        this.bindingsById = bindingsById;
        bindingView = Array.AsReadOnly(bindings);
    }

    public int Count => bindings.Length;

    public IReadOnlyList<QueryPlanBinding> Items => bindingView;

    public QueryPlanBinding this[int index] => bindings[index];

    public static QueryPlanBindings From(IReadOnlyList<QueryPlanBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (bindings.Count == 0)
            return Empty;

        var frozen = new QueryPlanBinding[bindings.Count];
        var bindingsById = new Dictionary<string, QueryPlanBinding>(bindings.Count, StringComparer.Ordinal);

        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = FreezeBinding(bindings[index], index);
            if (!bindingsById.TryAdd(binding.Id, binding))
                throw new ArgumentException($"Query plan binding id '{binding.Id}' is duplicated.", nameof(bindings));

            frozen[index] = binding;
        }

        return new QueryPlanBindings(frozen, bindingsById);
    }

    public bool TryGet(string id, out QueryPlanBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return bindingsById.TryGetValue(id, out binding!);
    }

    private static QueryPlanBinding FreezeBinding(QueryPlanBinding binding, int index)
    {
        if (binding is null)
            throw new ArgumentException("Query plan bindings cannot contain null entries.", nameof(binding));

        if (string.IsNullOrWhiteSpace(binding.Id))
            throw new ArgumentException($"Query plan binding at index {index} must have an id.", nameof(binding));

        if (binding.Type is null)
            throw new ArgumentException($"Query plan binding '{binding.Id}' must have a CLR type.", nameof(binding));

        if (binding.Values is null)
            return binding;

        return binding with { Values = CopyValues(binding.Values) };
    }

    private static object?[] CopyValues(IReadOnlyList<object?> values)
    {
        var copy = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
            copy[index] = values[index];

        return copy;
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
