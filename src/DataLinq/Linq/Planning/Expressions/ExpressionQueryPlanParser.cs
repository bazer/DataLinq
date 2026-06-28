using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning.Expressions;

internal readonly record struct ExpressionQueryPlanParserOptions(
    ExpressionLocalValueEvaluationOptions LocalValueEvaluation)
{
    public static ExpressionQueryPlanParserOptions Default { get; } = new(
        ExpressionLocalValueEvaluationOptions.Default);

    public static ExpressionQueryPlanParserOptions AotStrict { get; } = new(
        ExpressionLocalValueEvaluationOptions.AotStrict);
}

internal sealed class ExpressionQueryPlanParser
{
    private readonly DatabaseDefinition metadata;
    private readonly ExpressionQueryPlanParserOptions options;
    private readonly QueryPlanBindingFrame bindings = new();
    private readonly List<QueryPlanSourceSlot> sources = [];
    private readonly List<QueryPlanOperation> operations = [];
    private readonly Dictionary<ParameterExpression, QueryPlanSourceSlot> parameterSourceSlots = [];
    private readonly Dictionary<ParameterExpression, QueryPlanProjection> parameterProjections = [];
    private int relationSubqueryCounter;

    private ExpressionQueryPlanParser(DatabaseDefinition metadata, ExpressionQueryPlanParserOptions options)
    {
        this.metadata = metadata;
        this.options = options;
    }

    public static DataLinqQueryPlan Convert<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(query);

        return Convert(database.Provider.Metadata, query.Expression, typeof(TModel));
    }

    public static DataLinqQueryPlan Convert<TDatabase, TResult>(Database<TDatabase> database, Expression<Func<TResult>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(query);

        return Convert(database.Provider.Metadata, query.Body, typeof(TResult));
    }

    internal static DataLinqQueryPlan Convert(DatabaseDefinition metadata, Expression expression, Type resultType)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resultType);

        var parser = new ExpressionQueryPlanParser(metadata, ExpressionQueryPlanParserOptions.Default);
        return parser.Parse(expression, resultType);
    }

    internal static DataLinqQueryPlan Convert(
        DatabaseDefinition metadata,
        Expression expression,
        Type resultType,
        ExpressionQueryPlanParserOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resultType);

        var parser = new ExpressionQueryPlanParser(metadata, options);
        return parser.Parse(expression, resultType);
    }

    private DataLinqQueryPlan Parse(Expression expression, Type resultType)
    {
        var parsed = IsQueryableSequence(expression.Type)
            ? ParseSequence(expression)
            : ParseTerminal(expression, resultType);

        if (parsed.Grouping is not null)
        {
            throw new QueryTranslationException(
                "GroupBy without a grouped aggregate Select projection is not supported by the DataLinq expression parser. " +
                $"Expression: {expression}");
        }

        var projection = parsed.Projection ?? new QueryPlanProjection.Entity(parsed.RootSource);
        var result = parsed.Result ?? QueryPlanResult.Sequence(projection.ResultType);

        return new DataLinqQueryPlan(sources, operations, projection, result, bindings);
    }

    private ParsedQuery ParseSequence(Expression expression)
    {
        expression = UnwrapConvert(expression);

        if (TryParseRootSource(expression, QueryPlanSourceKind.RootTable, out var rootSource))
            return new ParsedQuery(rootSource, rootSource.ElementType);

        if (expression is not MethodCallExpression methodCall || !IsQueryableMethod(methodCall))
            throw new QueryTranslationException($"LINQ expression '{expression}' is not supported by the DataLinq expression parser.");

        return methodCall.Method.Name switch
        {
            nameof(Queryable.Where) => ParseWhere(methodCall),
            nameof(Queryable.OrderBy) => ParseOrderBy(methodCall, QueryPlanOrderingDirection.Ascending),
            nameof(Queryable.OrderByDescending) => ParseOrderBy(methodCall, QueryPlanOrderingDirection.Descending),
            nameof(Queryable.ThenBy) => ParseOrderBy(methodCall, QueryPlanOrderingDirection.Ascending),
            nameof(Queryable.ThenByDescending) => ParseOrderBy(methodCall, QueryPlanOrderingDirection.Descending),
            nameof(Queryable.Skip) => ParsePaging(methodCall, isSkip: true),
            nameof(Queryable.Take) => ParsePaging(methodCall, isSkip: false),
            nameof(Queryable.Select) => ParseSelect(methodCall),
            nameof(Queryable.Join) => ParseJoin(methodCall),
            nameof(Queryable.GroupBy) => ParseGroupBy(methodCall),
            _ => throw new QueryTranslationException($"LINQ operator '{methodCall.Method.Name}' is not supported by the DataLinq expression parser. Expression: {methodCall}")
        };
    }

    private ParsedQuery ParseTerminal(Expression expression, Type resultType)
    {
        expression = UnwrapConvert(expression);
        if (expression is not MethodCallExpression methodCall || !IsQueryableMethod(methodCall))
            throw new QueryTranslationException($"LINQ result expression '{expression}' is not supported by the DataLinq expression parser.");

        return methodCall.Method.Name switch
        {
            nameof(Queryable.Count) => ParseScalar(methodCall, QueryPlanResultKind.Count, resultType),
            nameof(Queryable.Any) => ParseScalar(methodCall, QueryPlanResultKind.Any, resultType),
            nameof(Queryable.Single) => ParseSingle(methodCall, QueryPlanResultKind.Single, resultType),
            nameof(Queryable.SingleOrDefault) => ParseSingle(methodCall, QueryPlanResultKind.SingleOrDefault, resultType),
            nameof(Queryable.First) => ParseSingle(methodCall, QueryPlanResultKind.First, resultType),
            nameof(Queryable.FirstOrDefault) => ParseSingle(methodCall, QueryPlanResultKind.FirstOrDefault, resultType),
            nameof(Queryable.Last) => ParseSingle(methodCall, QueryPlanResultKind.Last, resultType),
            nameof(Queryable.LastOrDefault) => ParseSingle(methodCall, QueryPlanResultKind.LastOrDefault, resultType),
            nameof(Queryable.Sum) => ParseAggregate(methodCall, QueryPlanResultKind.Sum, resultType),
            nameof(Queryable.Min) => ParseAggregate(methodCall, QueryPlanResultKind.Min, resultType),
            nameof(Queryable.Max) => ParseAggregate(methodCall, QueryPlanResultKind.Max, resultType),
            nameof(Queryable.Average) => ParseAggregate(methodCall, QueryPlanResultKind.Average, resultType),
            _ => throw new QueryTranslationException($"LINQ result operator '{methodCall.Method.Name}' is not supported by the DataLinq expression parser. Expression: {methodCall}")
        };
    }

    private ParsedQuery ParseWhere(MethodCallExpression methodCall)
    {
        EnsureArgumentCount(methodCall, 2);
        var parsed = ParseSequence(methodCall.Arguments[0]);
        RejectGroupedOperator(parsed, methodCall.Method.Name);
        RejectUnsupportedPostJoinComposition(parsed, methodCall.Method.Name);
        PushDownPostPagingOperations(methodCall.Method.Name);

        var predicate = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
        if (predicate.Parameters.Count != 1)
            throw new QueryTranslationException($"Where predicate '{predicate}' is not supported.");

        if (CanBindProjectionParameter(parsed))
        {
            WithProjection(predicate.Parameters[0], parsed.Projection!, () =>
                operations.Add(new QueryPlanOperation.Where(ConvertPredicate(predicate.Body))));
            return parsed;
        }

        RejectProjectedOperator(parsed, methodCall.Method.Name);

        WithSource(predicate.Parameters[0], parsed.RootSource, () =>
            operations.Add(new QueryPlanOperation.Where(ConvertPredicate(predicate.Body))));

        return parsed;
    }

    private ParsedQuery ParseOrderBy(MethodCallExpression methodCall, QueryPlanOrderingDirection direction)
    {
        EnsureArgumentCount(methodCall, 2);
        var parsed = ParseSequence(methodCall.Arguments[0]);
        RejectGroupedOperator(parsed, methodCall.Method.Name);
        RejectUnsupportedPostJoinComposition(parsed, methodCall.Method.Name);
        PushDownPostPagingOperations(methodCall.Method.Name);

        var keySelector = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
        if (keySelector.Parameters.Count != 1)
            throw new QueryTranslationException($"Ordering key selector '{keySelector}' is not supported.");

        var ordering = CreateOrdering(parsed, keySelector, direction);

        if (operations.LastOrDefault() is QueryPlanOperation.OrderBy lastOrderBy)
        {
            operations[^1] = new QueryPlanOperation.OrderBy(lastOrderBy.Orderings.Concat([ordering]));
        }
        else
        {
            operations.Add(new QueryPlanOperation.OrderBy([ordering]));
        }

        return parsed;
    }

    private QueryPlanOrdering CreateOrdering(
        ParsedQuery parsed,
        LambdaExpression keySelector,
        QueryPlanOrderingDirection direction)
    {
        if (TryCreateProjectedOrdering(parsed.Projection, keySelector, direction, out var ordering))
            return ordering;

        if (CanBindProjectionParameter(parsed))
        {
            return WithProjection(keySelector.Parameters[0], parsed.Projection!, () =>
                new QueryPlanOrdering(ConvertValue(keySelector.Body), direction));
        }

        return WithSource(keySelector.Parameters[0], parsed.RootSource, () =>
            new QueryPlanOrdering(ConvertValue(keySelector.Body), direction));
    }

    private static bool TryCreateProjectedOrdering(
        QueryPlanProjection? projection,
        LambdaExpression keySelector,
        QueryPlanOrderingDirection direction,
        out QueryPlanOrdering ordering)
    {
        if (projection is QueryPlanProjection.ScalarMember scalarProjection &&
            keySelector.Parameters.Count == 1 &&
            UnwrapConvert(keySelector.Body) == keySelector.Parameters[0])
        {
            ordering = new QueryPlanOrdering(
                new QueryPlanColumnValue(scalarProjection.Source, scalarProjection.Column, scalarProjection.ResultType),
                direction);
            return true;
        }

        ordering = null!;
        return false;
    }

    private ParsedQuery ParsePaging(MethodCallExpression methodCall, bool isSkip)
    {
        EnsureArgumentCount(methodCall, 2);
        var parsed = ParseSequence(methodCall.Arguments[0]);
        RejectUnsupportedPostJoinComposition(parsed, methodCall.Method.Name, allowAfterPaging: true);
        RejectGroupedOperator(parsed, methodCall.Method.Name);

        var count = ConvertValue(methodCall.Arguments[1]);
        operations.Add(isSkip
            ? new QueryPlanOperation.Skip(count)
            : new QueryPlanOperation.Take(count));

        return parsed;
    }

    private ParsedQuery ParseSelect(MethodCallExpression methodCall)
    {
        EnsureArgumentCount(methodCall, 2);
        var parsed = ParseSequence(methodCall.Arguments[0]);
        RejectPostJoinOperator(methodCall.Method.Name);
        var selector = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
        if (selector.Parameters.Count != 1)
            throw new QueryTranslationException($"Select selector '{selector}' is not supported.");

        if (parsed.Grouping is { } grouping)
        {
            var groupedProjection = CreateGroupedAggregateProjection(selector, grouping);
            return parsed with
            {
                ElementType = selector.ReturnType,
                Projection = groupedProjection,
                Grouping = null
            };
        }

        QueryPlanProjection projection = null!;
        WithSource(selector.Parameters[0], parsed.RootSource, () =>
            projection = CreateProjection(selector.Body, selector.ReturnType));

        return parsed with { ElementType = selector.ReturnType, Projection = projection };
    }

    private ParsedQuery ParseGroupBy(MethodCallExpression methodCall)
    {
        EnsureArgumentCount(methodCall, 2);
        var parsed = ParseSequence(methodCall.Arguments[0]);
        RejectPostJoinOperator(methodCall.Method.Name);
        RejectProjectedOperator(parsed, methodCall.Method.Name);
        RejectGroupedOperator(parsed, methodCall.Method.Name);

        if (operations.Any(static operation => operation is not QueryPlanOperation.Where))
        {
            throw new QueryTranslationException(
                "GroupBy is only supported after direct source queries or Where predicates by the DataLinq expression parser. " +
                $"Expression: {methodCall}");
        }

        var keySelector = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
        if (keySelector.Parameters.Count != 1)
            throw new QueryTranslationException($"GroupBy key selector '{keySelector}' is not supported.");

        QueryPlanValue key = null!;
        WithSource(keySelector.Parameters[0], parsed.RootSource, () =>
        {
            var keyBody = UnwrapConvert(keySelector.Body);
            if (!TryGetColumnValue(keyBody, out var column))
            {
                throw new QueryTranslationException(
                    $"GroupBy key selector '{keySelector}' is not supported by the DataLinq expression parser. " +
                    "Only direct mapped member keys are supported.");
            }

            key = column;
        });

        operations.Add(new QueryPlanOperation.GroupBy([key]));
        return parsed with
        {
            ElementType = GetQueryableElementType(methodCall.Type),
            Grouping = new QueryPlanGrouping(parsed.RootSource, key)
        };
    }

    private ParsedQuery ParseJoin(MethodCallExpression methodCall)
    {
        EnsureArgumentCount(methodCall, 5);
        var parsedOuter = ParseSequence(methodCall.Arguments[0]);
        if (operations.Count != 0)
        {
            throw new QueryTranslationException(
                $"Join queries currently support only direct source Join calls. Filtering, ordering, and additional operators over joins are not supported yet. Expression: {methodCall}");
        }

        if (!TryParseRootSource(methodCall.Arguments[1], QueryPlanSourceKind.ExplicitJoin, out var innerSource))
            throw new QueryTranslationException($"Join inner sequence '{methodCall.Arguments[1]}' is not supported. Only direct DataLinq query sources are supported.");

        var outerKeySelector = UnwrapLambda(methodCall.Arguments[2], methodCall.ToString());
        var innerKeySelector = UnwrapLambda(methodCall.Arguments[3], methodCall.ToString());
        var resultSelector = UnwrapLambda(methodCall.Arguments[4], methodCall.ToString());
        if (outerKeySelector.Parameters.Count != 1 || innerKeySelector.Parameters.Count != 1 || resultSelector.Parameters.Count != 2)
            throw new QueryTranslationException($"Join expression '{methodCall}' is not supported. Only direct member keys and a two-parameter result selector are supported.");

        QueryPlanJoin join = null!;
        WithSource(outerKeySelector.Parameters[0], parsedOuter.RootSource, () =>
        WithSource(innerKeySelector.Parameters[0], innerSource, () =>
        {
            var leftColumn = GetJoinKeyColumn(parsedOuter.RootSource, outerKeySelector.Body, "outer");
            var rightColumn = GetJoinKeyColumn(innerSource, innerKeySelector.Body, "inner");
            join = new QueryPlanJoin(QueryPlanJoinKind.Inner, parsedOuter.RootSource, leftColumn, innerSource, rightColumn);
        }));

        operations.Add(new QueryPlanOperation.Join(join));

        QueryPlanProjection projection = null!;
        WithSource(resultSelector.Parameters[0], parsedOuter.RootSource, () =>
        WithSource(resultSelector.Parameters[1], innerSource, () =>
            projection = CreateProjection(resultSelector.Body, resultSelector.ReturnType)));

        return parsedOuter with { ElementType = resultSelector.ReturnType, Projection = projection };
    }

    private ParsedQuery ParseScalar(MethodCallExpression methodCall, QueryPlanResultKind resultKind, Type resultType)
    {
        var parsed = ParseSequence(methodCall.Arguments[0]);
        RejectGroupedOperator(parsed, methodCall.Method.Name);
        RejectGroupedProjectionTerminal(parsed, methodCall.Method.Name);
        var isJoinedProjection = CanBindProjectionParameter(parsed);
        if (isJoinedProjection)
        {
            if (operations.Any(static operation => operation is QueryPlanOperation.Skip or QueryPlanOperation.Take))
            {
                throw new QueryTranslationException(
                    $"Terminal operator '{methodCall.Method.Name}' after joined query paging is not supported yet. " +
                    "Materialize before counting or checking existence over paged joined rows.");
            }
        }
        else
        {
            RejectPostJoinTerminalOperator(methodCall.Method.Name);
            PushDownPostPagingOperations(methodCall.Method.Name);
        }

        if (methodCall.Arguments.Count == 2)
        {
            var predicate = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
            if (predicate.Parameters.Count != 1)
                throw new QueryTranslationException($"{methodCall.Method.Name} predicate '{predicate}' is not supported.");

            if (isJoinedProjection)
            {
                WithProjection(predicate.Parameters[0], parsed.Projection!, () =>
                    operations.Add(new QueryPlanOperation.Where(ConvertPredicate(predicate.Body))));
            }
            else
            {
                WithSource(predicate.Parameters[0], parsed.RootSource, () =>
                    operations.Add(new QueryPlanOperation.Where(ConvertPredicate(predicate.Body))));
            }
        }
        else
        {
            EnsureArgumentCount(methodCall, 1);
        }

        return parsed with { Result = new QueryPlanResult(resultKind, resultType) };
    }

    private ParsedQuery ParseSingle(MethodCallExpression methodCall, QueryPlanResultKind resultKind, Type resultType)
    {
        var parsed = ParseSequence(methodCall.Arguments[0]);
        RejectPostJoinTerminalOperator(methodCall.Method.Name);
        RejectGroupedOperator(parsed, methodCall.Method.Name);
        RejectGroupedProjectionTerminal(parsed, methodCall.Method.Name);
        PushDownPostPagingOperations(methodCall.Method.Name);
        if (methodCall.Arguments.Count == 2)
        {
            var predicate = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
            if (predicate.Parameters.Count != 1)
                throw new QueryTranslationException($"{methodCall.Method.Name} predicate '{predicate}' is not supported.");

            WithSource(predicate.Parameters[0], parsed.RootSource, () =>
                operations.Add(new QueryPlanOperation.Where(ConvertPredicate(predicate.Body))));
        }
        else
        {
            EnsureArgumentCount(methodCall, 1);
        }

        return parsed with { Result = new QueryPlanResult(resultKind, resultType) };
    }

    private ParsedQuery ParseAggregate(MethodCallExpression methodCall, QueryPlanResultKind resultKind, Type resultType)
    {
        var parsed = ParseSequence(methodCall.Arguments[0]);
        RejectPostJoinTerminalOperator(methodCall.Method.Name);
        RejectGroupedOperator(parsed, methodCall.Method.Name);
        RejectGroupedProjectionTerminal(parsed, methodCall.Method.Name);
        PushDownPostPagingOperations(methodCall.Method.Name);
        if (methodCall.Arguments.Count != 2)
            throw new QueryTranslationException($"Aggregate operator '{methodCall.Method.Name}' requires a supported selector. Expression: {methodCall}");

        var selector = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
        if (selector.Parameters.Count != 1)
            throw new QueryTranslationException($"Aggregate selector '{selector}' is not supported.");

        QueryPlanProjection projection = null!;
        QueryPlanValue aggregateSelector = null!;
        WithSource(selector.Parameters[0], parsed.RootSource, () =>
        {
            projection = CreateProjection(selector.Body, resultType);
            aggregateSelector = GetAggregateSelector(selector.Body, methodCall.Method.Name);
        });

        return parsed with
        {
            ElementType = selector.ReturnType,
            Projection = projection,
            Result = new QueryPlanResult(resultKind, resultType, aggregateSelector)
        };
    }

    private QueryPlanProjection CreateProjection(Expression selector, Type resultType)
    {
        selector = UnwrapConvert(selector);
        ValidateProjectionSupported(selector);

        if (TryGetSource(selector, out var source))
            return new QueryPlanProjection.Entity(source);

        if (TryGetColumnValue(selector, out var scalarColumn))
            return new QueryPlanProjection.ScalarMember(scalarColumn.Source, scalarColumn.Column, resultType);

        var referencedSources = GetReferencedSources(selector);
        if (selector is NewExpression newExpression && TryCreateProjectionMembers(newExpression, out var members))
        {
            return referencedSources.Count > 1
                ? new QueryPlanProjection.JoinedRowLocal(resultType, members, referencedSources)
                : new QueryPlanProjection.Anonymous(resultType, members, referencedSources);
        }

        return referencedSources.Count > 1
            ? new QueryPlanProjection.JoinedRowLocal(
                resultType,
                [new QueryPlanProjectionMember("value", new QueryPlanFunctionValue(QueryPlanFunctionKind.ClientExpression, [], resultType))],
                referencedSources)
            : new QueryPlanProjection.ComputedRowLocal(resultType, GetExpressionShape(selector), referencedSources);
    }

    private QueryPlanProjection CreateGroupedAggregateProjection(LambdaExpression selector, QueryPlanGrouping grouping)
    {
        var body = UnwrapConvert(selector.Body);
        if (body is not NewExpression newExpression)
        {
            throw new QueryTranslationException(
                $"Grouped aggregate Select projection '{selector}' is not supported by the DataLinq expression parser. " +
                "Use a new-object projection containing only group.Key and supported grouped aggregate calls.");
        }

        if (newExpression.Constructor is null)
        {
            throw new QueryTranslationException(
                $"Grouped aggregate Select projection '{selector}' is not supported by the DataLinq expression parser. " +
                "The projection must use a constructor-backed new-object expression.");
        }

        var names = newExpression.Members?.Select(static member => member.Name).ToArray();
        if (names is null || names.Length != newExpression.Arguments.Count)
        {
            throw new QueryTranslationException(
                $"Grouped aggregate Select projection '{selector}' is not supported by the DataLinq expression parser. " +
                "Projection members must have stable names.");
        }

        var members = new List<QueryPlanProjectionMember>(newExpression.Arguments.Count);
        for (var index = 0; index < newExpression.Arguments.Count; index++)
        {
            var value = ConvertGroupedProjectionValue(
                UnwrapConvert(newExpression.Arguments[index]),
                selector.Parameters[0],
                grouping);
            members.Add(new QueryPlanProjectionMember(names[index], value));
        }

        return new QueryPlanProjection.GroupedAggregate(selector.ReturnType, members, grouping.Source, newExpression.Constructor);
    }

    private QueryPlanValue ConvertGroupedProjectionValue(
        Expression expression,
        ParameterExpression groupParameter,
        QueryPlanGrouping grouping)
    {
        if (expression is MemberExpression { Member.Name: "Key" } memberExpression &&
            UnwrapConvert(memberExpression.Expression!) == groupParameter)
        {
            return new QueryPlanGroupKeyValue(grouping.Key, memberExpression.Type);
        }

        if (expression is MethodCallExpression methodCall &&
            IsEnumerableMethod(methodCall, nameof(Enumerable.Count)) &&
            methodCall.Arguments.Count == 1 &&
            UnwrapConvert(methodCall.Arguments[0]) == groupParameter)
        {
            return new QueryPlanGroupedAggregateValue(QueryPlanGroupedAggregateKind.Count, methodCall.Type);
        }

        throw new QueryTranslationException(
            $"Grouped aggregate projection member '{expression}' is not supported by the DataLinq expression parser. " +
            "Only group.Key and group.Count() are supported.");
    }

    private bool TryCreateProjectionMembers(NewExpression newExpression, out IReadOnlyList<QueryPlanProjectionMember> members)
    {
        var names = newExpression.Members?.Select(static member => member.Name).ToArray();
        if (names is null || names.Length != newExpression.Arguments.Count)
        {
            members = [];
            return false;
        }

        var projectionMembers = new List<QueryPlanProjectionMember>(newExpression.Arguments.Count);
        for (var index = 0; index < newExpression.Arguments.Count; index++)
        {
            if (!TryConvertValue(newExpression.Arguments[index], out var value))
            {
                members = [];
                return false;
            }

            projectionMembers.Add(new QueryPlanProjectionMember(names[index], value));
        }

        members = projectionMembers;
        return true;
    }

    private QueryPlanPredicate ConvertPredicate(Expression expression, bool isNegated = false)
    {
        expression = UnwrapConvert(expression);

        if (expression is UnaryExpression { NodeType: ExpressionType.Not } not)
            return ConvertPredicate(not.Operand, !isNegated);

        if (expression is ConstantExpression { Type: { } type, Value: bool value } && type == typeof(bool))
            return new QueryPlanPredicate.Fixed(isNegated ? !value : value);

        QueryPlanPredicate predicate = expression switch
        {
            BinaryExpression { NodeType: ExpressionType.AndAlso } binary => new QueryPlanPredicate.And([
                ConvertPredicate(binary.Left),
                ConvertPredicate(binary.Right)
            ]),
            BinaryExpression { NodeType: ExpressionType.OrElse } binary => new QueryPlanPredicate.Or([
                ConvertPredicate(binary.Left),
                ConvertPredicate(binary.Right)
            ]),
            BinaryExpression binary when TryConvertRelationCountComparison(binary, isNegated, out var relationCountPredicate) => relationCountPredicate,
            BinaryExpression binary when IsComparison(binary.NodeType) => ConvertComparison(binary),
            MemberExpression member when TryConvertHasValuePredicate(member, out var hasValuePredicate) => hasValuePredicate,
            MemberExpression member when TryGetColumnValue(member, out var boolColumn) && GetNonNullableType(member.Type) == typeof(bool) => new QueryPlanPredicate.Compare(
                boolColumn,
                QueryPlanComparisonOperator.Equal,
                new QueryPlanConstantValue(true, typeof(bool))),
            MethodCallExpression methodCall when TryConvertMethodPredicate(methodCall, isNegated, out var methodPredicate) => methodPredicate,
            MethodCallExpression methodCall => throw new QueryTranslationException($"Method '{methodCall.Method.Name}' is not supported in DataLinq expression predicate translation. Expression: {methodCall}"),
            _ when !ContainsQueryReference(expression) => new QueryPlanPredicate.Fixed(System.Convert.ToBoolean(EvaluateScalar(expression), System.Globalization.CultureInfo.InvariantCulture) ^ isNegated),
            _ => throw new QueryTranslationException($"Predicate expression '{expression}' is not supported by the DataLinq expression parser.")
        };

        if (isNegated &&
            predicate is not QueryPlanPredicate.In &&
            predicate is not QueryPlanPredicate.Exists &&
            predicate is not QueryPlanPredicate.Fixed)
        {
            predicate = new QueryPlanPredicate.Not(predicate);
        }

        return predicate;
    }

    private QueryPlanPredicate ConvertComparison(BinaryExpression binary)
    {
        if (!ContainsQueryReference(binary.Left) && !ContainsQueryReference(binary.Right))
        {
            var result = EvaluateConstantBinary(binary.NodeType, EvaluateScalar(binary.Left), EvaluateScalar(binary.Right));
            return new QueryPlanPredicate.Fixed(result);
        }

        var left = ConvertValue(binary.Left);
        var right = ConvertValue(binary.Right);
        var comparisonOperator = GetComparisonOperator(binary.NodeType);
        var nullSemantics = QueryPlanNullSemanticsResolver.GetComparisonNullSemantics(comparisonOperator, left, right, bindings.Bindings);

        return new QueryPlanPredicate.Compare(left, comparisonOperator, right, nullSemantics);
    }

    private bool TryConvertMethodPredicate(MethodCallExpression methodCall, bool isNegated, out QueryPlanPredicate predicate)
    {
        if (TryConvertRelationAnyMethodCall(methodCall, isNegated, out predicate))
            return true;

        if (TryConvertLocalContains(methodCall, isNegated, out predicate))
            return true;

        if (TryConvertLocalAny(methodCall, isNegated, out predicate))
            return true;

        if (TryConvertStringPredicate(methodCall, out var function))
        {
            predicate = new QueryPlanPredicate.Compare(
                function,
                QueryPlanComparisonOperator.Equal,
                new QueryPlanConstantValue(true, typeof(bool)));
            return true;
        }

        predicate = null!;
        return false;
    }

    private bool TryConvertHasValuePredicate(MemberExpression member, out QueryPlanPredicate predicate)
    {
        if (member.Member.Name == nameof(Nullable<int>.HasValue) &&
            member.Expression is not null &&
            Nullable.GetUnderlyingType(member.Expression.Type) is not null &&
            TryGetColumnValue(member.Expression, out var column))
        {
            predicate = new QueryPlanPredicate.Compare(
                column,
                QueryPlanComparisonOperator.NotEqual,
                new QueryPlanConstantValue(null, member.Expression.Type));
            return true;
        }

        predicate = null!;
        return false;
    }

    private bool TryConvertLocalContains(MethodCallExpression methodCall, bool isNegated, out QueryPlanPredicate predicate)
    {
        predicate = null!;
        if (IsEnumerableMethod(methodCall, nameof(Enumerable.Contains)) && methodCall.Arguments.Count == 2)
        {
            var values = EvaluateLocalSequence(methodCall.Arguments[0]);
            return CreateLocalMembershipPredicate(values, methodCall.Arguments[1], isNegated, out predicate);
        }

        if (methodCall.Method.Name != nameof(Enumerable.Contains))
            return false;

        Expression? sequenceExpression = null;
        Expression? itemExpression = null;
        if (methodCall.Object is not null && methodCall.Arguments.Count == 1)
        {
            sequenceExpression = methodCall.Object;
            itemExpression = methodCall.Arguments[0];
        }
        else if (methodCall.Object is null && methodCall.Arguments.Count == 2)
        {
            sequenceExpression = methodCall.Arguments[0];
            itemExpression = methodCall.Arguments[1];
        }

        if (sequenceExpression is not null &&
            itemExpression is not null &&
            GetNonNullableType(sequenceExpression.Type) != typeof(string) &&
            TryEvaluateLocalSequence(sequenceExpression, out var instanceValues))
        {
            return CreateLocalMembershipPredicate(instanceValues, itemExpression, isNegated, out predicate);
        }

        return false;
    }

    private bool CreateLocalMembershipPredicate(object?[] values, Expression itemExpression, bool isNegated, out QueryPlanPredicate predicate)
    {
        itemExpression = UnwrapQueryColumnAccess(itemExpression);

        if (values.Length == 0)
        {
            predicate = new QueryPlanPredicate.Fixed(isNegated);
            return true;
        }

        if (TryGetColumnValue(itemExpression, out var item))
        {
            var sequence = bindings.CaptureLocalSequence(values, item.ClrType);
            predicate = new QueryPlanPredicate.In(item, sequence, isNegated);
            return true;
        }

        if (!ContainsQueryReference(itemExpression))
        {
            var itemValue = EvaluateScalar(itemExpression);
            var found = values.Any(value => object.Equals(value, itemValue));
            predicate = new QueryPlanPredicate.Fixed(isNegated ? !found : found);
            return true;
        }

        throw new QueryTranslationException($"Contains item expression '{itemExpression}' is not supported by the DataLinq expression parser. Expected member access on the query source or a local constant.");
    }

    private bool TryConvertLocalAny(MethodCallExpression methodCall, bool isNegated, out QueryPlanPredicate predicate)
    {
        predicate = null!;
        if (!IsEnumerableMethod(methodCall, nameof(Enumerable.Any)) || methodCall.Arguments.Count is not (1 or 2))
            return false;

        if (TryGetRelationProperty(methodCall.Arguments[0], out _))
            return false;

        var sourceValues = EvaluateLocalSequence(methodCall.Arguments[0]);
        if (methodCall.Arguments.Count == 1)
        {
            predicate = new QueryPlanPredicate.Fixed(isNegated ? sourceValues.Length == 0 : sourceValues.Length > 0);
            return true;
        }

        if (sourceValues.Length == 0)
        {
            predicate = new QueryPlanPredicate.Fixed(isNegated);
            return true;
        }

        var lambda = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
        if (lambda.Parameters.Count != 1 ||
            lambda.Body is not BinaryExpression { NodeType: ExpressionType.Equal } binary ||
            !TryCreateLocalAnyMembership(binary, lambda.Parameters[0], sourceValues, isNegated, out predicate))
        {
            throw new QueryTranslationException($"Any(predicate) over a non-empty local sequence only supports equality membership against a query column. Predicate: {lambda.Body}");
        }

        return true;
    }

    private bool TryCreateLocalAnyMembership(
        BinaryExpression binary,
        ParameterExpression localParameter,
        object?[] sourceValues,
        bool isNegated,
        out QueryPlanPredicate predicate)
    {
        return TryCreateLocalAnyMembershipSide(binary.Left, binary.Right, localParameter, sourceValues, isNegated, out predicate) ||
               TryCreateLocalAnyMembershipSide(binary.Right, binary.Left, localParameter, sourceValues, isNegated, out predicate);
    }

    private bool TryCreateLocalAnyMembershipSide(
        Expression outerCandidate,
        Expression localCandidate,
        ParameterExpression localParameter,
        object?[] sourceValues,
        bool isNegated,
        out QueryPlanPredicate predicate)
    {
        predicate = null!;
        outerCandidate = UnwrapQueryColumnAccess(outerCandidate);
        localCandidate = UnwrapQueryColumnAccess(localCandidate);

        if (!TryGetColumnValue(outerCandidate, out var outerColumn) ||
            !TryProjectLocalValues(localParameter, localCandidate, sourceValues, out var values))
        {
            return false;
        }

        predicate = new QueryPlanPredicate.In(
            outerColumn,
            bindings.CaptureLocalSequence(values, outerColumn.ClrType),
            isNegated);
        return true;
    }

    private bool TryProjectLocalValues(ParameterExpression parameter, Expression selector, object?[] sourceValues, out object?[] values)
    {
        values = [];
        selector = UnwrapQueryColumnAccess(selector);

        if (selector == parameter)
        {
            values = sourceValues;
            return true;
        }

        if (!IsLocalParameterExpression(selector, parameter))
            return false;

        try
        {
            values = sourceValues
                .Select(value => ExpressionLocalValueEvaluator.Evaluate(selector, parameter, value, options.LocalValueEvaluation))
                .ToArray();
            return true;
        }
        catch
        {
            values = [];
            return false;
        }
    }

    private static bool IsLocalParameterExpression(Expression expression, ParameterExpression parameter)
    {
        expression = UnwrapQueryColumnAccess(expression);

        return expression switch
        {
            ParameterExpression candidate => candidate == parameter,
            MemberExpression { Expression: not null } member => IsLocalParameterExpression(member.Expression, parameter),
            _ => false
        };
    }

    private bool TryConvertRelationAnyMethodCall(MethodCallExpression methodCall, bool isNegated, out QueryPlanPredicate predicate)
    {
        predicate = null!;
        if (!IsEnumerableMethod(methodCall, nameof(Enumerable.Any)) || methodCall.Arguments.Count is not (1 or 2))
            return false;

        if (!TryGetRelationProperty(methodCall.Arguments[0], out var relationProperty, out var parentSource))
            return false;

        var childSource = CreateRelationChildSource(relationProperty);
        QueryPlanPredicate? childPredicate = null;
        if (methodCall.Arguments.Count == 2)
        {
            var lambda = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
            if (lambda.Parameters.Count != 1)
                throw new QueryTranslationException($"Relation predicate lambda '{methodCall}' is not supported.");

            childPredicate = ConvertRelationPredicate(relationProperty, childSource, lambda.Parameters[0], lambda.Body);
        }

        predicate = new QueryPlanPredicate.Exists(relationProperty, parentSource, childSource, childPredicate, isNegated);
        return true;
    }

    private bool TryConvertRelationCountComparison(BinaryExpression binary, bool isNegated, out QueryPlanPredicate predicate)
    {
        if (TryGetRelationCount(binary.Left, out var relationProperty, out var parentSource, out var childPredicateFactory) &&
            TryGetConstantInt(binary.Right, out var constant))
        {
            return CreateRelationCountPredicate(relationProperty, parentSource, childPredicateFactory, binary.NodeType, constant, isNegated, out predicate);
        }

        if (TryGetRelationCount(binary.Right, out relationProperty, out parentSource, out childPredicateFactory) &&
            TryGetConstantInt(binary.Left, out constant))
        {
            return CreateRelationCountPredicate(relationProperty, parentSource, childPredicateFactory, ReverseExpressionType(binary.NodeType), constant, isNegated, out predicate);
        }

        predicate = null!;
        return false;
    }

    private bool TryGetRelationCount(
        Expression expression,
        out RelationProperty relationProperty,
        out QueryPlanSourceSlot parentSource,
        out Func<QueryPlanSourceSlot, QueryPlanPredicate?> childPredicateFactory)
    {
        expression = UnwrapConvert(expression);

        if (expression is MemberExpression { Member.Name: "Count", Expression: not null } member &&
            TryGetRelationProperty(member.Expression, out relationProperty, out parentSource))
        {
            childPredicateFactory = _ => null;
            return true;
        }

        if (expression is MethodCallExpression methodCall &&
            IsEnumerableMethod(methodCall, nameof(Enumerable.Count)) &&
            methodCall.Arguments.Count is 1 or 2 &&
            TryGetRelationProperty(methodCall.Arguments[0], out relationProperty, out parentSource))
        {
            var relation = relationProperty;
            childPredicateFactory = childSource =>
            {
                if (methodCall.Arguments.Count == 1)
                    return null;

                var lambda = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
                if (lambda.Parameters.Count != 1)
                    throw new QueryTranslationException($"Relation predicate lambda '{methodCall}' is not supported.");

                return ConvertRelationPredicate(relation, childSource, lambda.Parameters[0], lambda.Body);
            };
            return true;
        }

        relationProperty = null!;
        parentSource = null!;
        childPredicateFactory = null!;
        return false;
    }

    private bool CreateRelationCountPredicate(
        RelationProperty relationProperty,
        QueryPlanSourceSlot parentSource,
        Func<QueryPlanSourceSlot, QueryPlanPredicate?> childPredicateFactory,
        ExpressionType comparisonType,
        int constant,
        bool isNegated,
        out QueryPlanPredicate predicate)
    {
        if (!TryGetCountExistsSemantics(comparisonType, constant, out var shouldExist))
        {
            throw new QueryTranslationException(
                $"Relation Count() comparison '{comparisonType} {constant}' is not supported. " +
                "Use Count() > 0, Count() >= 1, Count() != 0, Count() == 0, Count() <= 0, or Count() < 1.");
        }

        if (isNegated)
            shouldExist = !shouldExist;

        var childSource = CreateRelationChildSource(relationProperty);
        predicate = new QueryPlanPredicate.Exists(
            relationProperty,
            parentSource,
            childSource,
            childPredicateFactory(childSource),
            IsNegated: !shouldExist);
        return true;
    }

    private QueryPlanPredicate ConvertRelationPredicate(RelationProperty relationProperty, QueryPlanSourceSlot childSource, ParameterExpression childParameter, Expression predicate)
    {
        return WithSource(childParameter, childSource, () => ConvertRelationPredicate(relationProperty, childSource, predicate));
    }

    private QueryPlanPredicate ConvertRelationPredicate(RelationProperty relationProperty, QueryPlanSourceSlot childSource, Expression predicate)
    {
        predicate = UnwrapConvert(predicate);
        if (predicate is BinaryExpression { NodeType: ExpressionType.AndAlso } and)
        {
            return new QueryPlanPredicate.And([
                ConvertRelationPredicate(relationProperty, childSource, and.Left),
                ConvertRelationPredicate(relationProperty, childSource, and.Right)
            ]);
        }

        if (predicate is BinaryExpression { NodeType: ExpressionType.OrElse } or)
        {
            return new QueryPlanPredicate.Or([
                ConvertRelationPredicate(relationProperty, childSource, or.Left),
                ConvertRelationPredicate(relationProperty, childSource, or.Right)
            ]);
        }

        if (predicate is not BinaryExpression comparison || !IsComparison(comparison.NodeType))
            throw new QueryTranslationException($"Relation predicate '{predicate}' is not supported. Only simple comparison predicates are supported.");

        if (TryGetColumnValue(comparison.Left, out var leftColumn) && leftColumn.Source == childSource && !ContainsQueryReference(comparison.Right))
        {
            return new QueryPlanPredicate.Compare(
                leftColumn,
                GetComparisonOperator(comparison.NodeType),
                ConvertValue(comparison.Right),
                GetRelationNullSemantics(leftColumn, comparison.NodeType, comparison.Right));
        }

        if (TryGetColumnValue(comparison.Right, out var rightColumn) && rightColumn.Source == childSource && !ContainsQueryReference(comparison.Left))
        {
            return new QueryPlanPredicate.Compare(
                rightColumn,
                ReverseComparisonOperator(GetComparisonOperator(comparison.NodeType)),
                ConvertValue(comparison.Left),
                GetRelationNullSemantics(rightColumn, comparison.NodeType, comparison.Left));
        }

        throw new QueryTranslationException(
            $"Relation predicate '{predicate}' is not supported. " +
            "Expected a direct related-row member compared with a local value.");
    }

    private QueryPlanNullSemantics GetRelationNullSemantics(QueryPlanColumnValue column, ExpressionType expressionType, Expression valueExpression)
    {
        if (GetComparisonOperator(expressionType) != QueryPlanComparisonOperator.NotEqual ||
            !column.Column.ValueProperty.CsNullable ||
            EvaluateScalar(valueExpression) is null)
        {
            return QueryPlanNullSemantics.Default;
        }

        return QueryPlanNullSemantics.CSharpNullableNotEqualIncludesNull;
    }

    private QueryPlanSourceSlot CreateRelationChildSource(RelationProperty relationProperty)
    {
        var relationPart = relationProperty.RelationPart;
        if (relationPart.Type != RelationPartType.CandidateKey)
        {
            throw new QueryTranslationException(
                $"Relation property '{relationProperty.PropertyName}' is not supported in relation predicate translation. " +
                "Only collection relations from the candidate-key side are supported.");
        }

        var childTable = relationPart.GetOtherSide().ColumnIndex.Table;
        var childType = childTable.Model.CsType.Type!;
        return RegisterSource(
            childType,
            QueryPlanSourceKind.RelationSubquery,
            alias: $"r{relationSubqueryCounter++}",
            table: childTable);
    }

    private bool TryGetRelationProperty(Expression expression, out RelationProperty relationProperty)
        => TryGetRelationProperty(expression, out relationProperty, out _);

    private bool TryGetRelationProperty(Expression expression, out RelationProperty relationProperty, out QueryPlanSourceSlot parentSource)
    {
        expression = UnwrapConvert(expression);

        if (expression is MemberExpression memberExpression &&
            TryGetSource(memberExpression.Expression, out parentSource) &&
            parentSource.Table.Model.RelationProperties.TryGetValue(memberExpression.Member.Name, out relationProperty!))
        {
            return true;
        }

        relationProperty = null!;
        parentSource = null!;
        return false;
    }

    private QueryPlanValue GetAggregateSelector(Expression selector, string operatorName)
    {
        selector = UnwrapConvert(selector);
        if (selector is MemberExpression memberExpression &&
            memberExpression.Member.Name == "Value" &&
            memberExpression.Expression is not null &&
            Nullable.GetUnderlyingType(memberExpression.Expression.Type) is not null)
        {
            selector = memberExpression.Expression;
        }

        if (TryConvertValue(selector, out var value))
            return value;

        throw new QueryTranslationException(
            $"Aggregate selector '{selector}' is not supported for '{operatorName}' by the DataLinq expression parser. " +
            "Only direct numeric members, nullable Value members, and supported scalar functions are supported.");
    }

    private QueryPlanValue ConvertValue(Expression expression)
    {
        if (TryConvertValue(expression, out var value))
            return value;

        throw new QueryTranslationException($"Value expression '{expression}' is not supported by the DataLinq expression parser.");
    }

    private bool TryConvertValue(Expression expression, out QueryPlanValue value)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary &&
            !ContainsQueryReference(unary.Operand))
        {
            var scalar = ExpressionLocalValueEvaluator.Evaluate(unary.Operand, null, null, options.LocalValueEvaluation);
            value = scalar is null
                ? new QueryPlanConstantValue(null, expression.Type)
                : bindings.CaptureScalar(scalar, expression.Type);
            return true;
        }

        expression = UnwrapConvert(expression);

        if (TryGetColumnValue(expression, out var column))
        {
            value = column;
            return true;
        }

        if (TryGetProjectedValue(expression, out var projectedValue))
        {
            value = projectedValue;
            return true;
        }

        if (!ContainsQueryReference(expression))
        {
            if (expression is ConstantExpression { Value: null })
            {
                value = new QueryPlanConstantValue(null, expression.Type);
                return true;
            }

            value = bindings.CaptureScalar(EvaluateScalar(expression), expression.Type);
            return true;
        }

        if (TryConvertFunctionValue(expression, out value))
            return true;

        value = null!;
        return false;
    }

    private bool TryGetColumnValue(Expression expression, out QueryPlanColumnValue value)
    {
        expression = UnwrapQueryColumnAccess(UnwrapConvert(expression));

        if (expression is MemberExpression memberExpression &&
            TryGetSource(memberExpression.Expression, out var source) &&
            source.Table.TryGetColumnByPropertyName(memberExpression.Member.Name, out var column))
        {
            value = new QueryPlanColumnValue(source, column);
            return true;
        }

        value = null!;
        return false;
    }

    private bool TryGetProjectedValue(Expression expression, out QueryPlanValue value)
    {
        expression = UnwrapQueryColumnAccess(UnwrapConvert(expression));

        if (expression is MemberExpression { Expression: ParameterExpression parameter } memberExpression &&
            parameterProjections.TryGetValue(parameter, out var projection) &&
            TryGetProjectionMembers(projection, out var members))
        {
            var matches = members
                .Where(member => member.Name == memberExpression.Member.Name)
                .Take(2)
                .ToArray();

            if (matches.Length == 1)
            {
                var projectedValue = matches[0].Value;
                if (projectedValue is QueryPlanFunctionValue { Function: QueryPlanFunctionKind.ClientExpression })
                {
                    throw new QueryTranslationException(
                        $"Joined projection member '{memberExpression.Member.Name}' is row-local and cannot be translated to SQL. " +
                        "Materialize before filtering or ordering over this member.");
                }

                value = projectedValue;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private bool TryConvertFunctionValue(Expression expression, out QueryPlanValue value)
    {
        expression = UnwrapConvert(expression);

        if (expression is MethodCallExpression methodCall)
        {
            if (TryConvertStringPredicate(methodCall, out var stringPredicate))
            {
                value = stringPredicate;
                return true;
            }

            if (methodCall.Object is not null &&
                TryGetStringMethodFunction(methodCall.Method.Name, out var functionKind))
            {
                var arguments = new List<QueryPlanValue> { ConvertValue(methodCall.Object) };
                arguments.AddRange(methodCall.Arguments.Select(ConvertValue));
                value = new QueryPlanFunctionValue(functionKind, arguments, methodCall.Type);
                return true;
            }
        }

        if (expression is MemberExpression memberExpression)
        {
            if (memberExpression.Member.Name == nameof(string.Length) &&
                memberExpression.Expression is not null &&
                GetNonNullableType(memberExpression.Expression.Type) == typeof(string))
            {
                value = new QueryPlanFunctionValue(QueryPlanFunctionKind.StringLength, [ConvertValue(memberExpression.Expression)], memberExpression.Type);
                return true;
            }

            if (TryGetDateTimePart(memberExpression, out var datePartFunction))
            {
                value = new QueryPlanFunctionValue(datePartFunction, [ConvertValue(memberExpression.Expression!)], memberExpression.Type);
                return true;
            }
        }

        value = null!;
        return false;
    }

    private bool TryConvertStringPredicate(MethodCallExpression methodCall, out QueryPlanFunctionValue value)
    {
        if (methodCall.Method.IsStatic &&
            methodCall.Method.DeclaringType == typeof(string) &&
            methodCall.Arguments.Count == 1 &&
            (methodCall.Method.Name == nameof(string.IsNullOrEmpty) || methodCall.Method.Name == nameof(string.IsNullOrWhiteSpace)))
        {
            value = new QueryPlanFunctionValue(
                methodCall.Method.Name == nameof(string.IsNullOrEmpty)
                    ? QueryPlanFunctionKind.StringIsNullOrEmpty
                    : QueryPlanFunctionKind.StringIsNullOrWhiteSpace,
                [ConvertValue(methodCall.Arguments[0])],
                typeof(bool));
            return true;
        }

        if (methodCall.Object is not null &&
            methodCall.Arguments.Count == 1 &&
            GetNonNullableType(methodCall.Object.Type) == typeof(string) &&
            methodCall.Method.Name is nameof(string.StartsWith) or nameof(string.EndsWith) or nameof(string.Contains))
        {
            var functionKind = methodCall.Method.Name switch
            {
                nameof(string.StartsWith) => QueryPlanFunctionKind.StringStartsWith,
                nameof(string.EndsWith) => QueryPlanFunctionKind.StringEndsWith,
                _ => QueryPlanFunctionKind.StringContains
            };

            value = new QueryPlanFunctionValue(
                functionKind,
                [ConvertValue(methodCall.Object), ConvertValue(methodCall.Arguments[0])],
                typeof(bool));
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryGetStringMethodFunction(string methodName, out QueryPlanFunctionKind functionKind)
    {
        switch (methodName)
        {
            case nameof(string.Trim):
                functionKind = QueryPlanFunctionKind.StringTrim;
                return true;
            case nameof(string.ToUpper):
                functionKind = QueryPlanFunctionKind.StringToUpper;
                return true;
            case nameof(string.ToLower):
                functionKind = QueryPlanFunctionKind.StringToLower;
                return true;
            case nameof(string.Substring):
                functionKind = QueryPlanFunctionKind.StringSubstring;
                return true;
            default:
                functionKind = default;
                return false;
        }
    }

    private static bool TryGetDateTimePart(MemberExpression memberExpression, out QueryPlanFunctionKind functionKind)
    {
        var sourceType = memberExpression.Expression is null
            ? null
            : GetNonNullableType(memberExpression.Expression.Type);

        if (sourceType == typeof(DateOnly) || sourceType == typeof(DateTime))
        {
            switch (memberExpression.Member.Name)
            {
                case nameof(DateTime.Year):
                    functionKind = QueryPlanFunctionKind.DatePartYear;
                    return true;
                case nameof(DateTime.Month):
                    functionKind = QueryPlanFunctionKind.DatePartMonth;
                    return true;
                case nameof(DateTime.Day):
                    functionKind = QueryPlanFunctionKind.DatePartDay;
                    return true;
                case nameof(DateTime.DayOfYear):
                    functionKind = QueryPlanFunctionKind.DatePartDayOfYear;
                    return true;
                case nameof(DateTime.DayOfWeek):
                    functionKind = QueryPlanFunctionKind.DatePartDayOfWeek;
                    return true;
            }
        }

        if (sourceType == typeof(TimeOnly) || sourceType == typeof(DateTime))
        {
            switch (memberExpression.Member.Name)
            {
                case nameof(DateTime.Hour):
                    functionKind = QueryPlanFunctionKind.TimePartHour;
                    return true;
                case nameof(DateTime.Minute):
                    functionKind = QueryPlanFunctionKind.TimePartMinute;
                    return true;
                case nameof(DateTime.Second):
                    functionKind = QueryPlanFunctionKind.TimePartSecond;
                    return true;
                case nameof(DateTime.Millisecond):
                    functionKind = QueryPlanFunctionKind.TimePartMillisecond;
                    return true;
            }
        }

        functionKind = default;
        return false;
    }

    private bool TryGetSource(Expression? expression, out QueryPlanSourceSlot source)
    {
        expression = UnwrapConvert(expression);

        if (expression is ParameterExpression parameter &&
            parameterSourceSlots.TryGetValue(parameter, out source!))
        {
            return true;
        }

        source = null!;
        return false;
    }

    private bool TryParseRootSource(Expression expression, QueryPlanSourceKind kind, out QueryPlanSourceSlot source)
    {
        expression = UnwrapConvert(expression);
        if (TryGetRootQueryable(expression, out var queryable))
        {
            source = RegisterSource(queryable.ElementType, kind);
            return true;
        }

        if (TryParseDatabaseQueryPropertyRoot(expression, kind, out source))
            return true;

        source = null!;
        return false;
    }

    private bool TryGetRootQueryable(Expression expression, out IQueryable queryable)
    {
        if (expression is ConstantExpression { Value: IQueryable constantQueryable } &&
            IsRootQueryable(constantQueryable))
        {
            queryable = constantQueryable;
            return true;
        }

        if (TryEvaluateCapturedValue(expression, out var capturedValue) &&
            capturedValue is IQueryable capturedQueryable &&
            IsRootQueryable(capturedQueryable))
        {
            queryable = capturedQueryable;
            return true;
        }

        queryable = null!;
        return false;
    }

    private bool TryParseDatabaseQueryPropertyRoot(
        Expression expression,
        QueryPlanSourceKind kind,
        out QueryPlanSourceSlot source)
    {
        if (expression is MemberExpression memberExpression &&
            TryGetDbReadElementType(memberExpression.Type, out var elementType) &&
            metadata.TryGetTableModel(elementType, out var tableModel) &&
            string.Equals(tableModel.CsPropertyName, memberExpression.Member.Name, StringComparison.Ordinal) &&
            IsDatabaseQueryCall(memberExpression.Expression))
        {
            source = RegisterSource(elementType, kind, table: tableModel.Table);
            return true;
        }

        source = null!;
        return false;
    }

    private QueryPlanSourceSlot RegisterSource(
        Type itemType,
        QueryPlanSourceKind kind,
        string? alias = null,
        TableDefinition? table = null)
    {
        table ??= GetTableForModelType(itemType);
        var source = new QueryPlanSourceSlot(
            $"s{sources.Count}",
            alias ?? $"t{sources.Count}",
            table,
            itemType,
            kind,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);

        sources.Add(source);
        return source;
    }

    private TableDefinition GetTableForModelType(Type modelType)
    {
        return metadata.TryGetTableModel(modelType, out var tableModel)
            ? tableModel.Table
            : throw new QueryTranslationException($"Query source type '{modelType}' is not a mapped DataLinq table model.");
    }

    private ColumnDefinition GetJoinKeyColumn(QueryPlanSourceSlot source, Expression keySelector, string side)
    {
        keySelector = UnwrapQueryColumnAccess(UnwrapConvert(keySelector));
        if (keySelector is not MemberExpression memberExpression ||
            !TryGetSource(memberExpression.Expression, out var memberSource) ||
            memberSource != source)
        {
            throw new QueryTranslationException($"The {side} Join key selector '{keySelector}' is not supported. Only direct member keys and nullable Value member keys are supported.");
        }

        return source.Table.TryGetColumnByPropertyName(memberExpression.Member.Name, out var column)
            ? column
            : throw new QueryTranslationException($"The {side} Join key member '{memberExpression.Member.Name}' is not mapped on table '{source.Table.DbName}'.");
    }

    private IReadOnlyList<QueryPlanSourceSlot> GetReferencedSources(Expression expression)
    {
        var visitor = new QuerySourceCollector(this);
        visitor.Visit(expression);
        return visitor.Sources;
    }

    private void ValidateProjectionSupported(Expression selector)
    {
        var visitor = new ProjectionUnsupportedShapeVisitor(this, selector);
        visitor.Visit(selector);
    }

    private bool TryGetRelationProjectionProperty(MemberExpression memberExpression, out RelationProperty relationProperty)
    {
        if (TryGetProjectionSourceType(memberExpression.Expression, out var sourceType) &&
            metadata.TryGetTableModel(sourceType, out var tableModel) &&
            tableModel.Model.RelationProperties.TryGetValue(memberExpression.Member.Name, out relationProperty!))
        {
            return true;
        }

        relationProperty = null!;
        return false;
    }

    private bool TryGetProjectionSourceType(Expression? expression, out Type sourceType)
    {
        expression = UnwrapConvert(expression);

        switch (expression)
        {
            case ParameterExpression parameterExpression:
                sourceType = parameterSourceSlots.TryGetValue(parameterExpression, out var parameterSource)
                    ? parameterSource.ElementType
                    : parameterExpression.Type;
                return true;

            case MemberExpression nestedMember:
                return TryGetProjectionSourceType(nestedMember.Expression, out sourceType);

            case MethodCallExpression methodCall:
                if (TryGetProjectionSourceType(methodCall.Object, out sourceType))
                    return true;

                foreach (var argument in methodCall.Arguments)
                {
                    if (TryGetProjectionSourceType(argument, out sourceType))
                        return true;
                }

                break;
        }

        sourceType = null!;
        return false;
    }

    private void PushDownPostPagingOperations(string operatorName)
    {
        if (!operations.Any(static operation => operation is QueryPlanOperation.Skip or QueryPlanOperation.Take))
            return;

        var innerOperations = operations.ToArray();
        var preservedOrderings = innerOperations
            .OfType<QueryPlanOperation.OrderBy>()
            .LastOrDefault()
            ?.Orderings
            .ToArray() ?? [];

        operations.Clear();
        operations.Add(new QueryPlanOperation.Pushdown(innerOperations, preservedOrderings));
    }

    private static void RejectProjectedOperator(ParsedQuery parsed, string operatorName)
    {
        if (parsed.Projection is null or QueryPlanProjection.Entity)
            return;

        throw new QueryTranslationException(
            $"LINQ operator '{operatorName}' after Select(...) is not supported by the DataLinq expression parser. " +
            "Materialize before applying provider-side operators over projected values.");
    }

    private static void RejectGroupedOperator(ParsedQuery parsed, string operatorName)
    {
        if (parsed.Grouping is null)
            return;

        throw new QueryTranslationException(
            $"LINQ operator '{operatorName}' after GroupBy(...) is not supported by the DataLinq expression parser. " +
            "Only an immediate grouped aggregate Select projection is supported.");
    }

    private static void RejectGroupedProjectionTerminal(ParsedQuery parsed, string operatorName)
    {
        if (parsed.Projection is not QueryPlanProjection.GroupedAggregate)
            return;

        throw new QueryTranslationException(
            $"Terminal operator '{operatorName}' over grouped aggregate projections is not supported by the DataLinq expression parser. " +
            "Materialize the grouped aggregate projection before applying terminal operators.");
    }

    private bool CanBindProjectionParameter(ParsedQuery parsed)
        => operations.Any(static operation => operation is QueryPlanOperation.Join) &&
           parsed.Projection is QueryPlanProjection.JoinedRowLocal;

    private void RejectUnsupportedPostJoinComposition(
        ParsedQuery parsed,
        string operatorName,
        bool allowAfterPaging = false)
    {
        if (!operations.Any(static operation => operation is QueryPlanOperation.Join))
            return;

        if (!CanBindProjectionParameter(parsed))
        {
            throw new QueryTranslationException(
                "Join queries can compose only over joined row projections whose members map to source-slot values. " +
                $"Operator: {operatorName}");
        }

        if (!allowAfterPaging &&
            operations.Any(static operation => operation is QueryPlanOperation.Skip or QueryPlanOperation.Take))
        {
            throw new QueryTranslationException(
                $"LINQ operator '{operatorName}' after Skip(...) or Take(...) over a joined query is not supported yet. " +
                "Materialize before composing further over paged joined rows.");
        }
    }

    private static bool TryGetProjectionMembers(
        QueryPlanProjection projection,
        out IReadOnlyList<QueryPlanProjectionMember> members)
    {
        switch (projection)
        {
            case QueryPlanProjection.Anonymous anonymous:
                members = anonymous.Members;
                return true;
            case QueryPlanProjection.JoinedRowLocal joined:
                members = joined.Members;
                return true;
            default:
                members = [];
                return false;
        }
    }

    private void RejectPostJoinOperator(string operatorName)
    {
        if (operations.Any(static operation => operation is QueryPlanOperation.Join))
        {
            throw new QueryTranslationException(
                "Join queries currently support only the Join body clause. " +
                "Filtering, ordering, and additional from clauses over joins are not supported yet. " +
                $"Operator: {operatorName}");
        }
    }

    private void RejectPostJoinTerminalOperator(string operatorName)
    {
        if (operations.Any(static operation => operation is QueryPlanOperation.Join))
            throw new QueryTranslationException($"Terminal operators over explicit Join queries are not supported yet. Operator: {operatorName}");
    }

    private void WithSource(ParameterExpression parameter, QueryPlanSourceSlot source, Action action)
    {
        parameterSourceSlots[parameter] = source;
        try
        {
            action();
        }
        finally
        {
            parameterSourceSlots.Remove(parameter);
        }
    }

    private TResult WithSource<TResult>(ParameterExpression parameter, QueryPlanSourceSlot source, Func<TResult> action)
    {
        parameterSourceSlots[parameter] = source;
        try
        {
            return action();
        }
        finally
        {
            parameterSourceSlots.Remove(parameter);
        }
    }

    private void WithProjection(ParameterExpression parameter, QueryPlanProjection projection, Action action)
    {
        parameterProjections[parameter] = projection;
        try
        {
            action();
        }
        finally
        {
            parameterProjections.Remove(parameter);
        }
    }

    private TResult WithProjection<TResult>(ParameterExpression parameter, QueryPlanProjection projection, Func<TResult> action)
    {
        parameterProjections[parameter] = projection;
        try
        {
            return action();
        }
        finally
        {
            parameterProjections.Remove(parameter);
        }
    }

    private object? EvaluateScalar(Expression expression)
    {
        if (ContainsQueryReference(expression))
            throw new QueryTranslationException($"Expression '{expression}' contains a query source and cannot be evaluated as a local scalar.");

        return ExpressionLocalValueEvaluator.Evaluate(expression, null, null, options.LocalValueEvaluation);
    }

    private object?[] EvaluateLocalSequence(Expression expression)
    {
        if (TryEvaluateLocalSequence(expression, out var values))
            return values;

        throw new QueryTranslationException($"Expression '{expression}' cannot be evaluated as a local sequence.");
    }

    private bool TryEvaluateLocalSequence(Expression expression, out object?[] values)
    {
        values = [];
        expression = NormalizeSequenceExpression(UnwrapConvert(expression));

        if (ContainsQueryReference(expression))
            return false;

        if (IsQueryableSequence(expression.Type))
            throw new QueryTranslationException(
                $"IQueryable expression '{expression}' cannot be evaluated as a local sequence by the DataLinq expression parser. " +
                "Nested database queries are not supported in local Contains(...) or Any(...) predicates.");

        if (expression is MethodCallExpression methodCall &&
            IsEnumerableMethod(methodCall, nameof(Enumerable.Select)) &&
            methodCall.Arguments.Count == 2)
        {
            if (!TryEvaluateLocalSequence(methodCall.Arguments[0], out var sourceValues))
                return false;

            var selector = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
            if (selector.Parameters.Count != 1)
                return false;

            values = sourceValues
                .Select(value => ExpressionLocalValueEvaluator.Evaluate(selector.Body, selector.Parameters[0], value, options.LocalValueEvaluation))
                .ToArray();
            return true;
        }

        try
        {
            var value = ExpressionLocalValueEvaluator.Evaluate(expression, null, null, options.LocalValueEvaluation);
            if (value is IQueryable)
            {
                throw new QueryTranslationException(
                    $"IQueryable expression '{expression}' cannot be evaluated as a local sequence by the DataLinq expression parser. " +
                    "Nested database queries are not supported in local Contains(...) or Any(...) predicates.");
            }

            return TryConvertToArray(value, out values);
        }
        catch (QueryTranslationException)
        {
            throw;
        }
        catch
        {
            values = [];
            return false;
        }
    }

    private bool ContainsQueryReference(Expression? expression)
    {
        if (expression is null)
            return false;

        var visitor = new QueryReferenceVisitor(this);
        visitor.Visit(expression);
        return visitor.ContainsReference;
    }

    private static Expression NormalizeSequenceExpression(Expression expression)
    {
        if (expression.Type.IsByRefLike &&
            expression is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } implicitCall)
        {
            return implicitCall.Arguments[0];
        }

        return expression;
    }

    private static bool TryConvertToArray(object? value, out object?[] values)
    {
        values = value switch
        {
            null => [],
            object?[] array => array,
            IEnumerable<object?> enumerable => enumerable.ToArray(),
            IEnumerable enumerable => enumerable.Cast<object?>().ToArray(),
            _ => []
        };

        return value == null || values.Length != 0 || value is IEnumerable;
    }

    private static Expression UnwrapConvert(Expression? expression)
    {
        if (expression is null)
            return null!;

        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert ||
                unary.NodeType == ExpressionType.ConvertChecked ||
                unary.NodeType == ExpressionType.Quote))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static Expression UnwrapQueryColumnAccess(Expression expression)
    {
        expression = UnwrapConvert(expression);
        if (expression is MemberExpression { Member.Name: "Value", Expression: not null } memberExpression &&
            Nullable.GetUnderlyingType(memberExpression.Expression.Type) != null)
        {
            expression = memberExpression.Expression;
        }

        return UnwrapConvert(expression);
    }

    private static LambdaExpression UnwrapLambda(Expression expression, string sourceExpression)
    {
        expression = UnwrapConvert(expression);
        if (expression is LambdaExpression lambda)
            return lambda;

        throw new QueryTranslationException($"Predicate lambda '{sourceExpression}' is not supported.");
    }

    private static void EnsureArgumentCount(MethodCallExpression methodCall, int count)
    {
        if (methodCall.Arguments.Count != count)
            throw new QueryTranslationException($"LINQ operator '{methodCall.Method.Name}' has unsupported argument count {methodCall.Arguments.Count}. Expression: {methodCall}");
    }

    private static bool IsQueryableMethod(MethodCallExpression methodCall)
        => methodCall.Method.IsGenericMethod &&
           methodCall.Method.GetGenericMethodDefinition().DeclaringType == typeof(Queryable);

    private static bool IsEnumerableMethod(MethodCallExpression methodCall, string methodName)
        => methodCall.Method.IsGenericMethod &&
           methodCall.Method.Name == methodName &&
           methodCall.Method.GetGenericMethodDefinition().DeclaringType == typeof(Enumerable);

    private static bool IsQueryableSequence(Type type)
        => typeof(IQueryable).IsAssignableFrom(type);

    private static Type GetQueryableElementType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
            return type.GetGenericArguments()[0];

        throw new QueryTranslationException($"Queryable expression type '{type}' does not expose an IQueryable<T> element type.");
    }

    private static bool IsRootQueryable(IQueryable queryable)
    {
        var expression = UnwrapConvert(queryable.Expression);
        return expression is ConstantExpression constantExpression &&
               ReferenceEquals(constantExpression.Value, queryable);
    }

    private bool IsDatabaseQueryCall(Expression? expression)
    {
        expression = UnwrapConvert(expression);
        return expression is MethodCallExpression { Arguments.Count: 0 } methodCall &&
               methodCall.Method.Name == "Query" &&
               metadata.CsType.Type is { } databaseType &&
               databaseType.IsAssignableFrom(methodCall.Type);
    }

    private static bool TryGetDbReadElementType(Type type, out Type elementType)
    {
        var current = type;
        while (current is not null)
        {
            if (current.IsGenericType &&
                current.GetGenericTypeDefinition() == typeof(DbRead<>))
            {
                elementType = current.GetGenericArguments()[0];
                return true;
            }

            current = current.BaseType;
        }

        elementType = null!;
        return false;
    }

    private static bool TryEvaluateCapturedValue(Expression? expression, out object? value)
    {
        expression = UnwrapConvert(expression);
        switch (expression)
        {
            case ConstantExpression constantExpression:
                value = constantExpression.Value;
                return true;

            case MemberExpression { Member: FieldInfo field } memberExpression
                when TryEvaluateCapturedValue(memberExpression.Expression, out var instance):
                value = field.GetValue(instance);
                return true;

            default:
                value = null;
                return false;
        }
    }

    private bool TryGetConstantInt(Expression expression, out int value)
    {
        expression = UnwrapConvert(expression);
        if (expression is ConstantExpression constantExpression)
        {
            value = System.Convert.ToInt32(constantExpression.Value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        if (!ContainsParameter(expression))
        {
            value = System.Convert.ToInt32(
                ExpressionLocalValueEvaluator.Evaluate(expression, null, null, options.LocalValueEvaluation),
                System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool ContainsParameter(Expression expression)
    {
        var visitor = new AnyParameterVisitor();
        visitor.Visit(expression);
        return visitor.ContainsParameter;
    }

    private static bool TryGetCountExistsSemantics(ExpressionType comparisonType, int constant, out bool shouldExist)
    {
        switch (comparisonType)
        {
            case ExpressionType.GreaterThan when constant == 0:
            case ExpressionType.GreaterThanOrEqual when constant == 1:
            case ExpressionType.NotEqual when constant == 0:
                shouldExist = true;
                return true;

            case ExpressionType.Equal when constant == 0:
            case ExpressionType.LessThanOrEqual when constant == 0:
            case ExpressionType.LessThan when constant == 1:
                shouldExist = false;
                return true;

            default:
                shouldExist = false;
                return false;
        }
    }

    private static bool IsComparison(ExpressionType type)
        => type is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

    private static QueryPlanComparisonOperator GetComparisonOperator(ExpressionType type) => type switch
    {
        ExpressionType.Equal => QueryPlanComparisonOperator.Equal,
        ExpressionType.NotEqual => QueryPlanComparisonOperator.NotEqual,
        ExpressionType.GreaterThan => QueryPlanComparisonOperator.GreaterThan,
        ExpressionType.GreaterThanOrEqual => QueryPlanComparisonOperator.GreaterThanOrEqual,
        ExpressionType.LessThan => QueryPlanComparisonOperator.LessThan,
        ExpressionType.LessThanOrEqual => QueryPlanComparisonOperator.LessThanOrEqual,
        _ => throw new QueryTranslationException($"Expression type '{type}' is not supported for query plan comparison mapping.")
    };

    private static QueryPlanComparisonOperator ReverseComparisonOperator(QueryPlanComparisonOperator comparisonOperator) => comparisonOperator switch
    {
        QueryPlanComparisonOperator.GreaterThan => QueryPlanComparisonOperator.LessThan,
        QueryPlanComparisonOperator.GreaterThanOrEqual => QueryPlanComparisonOperator.LessThanOrEqual,
        QueryPlanComparisonOperator.LessThan => QueryPlanComparisonOperator.GreaterThan,
        QueryPlanComparisonOperator.LessThanOrEqual => QueryPlanComparisonOperator.GreaterThanOrEqual,
        _ => comparisonOperator
    };

    private static ExpressionType ReverseExpressionType(ExpressionType expressionType) => expressionType switch
    {
        ExpressionType.GreaterThan => ExpressionType.LessThan,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
        ExpressionType.LessThan => ExpressionType.GreaterThan,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
        _ => expressionType
    };

    private static bool EvaluateConstantBinary(ExpressionType nodeType, object? left, object? right)
    {
        return nodeType switch
        {
            ExpressionType.Equal => Equals(left, right),
            ExpressionType.NotEqual => !Equals(left, right),
            ExpressionType.GreaterThan => Compare(left, right) > 0,
            ExpressionType.GreaterThanOrEqual => Compare(left, right) >= 0,
            ExpressionType.LessThan => Compare(left, right) < 0,
            ExpressionType.LessThanOrEqual => Compare(left, right) <= 0,
            _ => throw new QueryTranslationException($"Constant binary expression '{nodeType}' is not supported in query plan predicate translation.")
        };
    }

    private static int Compare(object? left, object? right)
    {
        if (left is null || right is null)
            throw new QueryTranslationException("Null constant values can only be compared with equality in query plan predicate translation.");

        if (left is IComparable comparable)
            return comparable.CompareTo(right);

        throw new QueryTranslationException($"Constant value '{left}' does not support comparison in query plan predicate translation.");
    }

    private static Type GetNonNullableType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static string GetExpressionShape(Expression selector)
        => selector switch
        {
            NewExpression => "new",
            MemberInitExpression => "member-init",
            BinaryExpression binary => binary.NodeType.ToString(),
            MethodCallExpression methodCall => methodCall.Method.Name,
            MemberExpression member => member.Member.Name,
            _ => selector.NodeType.ToString()
        };

    private sealed record ParsedQuery(
        QueryPlanSourceSlot RootSource,
        Type ElementType,
        QueryPlanProjection? Projection = null,
        QueryPlanResult? Result = null,
        QueryPlanGrouping? Grouping = null);

    private sealed record QueryPlanGrouping(QueryPlanSourceSlot Source, QueryPlanValue Key);

    private sealed class QueryReferenceVisitor(ExpressionQueryPlanParser parser) : ExpressionVisitor
    {
        public bool ContainsReference { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (parser.parameterSourceSlots.ContainsKey(node) ||
                parser.parameterProjections.ContainsKey(node))
            {
                ContainsReference = true;
            }

            return node;
        }
    }

    private sealed class QuerySourceCollector(ExpressionQueryPlanParser parser) : ExpressionVisitor
    {
        private readonly List<QueryPlanSourceSlot> sources = [];

        public IReadOnlyList<QueryPlanSourceSlot> Sources => sources;

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (parser.parameterSourceSlots.TryGetValue(node, out var source))
                Add(source);

            if (parser.parameterProjections.TryGetValue(node, out var projection) &&
                TryGetProjectionMembers(projection, out var members))
            {
                foreach (var member in members)
                    AddSources(member.Value);
            }

            return node;
        }

        private void AddSources(QueryPlanValue value)
        {
            switch (value)
            {
                case QueryPlanColumnValue column:
                    Add(column.Source);
                    break;
                case QueryPlanConvertedValue converted:
                    AddSources(converted.Value);
                    break;
                case QueryPlanFunctionValue function:
                    foreach (var argument in function.Arguments)
                        AddSources(argument);
                    break;
            }
        }

        private void Add(QueryPlanSourceSlot source)
        {
            if (!sources.Contains(source))
                sources.Add(source);
        }
    }

    private sealed class ProjectionUnsupportedShapeVisitor(
        ExpressionQueryPlanParser parser,
        Expression selector) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (parser.TryGetRelationProjectionProperty(node, out var relationProperty))
            {
                throw new QueryTranslationException(
                    $"Relation property '{relationProperty.PropertyName}' is not supported in LINQ Select projection. " +
                    "Projection expressions are evaluated after materializing the selected rows; load relation data explicitly after ToList(). " +
                    $"Expression: {selector}");
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable))
            {
                throw new QueryTranslationException(
                    "Nested database query projection is not supported in LINQ Select projection. " +
                    "Projection expressions are evaluated after materializing the selected rows; load nested query results explicitly after ToList(). " +
                    $"Expression: {selector}");
            }

            return base.VisitMethodCall(node);
        }
    }

    private sealed class AnyParameterVisitor : ExpressionVisitor
    {
        public bool ContainsParameter { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            ContainsParameter = true;
            return node;
        }
    }
}
