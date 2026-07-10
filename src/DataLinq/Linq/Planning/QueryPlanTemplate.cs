using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal sealed class QueryPlanTemplate
{
    public QueryPlanTemplate(
        IEnumerable<QueryPlanSourceSlot> sources,
        IEnumerable<QueryPlanOperation> operations,
        QueryPlanProjection projection,
        QueryPlanResult result,
        QueryPlanBindingDeclarations bindingDeclarations,
        QueryPlanSpecialization specialization)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bindingDeclarations);
        ArgumentNullException.ThrowIfNull(specialization);

        Sources = Freeze(sources, nameof(sources));
        Operations = Freeze(operations, nameof(operations));
        Projection = projection;
        Result = result;
        BindingDeclarations = bindingDeclarations;
        Specialization = specialization;

        ValidateSourceIds(Sources);
        QueryPlanTemplateValidator.Validate(this);
    }

    public IReadOnlyList<QueryPlanSourceSlot> Sources { get; }

    public IReadOnlyList<QueryPlanOperation> Operations { get; }

    public QueryPlanProjection Projection { get; }

    public QueryPlanResult Result { get; }

    public QueryPlanBindingDeclarations BindingDeclarations { get; }

    public QueryPlanSpecialization Specialization { get; }

    public QueryPlanSourceSlot GetSource(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return Sources.FirstOrDefault(source => source.Id == id)
            ?? throw new InvalidOperationException($"Query plan source slot '{id}' does not exist.");
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values, string parameterName)
    {
        var array = values.ToArray();
        if (array.Any(static value => value is null))
            throw new ArgumentException("Query plan collections cannot contain null entries.", parameterName);

        return Array.AsReadOnly(array);
    }

    private static void ValidateSourceIds(IReadOnlyList<QueryPlanSourceSlot> sources)
    {
        if (sources.Count == 0)
            throw new ArgumentException("A query plan template must contain at least one source slot.", nameof(sources));

        var duplicate = sources
            .GroupBy(static source => source.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"Query plan source slot id '{duplicate.Key}' is duplicated.", nameof(sources));
    }
}

internal static class QueryPlanTemplateValidator
{
    public static void Validate(QueryPlanTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        ValidateSpecialization(template.BindingDeclarations, template.Specialization);
        ValidateSourceReferences(template);

        foreach (var operation in template.Operations)
            ValidateOperation(operation, template.BindingDeclarations);

        ValidateProjection(template.Projection, template.BindingDeclarations);
        if (template.Result.AggregateSelector is not null)
            ValidateValue(template.Result.AggregateSelector, template.BindingDeclarations);
    }

    private static void ValidateSpecialization(
        QueryPlanBindingDeclarations declarations,
        QueryPlanSpecialization specialization)
    {
        foreach (var constraint in specialization.Items)
        {
            if (!declarations.TryGet(constraint.BindingId, out var declaration))
            {
                throw new ArgumentException(
                    $"Query plan specialization references undeclared binding '{constraint.BindingId}'.",
                    nameof(specialization));
            }

            if (declaration.Kind != constraint.Kind)
            {
                throw new ArgumentException(
                    $"Query plan specialization for binding '{constraint.BindingId}' has kind '{constraint.Kind}', " +
                    $"but the declaration has kind '{declaration.Kind}'.",
                    nameof(specialization));
            }

            if (constraint is QueryPlanBindingSpecialization.ScalarNullness
                {
                    Nullness: QueryPlanBindingNullness.Null
                } && !declaration.AllowsNull)
            {
                throw new ArgumentException(
                    $"Query plan specialization for binding '{constraint.BindingId}' requires null, " +
                    "but the declaration does not allow null invocation values.",
                    nameof(specialization));
            }
        }

        foreach (var declaration in declarations.Items)
        {
            if (!specialization.TryGet(declaration.Id, out _))
            {
                throw new ArgumentException(
                    $"Query plan binding declaration '{declaration.Id}' has no explicit specialization.",
                    nameof(specialization));
            }
        }
    }

    private static void ValidateOperation(
        QueryPlanOperation operation,
        QueryPlanBindingDeclarations declarations)
    {
        switch (operation)
        {
            case QueryPlanOperation.Where where:
                ValidatePredicate(where.Predicate, declarations);
                break;
            case QueryPlanOperation.Having having:
                ValidatePredicate(having.Predicate, declarations);
                break;
            case QueryPlanOperation.OrderBy orderBy:
                foreach (var ordering in orderBy.Orderings)
                    ValidateValue(ordering.Value, declarations);
                break;
            case QueryPlanOperation.Skip skip:
                ValidateValue(skip.Count, declarations);
                break;
            case QueryPlanOperation.Take take:
                ValidateValue(take.Count, declarations);
                break;
            case QueryPlanOperation.Pushdown pushdown:
                foreach (var innerOperation in pushdown.Operations)
                    ValidateOperation(innerOperation, declarations);
                foreach (var ordering in pushdown.PreservedOrderings)
                    ValidateValue(ordering.Value, declarations);
                break;
            case QueryPlanOperation.GroupBy groupBy:
                foreach (var key in groupBy.Keys)
                    ValidateValue(key, declarations);
                break;
            case QueryPlanOperation.Join:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan operation '{operation.GetType().Name}'.",
                    nameof(operation));
        }
    }

    private static void ValidatePredicate(
        QueryPlanPredicate predicate,
        QueryPlanBindingDeclarations declarations)
    {
        switch (predicate)
        {
            case QueryPlanPredicate.Fixed:
                break;
            case QueryPlanPredicate.And and:
                foreach (var term in and.Terms)
                    ValidatePredicate(term, declarations);
                break;
            case QueryPlanPredicate.Or or:
                foreach (var term in or.Terms)
                    ValidatePredicate(term, declarations);
                break;
            case QueryPlanPredicate.Not not:
                ValidatePredicate(not.Predicate, declarations);
                break;
            case QueryPlanPredicate.Compare compare:
                ValidateValue(compare.Left, declarations);
                ValidateValue(compare.Right, declarations);
                break;
            case QueryPlanPredicate.In inPredicate:
                ValidateValue(inPredicate.Item, declarations);
                ValidateValue(inPredicate.Sequence, declarations);
                break;
            case QueryPlanPredicate.Exists exists:
                if (exists.Predicate is not null)
                    ValidatePredicate(exists.Predicate, declarations);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan predicate '{predicate.GetType().Name}'.",
                    nameof(predicate));
        }
    }

    private static void ValidateProjection(
        QueryPlanProjection projection,
        QueryPlanBindingDeclarations declarations)
    {
        switch (projection)
        {
            case QueryPlanProjection.Anonymous anonymous:
                ValidateMembers(anonymous.Members, declarations);
                break;
            case QueryPlanProjection.JoinedRowLocal joined:
                ValidateMembers(joined.Members, declarations);
                break;
            case QueryPlanProjection.SqlRow sqlRow:
                ValidateMembers(sqlRow.Members, declarations);
                break;
            case QueryPlanProjection.GroupedAggregate grouped:
                ValidateMembers(grouped.Members, declarations);
                break;
            case QueryPlanProjection.Entity:
            case QueryPlanProjection.ScalarMember:
            case QueryPlanProjection.ComputedRowLocal:
            case QueryPlanProjection.TransparentIdentifier:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan projection '{projection.GetType().Name}'.",
                    nameof(projection));
        }
    }

    private static void ValidateMembers(
        IReadOnlyList<QueryPlanProjectionMember> members,
        QueryPlanBindingDeclarations declarations)
    {
        foreach (var member in members)
            ValidateValue(member.Value, declarations);
    }

    private static void ValidateValue(
        QueryPlanValue value,
        QueryPlanBindingDeclarations declarations)
    {
        switch (value)
        {
            case QueryPlanColumnValue:
                break;
            case QueryPlanIntrinsicValue intrinsic:
                ValidateIntrinsic(intrinsic);
                break;
            case QueryPlanScalarBindingReference scalar:
                ValidateReference(
                    scalar.BindingId,
                    QueryPlanBindingKind.Scalar,
                    scalar.ClrType,
                    declarations);
                break;
            case QueryPlanLocalSequenceBindingReference sequence:
                ValidateReference(
                    sequence.BindingId,
                    QueryPlanBindingKind.LocalSequence,
                    sequence.ElementType,
                    declarations);
                break;
            case QueryPlanFunctionValue function:
                foreach (var argument in function.Arguments)
                    ValidateValue(argument, declarations);
                break;
            case QueryPlanConvertedValue converted:
                ValidateValue(converted.Value, declarations);
                break;
            case QueryPlanGroupKeyValue groupKey:
                ValidateValue(groupKey.Key, declarations);
                break;
            case QueryPlanGroupedAggregateValue groupedAggregate when groupedAggregate.Selector is not null:
                ValidateValue(groupedAggregate.Selector, declarations);
                break;
            case QueryPlanGroupedAggregateValue:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan value '{value.GetType().Name}'.",
                    nameof(value));
        }
    }

    private static void ValidateIntrinsic(QueryPlanIntrinsicValue intrinsic)
    {
        if (!Enum.IsDefined(intrinsic.Intrinsic))
        {
            throw new ArgumentException(
                $"Query plan intrinsic kind '{intrinsic.Intrinsic}' is not defined.",
                nameof(intrinsic));
        }

        if (intrinsic.ClrType == typeof(void) ||
            intrinsic.ClrType.IsByRef ||
            intrinsic.ClrType.IsPointer ||
            intrinsic.ClrType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"Query plan intrinsic '{intrinsic.Intrinsic}' has invalid CLR type '{intrinsic.ClrType}'.",
                nameof(intrinsic));
        }

        if (intrinsic.Intrinsic == QueryPlanIntrinsicKind.Null &&
            intrinsic.ClrType.IsValueType &&
            Nullable.GetUnderlyingType(intrinsic.ClrType) is null)
        {
            throw new ArgumentException(
                $"Null query plan intrinsics require a reference or nullable CLR type, not '{intrinsic.ClrType}'.",
                nameof(intrinsic));
        }

        if (intrinsic.Intrinsic is QueryPlanIntrinsicKind.BooleanTrue or QueryPlanIntrinsicKind.BooleanFalse &&
            intrinsic.ClrType != typeof(bool))
        {
            throw new ArgumentException(
                $"Boolean query plan intrinsic '{intrinsic.Intrinsic}' requires CLR type '{typeof(bool)}', not '{intrinsic.ClrType}'.",
                nameof(intrinsic));
        }
    }

    private static void ValidateSourceReferences(QueryPlanTemplate template)
    {
        var sourcesById = template.Sources.ToDictionary(static source => source.Id, StringComparer.Ordinal);

        foreach (var operation in template.Operations)
            ValidateOperationSources(operation, sourcesById);

        ValidateProjectionSources(template.Projection, sourcesById);
        if (template.Result.AggregateSelector is not null)
            ValidateValueSources(template.Result.AggregateSelector, sourcesById);
    }

    private static void ValidateOperationSources(
        QueryPlanOperation operation,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (operation)
        {
            case QueryPlanOperation.Where where:
                ValidatePredicateSources(where.Predicate, sourcesById);
                break;
            case QueryPlanOperation.Having having:
                ValidatePredicateSources(having.Predicate, sourcesById);
                break;
            case QueryPlanOperation.OrderBy orderBy:
                foreach (var ordering in orderBy.Orderings)
                    ValidateValueSources(ordering.Value, sourcesById);
                break;
            case QueryPlanOperation.Skip skip:
                ValidateValueSources(skip.Count, sourcesById);
                break;
            case QueryPlanOperation.Take take:
                ValidateValueSources(take.Count, sourcesById);
                break;
            case QueryPlanOperation.Join join:
                ValidateSource(join.JoinShape.LeftSource, sourcesById);
                ValidateColumn(join.JoinShape.LeftSource, join.JoinShape.LeftColumn);
                ValidateSource(join.JoinShape.RightSource, sourcesById);
                ValidateColumn(join.JoinShape.RightSource, join.JoinShape.RightColumn);
                break;
            case QueryPlanOperation.Pushdown pushdown:
                foreach (var innerOperation in pushdown.Operations)
                    ValidateOperationSources(innerOperation, sourcesById);
                foreach (var ordering in pushdown.PreservedOrderings)
                    ValidateValueSources(ordering.Value, sourcesById);
                break;
            case QueryPlanOperation.GroupBy groupBy:
                foreach (var key in groupBy.Keys)
                    ValidateValueSources(key, sourcesById);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan operation '{operation.GetType().Name}'.",
                    nameof(operation));
        }
    }

    private static void ValidatePredicateSources(
        QueryPlanPredicate predicate,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (predicate)
        {
            case QueryPlanPredicate.Fixed:
                break;
            case QueryPlanPredicate.And and:
                foreach (var term in and.Terms)
                    ValidatePredicateSources(term, sourcesById);
                break;
            case QueryPlanPredicate.Or or:
                foreach (var term in or.Terms)
                    ValidatePredicateSources(term, sourcesById);
                break;
            case QueryPlanPredicate.Not not:
                ValidatePredicateSources(not.Predicate, sourcesById);
                break;
            case QueryPlanPredicate.Compare compare:
                ValidateValueSources(compare.Left, sourcesById);
                ValidateValueSources(compare.Right, sourcesById);
                break;
            case QueryPlanPredicate.In inPredicate:
                ValidateValueSources(inPredicate.Item, sourcesById);
                ValidateValueSources(inPredicate.Sequence, sourcesById);
                break;
            case QueryPlanPredicate.Exists exists:
                ValidateSource(exists.ParentSource, sourcesById);
                ValidateSource(exists.ChildSource, sourcesById);
                if (exists.Predicate is not null)
                    ValidatePredicateSources(exists.Predicate, sourcesById);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan predicate '{predicate.GetType().Name}'.",
                    nameof(predicate));
        }
    }

    private static void ValidateProjectionSources(
        QueryPlanProjection projection,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (projection)
        {
            case QueryPlanProjection.Entity entity:
                ValidateSource(entity.Source, sourcesById);
                break;
            case QueryPlanProjection.ScalarMember scalar:
                ValidateSource(scalar.Source, sourcesById);
                ValidateColumn(scalar.Source, scalar.Column);
                break;
            case QueryPlanProjection.Anonymous anonymous:
                ValidateProjectionMemberSources(anonymous.Members, sourcesById);
                ValidateSources(anonymous.Sources, sourcesById);
                break;
            case QueryPlanProjection.ComputedRowLocal computed:
                ValidateSources(computed.Sources, sourcesById);
                break;
            case QueryPlanProjection.JoinedRowLocal joined:
                ValidateProjectionMemberSources(joined.Members, sourcesById);
                ValidateSources(joined.Sources, sourcesById);
                break;
            case QueryPlanProjection.SqlRow sqlRow:
                ValidateProjectionMemberSources(sqlRow.Members, sourcesById);
                break;
            case QueryPlanProjection.TransparentIdentifier transparent:
                ValidateSources(transparent.SourcesByMember.Values, sourcesById);
                break;
            case QueryPlanProjection.GroupedAggregate grouped:
                ValidateSource(grouped.Source, sourcesById);
                ValidateProjectionMemberSources(grouped.Members, sourcesById);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan projection '{projection.GetType().Name}'.",
                    nameof(projection));
        }
    }

    private static void ValidateProjectionMemberSources(
        IReadOnlyList<QueryPlanProjectionMember> members,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        foreach (var member in members)
            ValidateValueSources(member.Value, sourcesById);
    }

    private static void ValidateValueSources(
        QueryPlanValue value,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (value)
        {
            case QueryPlanColumnValue column:
                ValidateSource(column.Source, sourcesById);
                ValidateColumn(column.Source, column.Column);
                break;
            case QueryPlanFunctionValue function:
                foreach (var argument in function.Arguments)
                    ValidateValueSources(argument, sourcesById);
                break;
            case QueryPlanConvertedValue converted:
                ValidateValueSources(converted.Value, sourcesById);
                break;
            case QueryPlanGroupKeyValue groupKey:
                ValidateValueSources(groupKey.Key, sourcesById);
                break;
            case QueryPlanGroupedAggregateValue groupedAggregate when groupedAggregate.Selector is not null:
                ValidateValueSources(groupedAggregate.Selector, sourcesById);
                break;
            case QueryPlanIntrinsicValue:
            case QueryPlanScalarBindingReference:
            case QueryPlanLocalSequenceBindingReference:
            case QueryPlanGroupedAggregateValue:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan value '{value.GetType().Name}'.",
                    nameof(value));
        }
    }

    private static void ValidateSources(
        IEnumerable<QueryPlanSourceSlot> sources,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        foreach (var source in sources)
            ValidateSource(source, sourcesById);
    }

    private static void ValidateSource(
        QueryPlanSourceSlot source,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        if (!sourcesById.TryGetValue(source.Id, out var declared))
        {
            throw new ArgumentException(
                $"Query plan references source slot '{source.Id}', which is not declared by the template.",
                nameof(source));
        }

        if (!ReferenceEquals(declared, source))
        {
            throw new ArgumentException(
                $"Query plan source reference '{source.Id}' does not match the source slot declared by the template.",
                nameof(source));
        }
    }

    private static void ValidateColumn(
        QueryPlanSourceSlot source,
        DataLinq.Metadata.ColumnDefinition column)
    {
        if (!ReferenceEquals(source.Table, column.Table))
        {
            throw new ArgumentException(
                $"Query plan column '{column.DbName}' does not belong to source slot '{source.Id}' table '{source.Table.DbName}'.",
                nameof(column));
        }
    }

    private static void ValidateReference(
        string bindingId,
        QueryPlanBindingKind expectedKind,
        Type expectedType,
        QueryPlanBindingDeclarations declarations)
    {
        if (!declarations.TryGet(bindingId, out var declaration))
        {
            throw new ArgumentException(
                $"Query plan value references undeclared binding '{bindingId}'.",
                nameof(declarations));
        }

        if (declaration.Kind != expectedKind)
        {
            throw new ArgumentException(
                $"Query plan binding reference '{bindingId}' has kind '{expectedKind}', " +
                $"but its declaration has kind '{declaration.Kind}'.",
                nameof(declarations));
        }

        if (declaration.ModelType != expectedType)
        {
            throw new ArgumentException(
                $"Query plan binding reference '{bindingId}' expects model type '{expectedType}', " +
                $"but its declaration uses '{declaration.ModelType}'.",
                nameof(declarations));
        }
    }
}
