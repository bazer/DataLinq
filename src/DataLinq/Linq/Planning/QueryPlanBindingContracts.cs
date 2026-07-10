using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal sealed record QueryPlanBindingDeclaration
{
    public QueryPlanBindingDeclaration(
        string Id,
        QueryPlanBindingKind Kind,
        Type ModelType,
        Type ProviderType,
        bool AllowsNull)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentNullException.ThrowIfNull(ModelType);
        ArgumentNullException.ThrowIfNull(ProviderType);

        if (AllowsNull && ModelType.IsValueType && Nullable.GetUnderlyingType(ModelType) is null)
        {
            throw new ArgumentException(
                $"Non-nullable model type '{ModelType}' cannot declare nullable invocation values.",
                nameof(AllowsNull));
        }

        this.Id = Id;
        this.Kind = Kind;
        this.ModelType = ModelType;
        this.ProviderType = ProviderType;
        this.AllowsNull = AllowsNull;
    }

    public string Id { get; }

    public QueryPlanBindingKind Kind { get; }

    public Type ModelType { get; }

    public Type ProviderType { get; }

    public bool AllowsNull { get; }
}

internal sealed class QueryPlanBindingDeclarations
{
    public static QueryPlanBindingDeclarations Empty { get; } = new([], []);

    private readonly QueryPlanBindingDeclaration[] declarations;
    private readonly ReadOnlyCollection<QueryPlanBindingDeclaration> declarationView;
    private readonly Dictionary<string, QueryPlanBindingDeclaration> declarationsById;

    private QueryPlanBindingDeclarations(
        QueryPlanBindingDeclaration[] declarations,
        Dictionary<string, QueryPlanBindingDeclaration> declarationsById)
    {
        this.declarations = declarations;
        this.declarationsById = declarationsById;
        declarationView = Array.AsReadOnly(declarations);
    }

    public int Count => declarations.Length;

    public IReadOnlyList<QueryPlanBindingDeclaration> Items => declarationView;

    public QueryPlanBindingDeclaration this[int index] => declarations[index];

    public static QueryPlanBindingDeclarations From(IEnumerable<QueryPlanBindingDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var source = declarations.ToArray();
        if (source.Length == 0)
            return Empty;

        var frozen = new QueryPlanBindingDeclaration[source.Length];
        var byId = new Dictionary<string, QueryPlanBindingDeclaration>(source.Length, StringComparer.Ordinal);

        for (var index = 0; index < source.Length; index++)
        {
            var declaration = source[index]
                ?? throw new ArgumentException("Query plan binding declarations cannot contain null entries.", nameof(declarations));

            if (!byId.TryAdd(declaration.Id, declaration))
            {
                throw new ArgumentException(
                    $"Query plan binding declaration id '{declaration.Id}' is duplicated.",
                    nameof(declarations));
            }

            frozen[index] = declaration;
        }

        return new QueryPlanBindingDeclarations(frozen, byId);
    }

    public bool TryGet(string id, out QueryPlanBindingDeclaration declaration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return declarationsById.TryGetValue(id, out declaration!);
    }
}

internal abstract record QueryPlanInvocationValue(string Id, QueryPlanBindingKind Kind)
{
    internal sealed record Scalar : QueryPlanInvocationValue
    {
        public Scalar(string id, object? value)
            : base(id, QueryPlanBindingKind.Scalar)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            Value = value;
        }

        public object? Value { get; }
    }

    internal sealed record LocalSequence : QueryPlanInvocationValue
    {
        public LocalSequence(string id, IReadOnlyList<object?> values)
            : base(id, QueryPlanBindingKind.LocalSequence)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(values);
            Values = values;
        }

        public IReadOnlyList<object?> Values { get; }
    }
}

internal sealed class QueryPlanBindingValues
{
    public static QueryPlanBindingValues Empty { get; } = new([], []);

    private readonly QueryPlanInvocationValue[] values;
    private readonly ReadOnlyCollection<QueryPlanInvocationValue> valueView;
    private readonly Dictionary<string, QueryPlanInvocationValue> valuesById;

    private QueryPlanBindingValues(
        QueryPlanInvocationValue[] values,
        Dictionary<string, QueryPlanInvocationValue> valuesById)
    {
        this.values = values;
        this.valuesById = valuesById;
        valueView = Array.AsReadOnly(values);
    }

    public int Count => values.Length;

    public IReadOnlyList<QueryPlanInvocationValue> Items => valueView;

    public QueryPlanInvocationValue this[int index] => values[index];

    public bool TryGet(string id, out QueryPlanInvocationValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return valuesById.TryGetValue(id, out value!);
    }

    internal static QueryPlanBindingValues CreateValidated(IReadOnlyList<QueryPlanInvocationValue> values)
    {
        if (values.Count == 0)
            return Empty;

        var frozen = new QueryPlanInvocationValue[values.Count];
        var byId = new Dictionary<string, QueryPlanInvocationValue>(values.Count, StringComparer.Ordinal);

        for (var index = 0; index < values.Count; index++)
        {
            var value = Freeze(values[index]);
            byId.Add(value.Id, value);
            frozen[index] = value;
        }

        return new QueryPlanBindingValues(frozen, byId);
    }

    private static QueryPlanInvocationValue Freeze(QueryPlanInvocationValue value)
    {
        return value switch
        {
            QueryPlanInvocationValue.Scalar scalar => new QueryPlanInvocationValue.Scalar(
                scalar.Id,
                CopyScalarValue(scalar.Value)),
            QueryPlanInvocationValue.LocalSequence sequence => new QueryPlanInvocationValue.LocalSequence(
                sequence.Id,
                Array.AsReadOnly(sequence.Values.ToArray())),
            _ => throw new ArgumentException(
                $"Unknown query plan invocation value '{value.GetType().Name}'.",
                nameof(value))
        };
    }

    private static object? CopyScalarValue(object? value)
        => value is Array array ? array.Clone() : value;
}

internal enum QueryPlanBindingNullness
{
    Null,
    NonNull
}

internal abstract record QueryPlanBindingSpecialization(string BindingId, QueryPlanBindingKind Kind)
{
    internal sealed record ScalarNullness : QueryPlanBindingSpecialization
    {
        public ScalarNullness(string bindingId, QueryPlanBindingNullness nullness)
            : base(bindingId, QueryPlanBindingKind.Scalar)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);

            if (!Enum.IsDefined(nullness))
                throw new ArgumentOutOfRangeException(nameof(nullness), nullness, "Unknown query plan scalar nullness.");

            Nullness = nullness;
        }

        public QueryPlanBindingNullness Nullness { get; }
    }

    internal sealed record LocalSequenceCount : QueryPlanBindingSpecialization
    {
        public LocalSequenceCount(string bindingId, int count)
            : base(bindingId, QueryPlanBindingKind.LocalSequence)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            Count = count;
        }

        public int Count { get; }
    }
}

internal sealed class QueryPlanSpecialization
{
    public static QueryPlanSpecialization Empty { get; } = new([], []);

    private readonly QueryPlanBindingSpecialization[] constraints;
    private readonly ReadOnlyCollection<QueryPlanBindingSpecialization> constraintView;
    private readonly Dictionary<string, QueryPlanBindingSpecialization> constraintsByBindingId;

    private QueryPlanSpecialization(
        QueryPlanBindingSpecialization[] constraints,
        Dictionary<string, QueryPlanBindingSpecialization> constraintsByBindingId)
    {
        this.constraints = constraints;
        this.constraintsByBindingId = constraintsByBindingId;
        constraintView = Array.AsReadOnly(constraints);
    }

    public int Count => constraints.Length;

    public IReadOnlyList<QueryPlanBindingSpecialization> Items => constraintView;

    public static QueryPlanSpecialization From(IEnumerable<QueryPlanBindingSpecialization> constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);

        var source = constraints.ToArray();
        if (source.Length == 0)
            return Empty;

        var frozen = new QueryPlanBindingSpecialization[source.Length];
        var byBindingId = new Dictionary<string, QueryPlanBindingSpecialization>(source.Length, StringComparer.Ordinal);

        for (var index = 0; index < source.Length; index++)
        {
            var constraint = source[index]
                ?? throw new ArgumentException("Query plan specialization cannot contain null entries.", nameof(constraints));

            if (!byBindingId.TryAdd(constraint.BindingId, constraint))
            {
                throw new ArgumentException(
                    $"Query plan binding specialization for '{constraint.BindingId}' is duplicated.",
                    nameof(constraints));
            }

            frozen[index] = constraint;
        }

        return new QueryPlanSpecialization(frozen, byBindingId);
    }

    public bool TryGet(string bindingId, out QueryPlanBindingSpecialization specialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        return constraintsByBindingId.TryGetValue(bindingId, out specialization!);
    }
}
