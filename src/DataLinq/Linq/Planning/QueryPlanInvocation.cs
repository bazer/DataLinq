using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal sealed class QueryPlanInvocation
{
    private QueryPlanInvocation(QueryPlanTemplate template, QueryPlanBindingValues values)
    {
        Template = template;
        Values = values;
    }

    public QueryPlanTemplate Template { get; }

    public QueryPlanBindingValues Values { get; }

    public static QueryPlanInvocation Bind(
        QueryPlanTemplate template,
        IEnumerable<QueryPlanInvocationValue> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        var sourceValues = values.ToArray();
        var valuesById = ValidateIds(template.BindingDeclarations, sourceValues);
        var orderedValues = template.BindingDeclarations.Items
            .Select(declaration => valuesById[declaration.Id])
            .ToArray();
        var frozenValues = QueryPlanBindingValues.CreateValidated(orderedValues);
        var frozenValuesById = frozenValues.Items.ToDictionary(
            static value => value.Id,
            StringComparer.Ordinal);

        ValidateValues(template, frozenValuesById);

        return new QueryPlanInvocation(template, frozenValues);
    }

    private static Dictionary<string, QueryPlanInvocationValue> ValidateIds(
        QueryPlanBindingDeclarations declarations,
        IReadOnlyList<QueryPlanInvocationValue> values)
    {
        var valuesById = new Dictionary<string, QueryPlanInvocationValue>(values.Count, StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index]
                ?? throw new ArgumentException("Query plan invocation values cannot contain null entries.", nameof(values));

            if (!declarations.TryGet(value.Id, out _))
                throw new QueryPlanInvocationException($"Invocation contains undeclared binding '{value.Id}'.");

            if (!valuesById.TryAdd(value.Id, value))
                throw new QueryPlanInvocationException($"Invocation binding '{value.Id}' is duplicated.");
        }

        foreach (var declaration in declarations.Items)
        {
            if (!valuesById.ContainsKey(declaration.Id))
                throw new QueryPlanInvocationException($"Invocation is missing binding '{declaration.Id}'.");
        }

        return valuesById;
    }

    private static void ValidateValues(
        QueryPlanTemplate template,
        IReadOnlyDictionary<string, QueryPlanInvocationValue> valuesById)
    {
        foreach (var declaration in template.BindingDeclarations.Items)
        {
            var value = valuesById[declaration.Id];
            if (value.Kind != declaration.Kind)
            {
                throw new QueryPlanInvocationException(
                    $"Invocation binding '{declaration.Id}' has kind '{value.Kind}', expected '{declaration.Kind}'.");
            }

            switch (value)
            {
                case QueryPlanInvocationValue.Scalar scalar:
                    ValidateScalar(declaration, scalar.Value);
                    break;
                case QueryPlanInvocationValue.LocalSequence sequence:
                    ValidateLocalSequence(declaration, sequence.Values);
                    break;
                default:
                    throw new QueryPlanInvocationException(
                        $"Invocation binding '{declaration.Id}' has unsupported value type '{value.GetType().Name}'.");
            }

            ValidateSpecialization(template.Specialization, declaration, value);
        }
    }

    private static void ValidateScalar(QueryPlanBindingDeclaration declaration, object? value)
    {
        if (value is null)
        {
            if (!declaration.AllowsNull)
            {
                throw new QueryPlanInvocationException(
                    $"Invocation scalar binding '{declaration.Id}' cannot be null; expected model type '{TypeName(declaration.ModelType)}'.");
            }

            return;
        }

        if (!IsCompatibleType(declaration.ModelType, value.GetType()))
        {
            throw new QueryPlanInvocationException(
                $"Invocation scalar binding '{declaration.Id}' has CLR type '{TypeName(value.GetType())}', " +
                $"expected model type '{TypeName(declaration.ModelType)}'.");
        }
    }

    private static void ValidateLocalSequence(
        QueryPlanBindingDeclaration declaration,
        IReadOnlyList<object?> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null)
            {
                if (!declaration.AllowsNull)
                {
                    throw new QueryPlanInvocationException(
                        $"Invocation local-sequence binding '{declaration.Id}' contains null at index {index}; " +
                        $"expected element model type '{TypeName(declaration.ModelType)}'.");
                }

                continue;
            }

            if (!IsCompatibleType(declaration.ModelType, value.GetType()))
            {
                throw new QueryPlanInvocationException(
                    $"Invocation local-sequence binding '{declaration.Id}' contains CLR type '{TypeName(value.GetType())}' at index {index}, " +
                    $"expected element model type '{TypeName(declaration.ModelType)}'.");
            }
        }
    }

    private static void ValidateSpecialization(
        QueryPlanSpecialization specialization,
        QueryPlanBindingDeclaration declaration,
        QueryPlanInvocationValue value)
    {
        if (!specialization.TryGet(declaration.Id, out var constraint))
            return;

        if (constraint.Kind != declaration.Kind)
        {
            throw new QueryPlanInvocationException(
                $"Template specialization for binding '{declaration.Id}' has kind '{constraint.Kind}', expected '{declaration.Kind}'.");
        }

        switch (constraint, value)
        {
            case (QueryPlanBindingSpecialization.ScalarNullness scalarConstraint, QueryPlanInvocationValue.Scalar scalar):
                var actualNullness = scalar.Value is null
                    ? QueryPlanBindingNullness.Null
                    : QueryPlanBindingNullness.NonNull;
                if (actualNullness != scalarConstraint.Nullness)
                {
                    throw new QueryPlanInvocationException(
                        $"Invocation scalar binding '{declaration.Id}' has nullness '{actualNullness}', " +
                        $"but the template requires '{scalarConstraint.Nullness}'.");
                }

                break;
            case (QueryPlanBindingSpecialization.LocalSequenceShape sequenceConstraint, QueryPlanInvocationValue.LocalSequence sequence):
                var actualNullCount = sequence.Values.Count(static item => item is null);
                if (sequence.Values.Count != sequenceConstraint.Count ||
                    actualNullCount != sequenceConstraint.NullCount)
                {
                    throw new QueryPlanInvocationException(
                        $"Invocation local-sequence binding '{declaration.Id}' has shape " +
                        $"(count {sequence.Values.Count}, null count {actualNullCount}), but the template requires " +
                        $"exact shape (count {sequenceConstraint.Count}, null count {sequenceConstraint.NullCount}).");
                }

                break;
            default:
                throw new QueryPlanInvocationException(
                    $"Template specialization for binding '{declaration.Id}' is incompatible with invocation kind '{value.Kind}'.");
        }
    }

    private static bool IsCompatibleType(Type expectedType, Type actualType)
    {
        var normalizedExpected = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
        return normalizedExpected.IsAssignableFrom(actualType);
    }

    private static string TypeName(Type type) => type.FullName ?? type.Name;
}

internal sealed class QueryPlanInvocationException : InvalidOperationException
{
    public QueryPlanInvocationException(string message)
        : base(message)
    {
    }
}
