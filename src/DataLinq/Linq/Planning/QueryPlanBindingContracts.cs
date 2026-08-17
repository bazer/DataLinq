using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal enum QueryPlanBindingKind
{
    Scalar,
    LocalSequence
}

internal interface IQueryPlanSpecializationLookup
{
    bool TryGetSpecialization(
        string bindingId,
        out QueryPlanBindingSpecialization specialization);
}

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
    private readonly Dictionary<string, int> declarationOrdinalsById;

    private QueryPlanBindingDeclarations(
        QueryPlanBindingDeclaration[] declarations,
        Dictionary<string, int> declarationOrdinalsById)
    {
        this.declarations = declarations;
        this.declarationOrdinalsById = declarationOrdinalsById;
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
        var ordinalsById = new Dictionary<string, int>(source.Length, StringComparer.Ordinal);

        for (var index = 0; index < source.Length; index++)
        {
            var declaration = source[index]
                ?? throw new ArgumentException("Query plan binding declarations cannot contain null entries.", nameof(declarations));

            if (!ordinalsById.TryAdd(declaration.Id, index))
            {
                throw new ArgumentException(
                    $"Query plan binding declaration id '{declaration.Id}' is duplicated.",
                    nameof(declarations));
            }

            frozen[index] = declaration;
        }

        return new QueryPlanBindingDeclarations(frozen, ordinalsById);
    }

    public bool TryGet(string id, out QueryPlanBindingDeclaration declaration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (declarationOrdinalsById.TryGetValue(id, out var ordinal))
        {
            declaration = declarations[ordinal];
            return true;
        }

        declaration = null!;
        return false;
    }

    internal bool TryGetOrdinal(string id, out int ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return declarationOrdinalsById.TryGetValue(id, out ordinal);
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
    public static QueryPlanBindingValues Empty { get; } = new(
        QueryPlanBindingDeclarations.Empty,
        Array.Empty<QueryPlanInvocationValue>());

    private readonly QueryPlanBindingDeclarations declarations;
    private readonly IReadOnlyList<QueryPlanInvocationValue> values;

    private QueryPlanBindingValues(
        QueryPlanBindingDeclarations declarations,
        IReadOnlyList<QueryPlanInvocationValue> values)
    {
        this.declarations = declarations;
        this.values = values;
    }

    public int Count => values.Count;

    public IReadOnlyList<QueryPlanInvocationValue> Items => values;

    public QueryPlanInvocationValue this[int index] => values[index];

    public bool TryGet(string id, out QueryPlanInvocationValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (declarations.TryGetOrdinal(id, out var ordinal) && ordinal < values.Count)
        {
            value = values[ordinal];
            return true;
        }

        value = null!;
        return false;
    }

    internal static QueryPlanBindingValues CreateDefensive(
        QueryPlanBindingDeclarations declarations,
        QueryPlanInvocationValue[] values)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length == 0)
            return Empty;

        return new QueryPlanBindingValues(declarations, Array.AsReadOnly(values));
    }

    internal static QueryPlanBindingValues CreateParserOwned(
        QueryPlanBindingDeclarations declarations,
        IReadOnlyList<QueryPlanInvocationValue> values)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(values);

        return values.Count == 0
            ? Empty
            : new QueryPlanBindingValues(declarations, values);
    }

    internal static QueryPlanInvocationValue Freeze(QueryPlanInvocationValue value)
    {
        return value switch
        {
            QueryPlanInvocationValue.Scalar scalar => new QueryPlanInvocationValue.Scalar(
                scalar.Id,
                CopyScalarValue(scalar.Value)),
            QueryPlanInvocationValue.LocalSequence sequence => new QueryPlanInvocationValue.LocalSequence(
                sequence.Id,
                Array.AsReadOnly(CopyValues(sequence.Values))),
            _ => throw new ArgumentException(
                $"Unknown query plan invocation value '{value.GetType().Name}'.",
                nameof(value))
        };
    }

    private static object? CopyScalarValue(object? value)
        => value is Array array ? array.Clone() : value;

    private static object?[] CopyValues(IReadOnlyList<object?> values)
    {
        var copy = new object?[values.Count];
        var index = 0;

        foreach (var value in values)
        {
            if (index == copy.Length)
            {
                throw new ArgumentException(
                    "Query plan local-sequence value enumerated more items than its declared count.",
                    nameof(values));
            }

            copy[index++] = CopyScalarValue(value);
        }

        if (index != copy.Length)
        {
            throw new ArgumentException(
                "Query plan local-sequence value enumerated fewer items than its declared count.",
                nameof(values));
        }

        return copy;
    }
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

    internal sealed record LocalSequenceShape : QueryPlanBindingSpecialization
    {
        public LocalSequenceShape(string bindingId, int count, int nullCount)
            : base(bindingId, QueryPlanBindingKind.LocalSequence)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfNegative(nullCount);
            if (nullCount > count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nullCount),
                    nullCount,
                    "Local-sequence null count cannot exceed its total count.");
            }

            Count = count;
            NullCount = nullCount;
        }

        public int Count { get; }

        public int NullCount { get; }
    }
}

internal sealed class QueryPlanSpecialization : IQueryPlanSpecializationLookup
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

    public bool TryGetSpecialization(
        string bindingId,
        out QueryPlanBindingSpecialization specialization)
        => TryGet(bindingId, out specialization);
}
