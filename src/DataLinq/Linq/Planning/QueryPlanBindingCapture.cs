using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DataLinq.Linq.Planning;

/// <summary>
/// Parse-local accumulator that creates the three independent products of a capture:
/// a structural declaration, an invocation value, and an explicit specialization.
/// </summary>
internal sealed class QueryPlanBindingCapture : IQueryPlanSpecializationLookup
{
    private readonly List<QueryPlanBindingDeclaration> declarations = [];
    private readonly List<QueryPlanInvocationValue> invocationValues = [];
    private readonly List<QueryPlanBindingSpecialization> specializations = [];
    private readonly Dictionary<string, QueryPlanBindingSpecialization> specializationsByBindingId =
        new(StringComparer.Ordinal);
    private readonly ReadOnlyCollection<QueryPlanInvocationValue> invocationValueView;

    public QueryPlanBindingCapture()
    {
        invocationValueView = invocationValues.AsReadOnly();
    }

    public IReadOnlyList<QueryPlanInvocationValue> InvocationValues => invocationValueView;

    public QueryPlanScalarBindingReference CaptureScalar(
        object? value,
        Type? modelType = null,
        Type? providerType = null)
    {
        var declaredModelType = modelType ?? value?.GetType() ?? typeof(object);
        var declaredProviderType = providerType ?? declaredModelType;
        var id = NextId();

        Add(
            new QueryPlanBindingDeclaration(
                id,
                QueryPlanBindingKind.Scalar,
                declaredModelType,
                declaredProviderType,
                AllowsNull(declaredModelType)),
            new QueryPlanInvocationValue.Scalar(id, CopyScalarValue(value)),
            new QueryPlanBindingSpecialization.ScalarNullness(
                id,
                value is null ? QueryPlanBindingNullness.Null : QueryPlanBindingNullness.NonNull));

        return new QueryPlanScalarBindingReference(id, declaredModelType);
    }

    public QueryPlanLocalSequenceBindingReference CaptureLocalSequence(
        IReadOnlyList<object?> values,
        Type? elementModelType = null,
        Type? elementProviderType = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        var declaredModelType = elementModelType ?? InferElementType(values);
        var declaredProviderType = elementProviderType ?? declaredModelType;
        var id = NextId();
        var copiedValues = Array.AsReadOnly(CopyValues(values));

        Add(
            new QueryPlanBindingDeclaration(
                id,
                QueryPlanBindingKind.LocalSequence,
                declaredModelType,
                declaredProviderType,
                AllowsNull(declaredModelType)),
            new QueryPlanInvocationValue.LocalSequence(id, copiedValues),
            new QueryPlanBindingSpecialization.LocalSequenceShape(
                id,
                copiedValues.Count,
                CountNulls(copiedValues)));

        return new QueryPlanLocalSequenceBindingReference(id, declaredModelType);
    }

    public QueryPlanBindingDeclarations CreateDeclarations()
        => QueryPlanBindingDeclarations.From(declarations);

    public QueryPlanSpecialization CreateSpecialization()
        => QueryPlanSpecialization.From(specializations);

    public bool TryGetSpecialization(
        string bindingId,
        out QueryPlanBindingSpecialization specialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        return specializationsByBindingId.TryGetValue(bindingId, out specialization!);
    }

    private void Add(
        QueryPlanBindingDeclaration declaration,
        QueryPlanInvocationValue invocationValue,
        QueryPlanBindingSpecialization specialization)
    {
        declarations.Add(declaration);
        invocationValues.Add(invocationValue);
        specializations.Add(specialization);

        if (!specializationsByBindingId.TryAdd(specialization.BindingId, specialization))
        {
            throw new InvalidOperationException(
                $"Query plan binding specialization for '{specialization.BindingId}' was captured more than once.");
        }
    }

    private string NextId() => $"p{declarations.Count}";

    private static bool AllowsNull(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static Type InferElementType(IReadOnlyList<object?> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is { } value)
                return value.GetType();
        }

        return typeof(object);
    }

    private static int CountNulls(IReadOnlyList<object?> values)
    {
        var count = 0;
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is null)
                count++;
        }

        return count;
    }

    private static object? CopyScalarValue(object? value)
        => value is Array array ? array.Clone() : value;

    private static object?[] CopyValues(IReadOnlyList<object?> values)
    {
        var copy = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
            copy[index] = CopyScalarValue(values[index]);

        return copy;
    }
}
