using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing.Structure;

namespace DataLinq.Linq.Planning;

internal sealed class RemotionQueryPlanAdapter
{
    private readonly DatabaseDefinition metadata;
    private readonly QueryPlanBindingFrame bindings = new();
    private readonly List<QueryPlanSourceSlot> sources = [];
    private readonly List<QueryPlanOperation> operations = [];
    private readonly Dictionary<IQuerySource, QueryPlanSourceSlot> sourceSlots = [];
    private readonly Dictionary<ParameterExpression, QueryPlanSourceSlot> parameterSourceSlots = [];
    private int relationSubqueryCounter;

    private RemotionQueryPlanAdapter(DatabaseDefinition metadata)
    {
        this.metadata = metadata;
    }

    public static DataLinqQueryPlan Convert<TDatabase, TModel>(Database<TDatabase> database, IQueryable<TModel> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(query);

        return ConvertExpression(database, query.Expression, typeof(TModel));
    }

    public static DataLinqQueryPlan Convert<TDatabase, TResult>(Database<TDatabase> database, Expression<Func<TResult>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(query);

        return ConvertExpression(database, query.Body, typeof(TResult));
    }

    private static DataLinqQueryPlan ConvertExpression<TDatabase>(Database<TDatabase> database, Expression expression, Type resultType)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var queryParser = QueryParser.CreateDefault();
        var queryModel = queryParser.GetParsedQuery(expression);
        var adapter = new RemotionQueryPlanAdapter(database.Provider.Metadata);
        return adapter.ConvertQueryModel(queryModel, resultType);
    }

    private DataLinqQueryPlan ConvertQueryModel(QueryModel queryModel, Type resultType)
    {
        var rootSource = ConvertQueryBody(queryModel);
        var projection = CreateProjection(queryModel.SelectClause.Selector, resultType);
        var result = CreateResult(queryModel, projection, resultType);

        return new DataLinqQueryPlan(sources, operations, projection, result, bindings);
    }

    private QueryPlanSourceSlot ConvertQueryBody(QueryModel queryModel)
    {
        var subQueryModel = ExtractQueryModel(queryModel.MainFromClause.FromExpression);
        QueryPlanSourceSlot rootSource;
        if (subQueryModel is not null)
        {
            if (HasPagingResultOperator(subQueryModel) &&
                (queryModel.BodyClauses.Count > 0 || HasPagingResultOperator(queryModel)))
            {
                throw new QueryTranslationException(
                    "LINQ operators after Skip(...) or Take(...) require subquery pushdown and are not supported yet. " +
                    "Apply filtering and ordering before paging, or materialize before applying post-paging operators. " +
                    $"Query model: {queryModel}");
            }

            if (HasJoinBodyClause(subQueryModel) &&
                (queryModel.BodyClauses.Count > 0 || queryModel.ResultOperators.Any()))
            {
                throw new QueryTranslationException($"Join queries currently support only the Join body clause. Filtering, ordering, and additional from clauses over joins are not supported yet. Query model: {queryModel}");
            }

            rootSource = ConvertQueryBody(subQueryModel);
            sourceSlots[queryModel.MainFromClause] = rootSource;
        }
        else
        {
            rootSource = RegisterSource(queryModel.MainFromClause, queryModel.MainFromClause.ItemType, QueryPlanSourceKind.RootTable);
        }

        ValidateJoinShape(queryModel);

        foreach (var bodyClause in queryModel.BodyClauses)
        {
            switch (bodyClause)
            {
                case WhereClause whereClause:
                    operations.Add(new QueryPlanOperation.Where(ConvertPredicate(whereClause.Predicate)));
                    break;

                case OrderByClause orderByClause:
                    operations.Add(new QueryPlanOperation.OrderBy(orderByClause.Orderings.Select(ordering =>
                        new QueryPlanOrdering(
                            ConvertValue(ordering.Expression),
                            ordering.OrderingDirection == OrderingDirection.Asc
                                ? QueryPlanOrderingDirection.Ascending
                                : QueryPlanOrderingDirection.Descending))));
                    break;

                case JoinClause joinClause:
                    operations.Add(new QueryPlanOperation.Join(ConvertJoin(queryModel.MainFromClause, joinClause)));
                    break;

                case GroupJoinClause:
                    throw new QueryTranslationException($"GroupJoin is not supported. Use a simple inner Join with direct member keys. Query model: {queryModel}");

                default:
                    throw new QueryTranslationException($"Query body clause '{bodyClause.GetType().Name}' is not supported by the query plan adapter. Query model: {queryModel}");
            }
        }

        foreach (var resultOperator in queryModel.ResultOperators)
        {
            switch (resultOperator)
            {
                case SkipResultOperator skip:
                    operations.Add(new QueryPlanOperation.Skip(ConvertValue(skip.Count)));
                    break;

                case TakeResultOperator take:
                    operations.Add(new QueryPlanOperation.Take(ConvertValue(take.Count)));
                    break;
            }
        }

        return rootSource;
    }

    private static void ValidateJoinShape(QueryModel queryModel)
    {
        if (queryModel.BodyClauses.Any(static body => body is GroupJoinClause))
            throw new QueryTranslationException($"GroupJoin is not supported. Use a simple inner Join with direct member keys. Query model: {queryModel}");

        var joinClauses = queryModel.BodyClauses.OfType<JoinClause>().ToArray();
        if (joinClauses.Length == 0)
            return;

        if (joinClauses.Length != 1)
            throw new QueryTranslationException($"Only a single explicit inner Join is supported. Query model: {queryModel}");

        if (queryModel.BodyClauses.Any(static body => body is not JoinClause))
            throw new QueryTranslationException($"Join queries currently support only the Join body clause. Filtering, ordering, and additional from clauses over joins are not supported yet. Query model: {queryModel}");

        if (queryModel.ResultOperators.Any())
            throw new QueryTranslationException($"Result operators over explicit Join queries are not supported yet. Query model: {queryModel}");
    }

    private QueryPlanJoin ConvertJoin(MainFromClause rootFromClause, JoinClause joinClause)
    {
        if (joinClause.InnerSequence is not ConstantExpression { Value: IQueryable })
            throw new QueryTranslationException($"Join inner sequence '{joinClause.InnerSequence}' is not supported. Only direct DataLinq query sources are supported.");

        var rootSource = GetSource(rootFromClause);
        var innerSource = RegisterSource(joinClause, joinClause.ItemType, QueryPlanSourceKind.ExplicitJoin);
        var leftColumn = GetJoinKeyColumn(rootSource, joinClause.OuterKeySelector, "outer");
        var rightColumn = GetJoinKeyColumn(innerSource, joinClause.InnerKeySelector, "inner");

        return new QueryPlanJoin(QueryPlanJoinKind.Inner, rootSource, leftColumn, innerSource, rightColumn);
    }

    private QueryPlanResult CreateResult(QueryModel queryModel, QueryPlanProjection projection, Type resultType)
    {
        QueryPlanResult result = QueryPlanResult.Sequence(resultType);

        foreach (var resultOperator in queryModel.ResultOperators)
        {
            result = resultOperator switch
            {
                SkipResultOperator or TakeResultOperator => result,
                SingleResultOperator single => new QueryPlanResult(
                    single.ReturnDefaultWhenEmpty ? QueryPlanResultKind.SingleOrDefault : QueryPlanResultKind.Single,
                    resultType),
                FirstResultOperator first => new QueryPlanResult(
                    first.ReturnDefaultWhenEmpty ? QueryPlanResultKind.FirstOrDefault : QueryPlanResultKind.First,
                    resultType),
                LastResultOperator last => new QueryPlanResult(
                    last.ReturnDefaultWhenEmpty ? QueryPlanResultKind.LastOrDefault : QueryPlanResultKind.Last,
                    resultType),
                CountResultOperator => new QueryPlanResult(QueryPlanResultKind.Count, resultType),
                AnyResultOperator => new QueryPlanResult(QueryPlanResultKind.Any, resultType),
                SumResultOperator => new QueryPlanResult(QueryPlanResultKind.Sum, resultType, GetAggregateSelector(queryModel)),
                MinResultOperator => new QueryPlanResult(QueryPlanResultKind.Min, resultType, GetAggregateSelector(queryModel)),
                MaxResultOperator => new QueryPlanResult(QueryPlanResultKind.Max, resultType, GetAggregateSelector(queryModel)),
                AverageResultOperator => new QueryPlanResult(QueryPlanResultKind.Average, resultType, GetAggregateSelector(queryModel)),
                _ => throw new QueryTranslationException($"Result operator '{GetResultOperatorDisplayName(resultOperator)}' is not supported by the query plan adapter. Query model: {queryModel}")
            };
        }

        return result;
    }

    private QueryPlanValue GetAggregateSelector(QueryModel queryModel)
    {
        var selector = UnwrapConvert(queryModel.SelectClause.Selector);
        if (selector is MemberExpression memberExpression &&
            memberExpression.Member.Name == "Value" &&
            memberExpression.Expression is not null &&
            Nullable.GetUnderlyingType(memberExpression.Expression.Type) is not null)
        {
            selector = memberExpression.Expression;
        }

        return ConvertValue(selector);
    }

    private QueryPlanProjection CreateProjection(Expression selector, Type resultType)
    {
        selector = UnwrapConvert(selector);

        if (selector is QuerySourceReferenceExpression querySource)
            return new QueryPlanProjection.Entity(GetSource(querySource.ReferencedQuerySource));

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
            var argument = newExpression.Arguments[index];
            if (!TryConvertValue(argument, out var value))
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
            SubQueryExpression subQuery when TryConvertSubQueryPredicate(subQuery, isNegated, out var subQueryPredicate) => subQueryPredicate,
            _ when !ContainsQueryReference(expression) => new QueryPlanPredicate.Fixed(System.Convert.ToBoolean(EvaluateScalar(expression), System.Globalization.CultureInfo.InvariantCulture) ^ isNegated),
            _ => throw new QueryTranslationException($"Predicate expression '{expression}' is not supported by the query plan adapter.")
        };

        if (isNegated &&
            predicate is not QueryPlanPredicate.In { IsNegated: true } &&
            predicate is not QueryPlanPredicate.Exists { IsNegated: true } &&
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
        var nullSemantics = comparisonOperator == QueryPlanComparisonOperator.NotEqual && IsNullableColumnComparedWithNonNull(left, right)
            ? QueryPlanNullSemantics.CSharpNullableNotEqualIncludesNull
            : QueryPlanNullSemantics.Default;

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

            if (isNegated)
                predicate = new QueryPlanPredicate.Not(predicate);

            return true;
        }

        predicate = null!;
        return false;
    }

    private bool TryConvertSubQueryPredicate(SubQueryExpression subQuery, bool isNegated, out QueryPlanPredicate predicate)
    {
        if (TryConvertRelationAnySubQuery(subQuery, isNegated, out predicate))
            return true;

        foreach (var resultOperator in subQuery.QueryModel.ResultOperators)
        {
            if (resultOperator is ContainsResultOperator contains)
                return TryConvertLocalContainsSubQuery(subQuery.QueryModel, contains, isNegated, out predicate);

            if (resultOperator is AnyResultOperator)
                return TryConvertLocalAnySubQuery(subQuery.QueryModel, isNegated, out predicate);
        }

        predicate = null!;
        return false;
    }

    private bool TryConvertHasValuePredicate(MemberExpression member, out QueryPlanPredicate predicate)
    {
        if (member.Member.Name == nameof(Nullable<int>.HasValue) &&
            member.Expression is MemberExpression nullableMember &&
            Nullable.GetUnderlyingType(nullableMember.Type) is not null &&
            TryGetColumnValue(nullableMember, out var column))
        {
            predicate = new QueryPlanPredicate.Compare(
                column,
                QueryPlanComparisonOperator.NotEqual,
                new QueryPlanConstantValue(null, nullableMember.Type));
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
            var values = LocalSequenceExtractor.Evaluate(methodCall.Arguments[0]);
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
            LocalSequenceExtractor.TryEvaluate(UnwrapConvert(sequenceExpression), out var instanceValues))
        {
            return CreateLocalMembershipPredicate(instanceValues, itemExpression, isNegated, out predicate);
        }

        return false;
    }

    private bool TryConvertLocalContainsSubQuery(QueryModel queryModel, ContainsResultOperator contains, bool isNegated, out QueryPlanPredicate predicate)
    {
        var values = LocalSequenceExtractor.Evaluate(queryModel);
        return CreateLocalMembershipPredicate(values, contains.Item, isNegated, out predicate);
    }

    private bool CreateLocalMembershipPredicate(object?[] values, Expression itemExpression, bool isNegated, out QueryPlanPredicate predicate)
    {
        itemExpression = LocalSequenceExtractor.UnwrapQueryColumnAccess(itemExpression);

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

        throw new QueryTranslationException($"Contains item expression '{itemExpression}' is not supported by the query plan adapter. Expected member access on the query source or a local constant.");
    }

    private bool TryConvertLocalAny(MethodCallExpression methodCall, bool isNegated, out QueryPlanPredicate predicate)
    {
        predicate = null!;
        if (!IsEnumerableMethod(methodCall, nameof(Enumerable.Any)) || methodCall.Arguments.Count is not (1 or 2))
            return false;

        if (TryGetRelationProperty(methodCall.Arguments[0], out _))
            return false;

        var sourceValues = LocalSequenceExtractor.Evaluate(methodCall.Arguments[0]);
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

    private bool TryConvertLocalAnySubQuery(QueryModel queryModel, bool isNegated, out QueryPlanPredicate predicate)
    {
        predicate = null!;
        var sourceValues = LocalSequenceExtractor.Evaluate(queryModel.MainFromClause.FromExpression);
        if (queryModel.BodyClauses.Count == 0)
        {
            predicate = new QueryPlanPredicate.Fixed(isNegated ? sourceValues.Length == 0 : sourceValues.Length > 0);
            return true;
        }

        if (sourceValues.Length == 0)
        {
            predicate = new QueryPlanPredicate.Fixed(isNegated);
            return true;
        }

        if (queryModel.BodyClauses.Count == 1 &&
            queryModel.BodyClauses[0] is WhereClause whereClause &&
            whereClause.Predicate is BinaryExpression { NodeType: ExpressionType.Equal } binary &&
            TryCreateLocalAnyMembership(binary, queryModel.MainFromClause, sourceValues, isNegated, out predicate))
        {
            return true;
        }

        throw new QueryTranslationException($"Any(predicate) over a non-empty local sequence only supports equality membership against a query column. Predicate: {queryModel.BodyClauses.FirstOrDefault()}");
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

    private bool TryCreateLocalAnyMembership(
        BinaryExpression binary,
        MainFromClause localFromClause,
        object?[] sourceValues,
        bool isNegated,
        out QueryPlanPredicate predicate)
    {
        return TryCreateLocalAnyMembershipSide(binary.Left, binary.Right, localFromClause, sourceValues, isNegated, out predicate) ||
               TryCreateLocalAnyMembershipSide(binary.Right, binary.Left, localFromClause, sourceValues, isNegated, out predicate);
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
        outerCandidate = LocalSequenceExtractor.UnwrapQueryColumnAccess(outerCandidate);
        localCandidate = LocalSequenceExtractor.UnwrapQueryColumnAccess(localCandidate);

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

    private bool TryCreateLocalAnyMembershipSide(
        Expression outerCandidate,
        Expression localCandidate,
        MainFromClause localFromClause,
        object?[] sourceValues,
        bool isNegated,
        out QueryPlanPredicate predicate)
    {
        predicate = null!;
        outerCandidate = LocalSequenceExtractor.UnwrapQueryColumnAccess(outerCandidate);
        localCandidate = LocalSequenceExtractor.UnwrapQueryColumnAccess(localCandidate);

        if (!TryGetColumnValue(outerCandidate, out var outerColumn) ||
            !LocalSequenceExtractor.TryProject(localFromClause, localCandidate, sourceValues, out var values))
        {
            return false;
        }

        predicate = new QueryPlanPredicate.In(
            outerColumn,
            bindings.CaptureLocalSequence(values, outerColumn.ClrType),
            isNegated);
        return true;
    }

    private static bool TryProjectLocalValues(ParameterExpression parameter, Expression selector, object?[] sourceValues, out object?[] values)
    {
        values = [];
        selector = LocalSequenceExtractor.UnwrapQueryColumnAccess(selector);

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
                .Select(value => ProjectionExpressionEvaluator.Evaluate(selector, parameter, value))
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
        expression = LocalSequenceExtractor.UnwrapQueryColumnAccess(expression);

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

    private bool TryConvertRelationAnySubQuery(SubQueryExpression subQuery, bool isNegated, out QueryPlanPredicate predicate)
    {
        predicate = null!;
        if (!TryGetRelationProperty(subQuery.QueryModel.MainFromClause.FromExpression, out var relationProperty, out var parentSource))
            return false;

        if (subQuery.QueryModel.ResultOperators.Count != 1 ||
            subQuery.QueryModel.ResultOperators[0] is not AnyResultOperator)
        {
            throw new QueryTranslationException($"Relation subquery '{subQuery.QueryModel}' is not supported. Only relation Any(...) predicates are supported in this context.");
        }

        var childSource = CreateRelationChildSource(relationProperty, subQuery.QueryModel.MainFromClause);
        QueryPlanPredicate? childPredicate = null;
        if (subQuery.QueryModel.BodyClauses.Count == 1 && subQuery.QueryModel.BodyClauses[0] is WhereClause whereClause)
            childPredicate = ConvertRelationPredicate(relationProperty, childSource, subQuery.QueryModel.MainFromClause, whereClause.Predicate);
        else if (subQuery.QueryModel.BodyClauses.Count > 1)
            throw new QueryTranslationException($"Relation predicate '{subQuery.QueryModel}' is not supported. Only a single Where predicate inside Any(...) or Count(...) is supported.");

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

    private bool TryGetRelationCount(
        Expression expression,
        out RelationProperty relationProperty,
        out QueryPlanSourceSlot parentSource,
        out Func<QueryPlanSourceSlot, QueryPlanPredicate?> childPredicateFactory)
    {
        expression = UnwrapConvert(expression);

        if (expression is SubQueryExpression subQuery &&
            subQuery.QueryModel.ResultOperators.Count == 1 &&
            subQuery.QueryModel.ResultOperators[0] is CountResultOperator &&
            TryGetRelationProperty(subQuery.QueryModel.MainFromClause.FromExpression, out relationProperty, out parentSource))
        {
            var relation = relationProperty;
            childPredicateFactory = childSource =>
            {
                if (subQuery.QueryModel.BodyClauses.Count == 0)
                    return null;

                if (subQuery.QueryModel.BodyClauses.Count == 1 && subQuery.QueryModel.BodyClauses[0] is WhereClause whereClause)
                    return ConvertRelationPredicate(relation, childSource, subQuery.QueryModel.MainFromClause, whereClause.Predicate);

                throw new QueryTranslationException($"Relation predicate '{subQuery.QueryModel}' is not supported. Only a single Where predicate inside Any(...) or Count(...) is supported.");
            };
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

    private QueryPlanPredicate ConvertRelationPredicate(RelationProperty relationProperty, QueryPlanSourceSlot childSource, MainFromClause childQuerySource, Expression predicate)
    {
        sourceSlots[childQuerySource] = childSource;
        try
        {
            return ConvertRelationPredicate(relationProperty, childSource, predicate);
        }
        finally
        {
            sourceSlots.Remove(childQuerySource);
        }
    }

    private QueryPlanPredicate ConvertRelationPredicate(RelationProperty relationProperty, QueryPlanSourceSlot childSource, ParameterExpression childParameter, Expression predicate)
    {
        parameterSourceSlots[childParameter] = childSource;
        try
        {
            return ConvertRelationPredicate(relationProperty, childSource, predicate);
        }
        finally
        {
            parameterSourceSlots.Remove(childParameter);
        }
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

    private QueryPlanSourceSlot CreateRelationChildSource(RelationProperty relationProperty, MainFromClause? childQuerySource = null)
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
        var childSource = RegisterSource(
            childQuerySource,
            childType,
            QueryPlanSourceKind.RelationSubquery,
            alias: $"r{relationSubqueryCounter++}",
            table: childTable);

        return childSource;
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

    private QueryPlanValue ConvertValue(Expression expression)
    {
        if (TryConvertValue(expression, out var value))
            return value;

        throw new QueryTranslationException($"Value expression '{expression}' is not supported by the query plan adapter.");
    }

    private bool TryConvertValue(Expression expression, out QueryPlanValue value)
    {
        expression = UnwrapConvert(expression);

        if (TryGetColumnValue(expression, out var column))
        {
            value = column;
            return true;
        }

        if (TryConvertFunctionValue(expression, out value))
            return true;

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

        value = null!;
        return false;
    }

    private bool TryGetColumnValue(Expression expression, out QueryPlanColumnValue value)
    {
        expression = LocalSequenceExtractor.UnwrapQueryColumnAccess(UnwrapConvert(expression));

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

        switch (expression)
        {
            case QuerySourceReferenceExpression querySourceReference:
                return sourceSlots.TryGetValue(querySourceReference.ReferencedQuerySource, out source!);

            case ParameterExpression parameter:
                return parameterSourceSlots.TryGetValue(parameter, out source!);

            default:
                source = null!;
                return false;
        }
    }

    private QueryPlanSourceSlot GetSource(IQuerySource querySource)
    {
        if (sourceSlots.TryGetValue(querySource, out var source))
            return source;

        throw new QueryTranslationException($"Query source '{querySource}' has not been registered in the query plan adapter.");
    }

    private QueryPlanSourceSlot RegisterSource(
        IQuerySource? querySource,
        Type itemType,
        QueryPlanSourceKind kind,
        string? alias = null,
        TableDefinition? table = null)
    {
        if (querySource is not null && sourceSlots.TryGetValue(querySource, out var existing))
            return existing;

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
        if (querySource is not null)
            sourceSlots[querySource] = source;

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
        keySelector = LocalSequenceExtractor.UnwrapQueryColumnAccess(UnwrapConvert(keySelector));
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

    private static QueryModel? ExtractQueryModel(Expression? expression)
    {
        switch (expression)
        {
            case SubQueryExpression subQueryExpression:
                return subQueryExpression.QueryModel;

            case MemberExpression memberExpression:
                return ExtractQueryModel(memberExpression.Expression);

            case MethodCallExpression methodCallExpression:
                foreach (var argument in methodCallExpression.Arguments)
                {
                    var subQuery = ExtractQueryModel(argument);
                    if (subQuery != null)
                        return subQuery;
                }
                break;

            case UnaryExpression unaryExpression:
                return ExtractQueryModel(unaryExpression.Operand);
        }

        return null;
    }

    private static bool HasPagingResultOperator(QueryModel queryModel)
        => queryModel.ResultOperators.Any(static op => op is TakeResultOperator or SkipResultOperator);

    private static bool HasJoinBodyClause(QueryModel queryModel)
        => queryModel.BodyClauses.Any(static body => body is JoinClause or GroupJoinClause);

    private static Expression UnwrapConvert(Expression? expression)
    {
        if (expression is null)
            return null!;

        while (expression is UnaryExpression unary &&
                   (unary.NodeType == ExpressionType.Convert ||
                    unary.NodeType == ExpressionType.ConvertChecked ||
                    unary.NodeType == ExpressionType.Quote) ||
               expression is PartialEvaluationExceptionExpression)
        {
            expression = expression is UnaryExpression currentUnary
                ? currentUnary.Operand
                : ((PartialEvaluationExceptionExpression)expression).EvaluatedExpression;
        }

        return expression;
    }

    private static LambdaExpression UnwrapLambda(Expression expression, string sourceExpression)
    {
        expression = UnwrapConvert(expression);
        if (expression is LambdaExpression lambda)
            return lambda;

        throw new QueryTranslationException($"Predicate lambda '{sourceExpression}' is not supported.");
    }

    private static object? EvaluateScalar(Expression expression)
    {
        if (expression is ConstantExpression constantExpression)
            return constantExpression.Value;

        var evaluatedExpression = Evaluator.PartialEval(
            expression,
            candidate => candidate is not QuerySourceReferenceExpression and not SubQueryExpression);

        if (evaluatedExpression is ConstantExpression constantAfterEval)
            return constantAfterEval.Value;

        return ProjectionExpressionEvaluator.Evaluate(evaluatedExpression!);
    }

    private static bool ContainsQueryReference(Expression? expression)
    {
        if (expression is null)
            return false;

        var visitor = new QueryReferenceVisitor();
        visitor.Visit(expression);
        return visitor.ContainsReference;
    }

    private static bool IsEnumerableMethod(MethodCallExpression methodCall, string methodName)
    {
        if (!methodCall.Method.IsGenericMethod || methodCall.Method.Name != methodName)
            return false;

        return methodCall.Method.GetGenericMethodDefinition().DeclaringType == typeof(Enumerable);
    }

    private static bool TryGetConstantInt(Expression expression, out int value)
    {
        expression = UnwrapConvert(expression);
        if (expression is ConstantExpression constantExpression)
        {
            value = System.Convert.ToInt32(constantExpression.Value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        value = 0;
        return false;
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

    private static bool IsNullableColumnComparedWithNonNull(QueryPlanValue left, QueryPlanValue right)
        => IsNullableColumnAndNonNullValue(left, right) || IsNullableColumnAndNonNullValue(right, left);

    private static bool IsNullableColumnAndNonNullValue(QueryPlanValue columnCandidate, QueryPlanValue valueCandidate)
        => columnCandidate is QueryPlanColumnValue column &&
           column.Column.ValueProperty.CsNullable &&
           valueCandidate is not QueryPlanConstantValue { Value: null };

    private static Type GetNonNullableType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static string GetResultOperatorDisplayName(ResultOperatorBase op)
    {
        var name = op.GetType().Name;
        const string suffix = "ResultOperator";
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }

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

    private sealed class QueryReferenceVisitor : ExpressionVisitor
    {
        public bool ContainsReference { get; private set; }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is QuerySourceReferenceExpression or SubQueryExpression)
            {
                ContainsReference = true;
                return node;
            }

            return node.CanReduce ? Visit(node.Reduce()) : node;
        }
    }

    private sealed class QuerySourceCollector(RemotionQueryPlanAdapter adapter) : ExpressionVisitor
    {
        private readonly List<QueryPlanSourceSlot> sources = [];

        public IReadOnlyList<QueryPlanSourceSlot> Sources => sources;

        protected override Expression VisitExtension(Expression node)
        {
            if (node is QuerySourceReferenceExpression querySourceReference)
            {
                Add(adapter.GetSource(querySourceReference.ReferencedQuerySource));
                return node;
            }

            return node.CanReduce ? Visit(node.Reduce()) : node;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (adapter.parameterSourceSlots.TryGetValue(node, out var source))
                Add(source);

            return node;
        }

        private void Add(QueryPlanSourceSlot source)
        {
            if (!sources.Contains(source))
                sources.Add(source);
        }
    }
}
