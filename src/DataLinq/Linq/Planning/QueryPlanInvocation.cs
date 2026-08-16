using System;
using System.Collections.Generic;

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

        var orderedValues = template.BindingDeclarations.Count == 0
            ? Array.Empty<QueryPlanInvocationValue>()
            : new QueryPlanInvocationValue[template.BindingDeclarations.Count];
        if (values is IReadOnlyList<QueryPlanInvocationValue> valueList)
        {
            for (var index = 0; index < valueList.Count; index++)
                AddByDeclarationOrdinal(template.BindingDeclarations, orderedValues, valueList[index]);
        }
        else
        {
            foreach (var value in values)
                AddByDeclarationOrdinal(template.BindingDeclarations, orderedValues, value);
        }

        EnsureNoMissingValues(template.BindingDeclarations, orderedValues);

        for (var index = 0; index < orderedValues.Length; index++)
            orderedValues[index] = QueryPlanBindingValues.Freeze(orderedValues[index]);

        ValidateValues(template, orderedValues);

        var frozenValues = QueryPlanBindingValues.CreateDefensive(
            template.BindingDeclarations,
            orderedValues);

        return new QueryPlanInvocation(template, frozenValues);
    }

    /// <summary>
    /// Binds values whose defensive snapshots are owned exclusively by the expression parser.
    /// The parser supplies declaration-ordered, read-only values, so cloning them again would
    /// add allocations without adding mutation safety.
    /// </summary>
    internal static QueryPlanInvocation BindParserOwned(
        QueryPlanTemplate template,
        IReadOnlyList<QueryPlanInvocationValue> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        ValidateParserOwnedIds(template.BindingDeclarations, values);
        ValidateValues(template, values);

        return new QueryPlanInvocation(
            template,
            QueryPlanBindingValues.CreateParserOwned(template.BindingDeclarations, values));
    }

    private static void AddByDeclarationOrdinal(
        QueryPlanBindingDeclarations declarations,
        QueryPlanInvocationValue[] orderedValues,
        QueryPlanInvocationValue value)
    {
        if (value is null)
            throw new ArgumentException("Query plan invocation values cannot contain null entries.", nameof(value));

        if (!declarations.TryGetOrdinal(value.Id, out var ordinal))
            throw new QueryPlanInvocationException($"Invocation contains undeclared binding '{value.Id}'.");

        if (orderedValues[ordinal] is not null)
            throw new QueryPlanInvocationException($"Invocation binding '{value.Id}' is duplicated.");

        orderedValues[ordinal] = value;
    }

    private static void EnsureNoMissingValues(
        QueryPlanBindingDeclarations declarations,
        IReadOnlyList<QueryPlanInvocationValue> orderedValues)
    {
        for (var index = 0; index < declarations.Count; index++)
        {
            if (orderedValues[index] is null)
            {
                throw new QueryPlanInvocationException(
                    $"Invocation is missing binding '{declarations[index].Id}'.");
            }
        }
    }

    private static void ValidateParserOwnedIds(
        QueryPlanBindingDeclarations declarations,
        IReadOnlyList<QueryPlanInvocationValue> values)
    {
        if (values.Count != declarations.Count)
        {
            throw new QueryPlanInvocationException(
                $"Parser-owned invocation has {values.Count} bindings, but the template declares {declarations.Count}.");
        }

        for (var index = 0; index < declarations.Count; index++)
        {
            var value = values[index]
                ?? throw new ArgumentException("Query plan invocation values cannot contain null entries.", nameof(values));
            var declaration = declarations[index];
            if (!StringComparer.Ordinal.Equals(value.Id, declaration.Id))
            {
                throw new QueryPlanInvocationException(
                    $"Parser-owned invocation binding at ordinal {index} is '{value.Id}', expected '{declaration.Id}'.");
            }
        }
    }

    private static void ValidateValues(
        QueryPlanTemplate template,
        IReadOnlyList<QueryPlanInvocationValue> values)
    {
        for (var index = 0; index < template.BindingDeclarations.Count; index++)
        {
            var declaration = template.BindingDeclarations[index];
            var value = values[index];
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
                var actualNullCount = CountNulls(sequence.Values);
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
