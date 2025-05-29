using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Query;
using DataLinq.Utils;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace DataLinq.Linq.Visitors;

internal class WhereVisitor<T> : ExpressionVisitor
{
    protected SqlQuery<T> query;
    NonNegativeInt negations = new NonNegativeInt(0);
    NonNegativeInt ors = new NonNegativeInt(0);
    Stack<WhereGroup<T>> whereGroups = new Stack<WhereGroup<T>>();

    internal WhereVisitor(SqlQuery<T> query)
    {
        this.query = query;
    }

    internal void Parse(WhereClause whereClause)
    {
        Visit(whereClause.Predicate);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        return base.VisitConstant(node);
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        return base.VisitConditional(node);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
            negations.Increment();

        if (node.NodeType == ExpressionType.Convert)
        {
            Expression currentOperand = node.Operand;
            while (currentOperand is UnaryExpression unaryOperand && unaryOperand.NodeType == ExpressionType.Convert)
            {
                currentOperand = unaryOperand.Operand;
            }
            return Visit(currentOperand);
        }
        return base.VisitUnary(node);
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node.CanReduce)
        {
            return Visit(node.Reduce());
        }

        if (node is QuerySourceReferenceExpression querySourceRef)
        {
            return querySourceRef;
        }

        if (node is SubQueryExpression subQuery)
        {
            var currentGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
            var currentBooleanType = ors.Value > 0 ? BooleanType.Or : BooleanType.And;
            if (ors.Value > 0) ors.Decrement();

            var collectionExpr = Visit(subQuery.QueryModel.MainFromClause.FromExpression)!;
            var collectionValue = query.GetValue(collectionExpr);
            var listToSearch = ConvertToList(collectionValue);

            foreach (var resultOperator in subQuery.QueryModel.ResultOperators)
            {
                if (resultOperator is ContainsResultOperator containsResultOperator)
                {
                    var itemToFindInListExpression = Visit(containsResultOperator.Item)!;

                    if (listToSearch.Length == 0)
                    {
                        var effectiveRelation = negations.Value > 0 ? Relation.AlwaysTrue : Relation.AlwaysFalse;
                        if (negations.Value > 0) negations.Decrement();
                        currentGroup.AddFixedCondition(effectiveRelation, currentBooleanType);
                    }
                    else
                    {
                        if (itemToFindInListExpression is MemberExpression memberOuter &&
                            memberOuter.Expression is QuerySourceReferenceExpression /* qsr && qsr.ReferencedQuerySource == queryModel_MainFromClause_For_OuterContext() */ )
                        {
                            var fieldName = query.GetColumn(memberOuter)?.DbName ?? memberOuter.Member.Name;
                            var whereClause = currentGroup.AddWhere(fieldName, null, currentBooleanType);
                            if (negations.Value > 0)
                            {
                                negations.Decrement();
                                whereClause.NotIn(listToSearch);
                            }
                            else
                            {
                                whereClause.In(listToSearch);
                            }
                        }
                        else
                        {
                            throw new NotImplementedException($"Contains operator's item expression '{itemToFindInListExpression}' is not a direct member access on the outer query source.");
                        }
                    }
                    return node;
                }
                else if (resultOperator is AnyResultOperator) // Corrected: Was ExistsResultOperator
                {
                    if (listToSearch.Length == 0)
                    {
                        var effectiveRelation = negations.Value > 0 ? Relation.AlwaysTrue : Relation.AlwaysFalse;
                        if (negations.Value > 0) negations.Decrement();
                        currentGroup.AddFixedCondition(effectiveRelation, currentBooleanType);
                        return node;
                    }
                    else
                    {
                        if (subQuery.QueryModel.BodyClauses.Count == 1 &&
                            subQuery.QueryModel.BodyClauses[0] is WhereClause subWhereClause &&
                            subWhereClause.Predicate is BinaryExpression binary && binary.NodeType == ExpressionType.Equal)
                        {
                            Expression? outerQueryMemberAccess = null;

                            if (IsOuterQueryMember(binary.Left, subQuery.QueryModel.MainFromClause) && IsSubQueryParameter(binary.Right, subQuery.QueryModel.MainFromClause))
                            {
                                outerQueryMemberAccess = binary.Left;
                            }
                            else if (IsOuterQueryMember(binary.Right, subQuery.QueryModel.MainFromClause) && IsSubQueryParameter(binary.Left, subQuery.QueryModel.MainFromClause))
                            {
                                outerQueryMemberAccess = binary.Right;
                            }

                            if (outerQueryMemberAccess is MemberExpression memberOuter)
                            {
                                var fieldName = query.GetColumn(memberOuter)?.DbName ?? memberOuter.Member.Name;
                                var whereClause = currentGroup.AddWhere(fieldName, null, currentBooleanType);

                                if (negations.Value > 0)
                                {
                                    negations.Decrement();
                                    whereClause.NotIn(listToSearch);
                                }
                                else
                                {
                                    whereClause.In(listToSearch);
                                }
                                return node;
                            }
                        }
                        throw new NotImplementedException($"Translation for this form of 'Any(predicate)' with a non-empty list (resulting in AnyResultOperator) is not implemented. SubQuery Predicate: {subQuery.QueryModel.BodyClauses.FirstOrDefault()}");
                    }
                }
                else
                    throw new NotImplementedException($"Result operator '{resultOperator}' within SubQuery is not implemented.");
            }
            return node;
        }

        return base.VisitExtension(node);
    }

    private bool IsOuterQueryMember(Expression expr, MainFromClause subQueryFromClause)
    {
        if (expr is MemberExpression me && me.Expression is QuerySourceReferenceExpression qsr)
        {
            return qsr.ReferencedQuerySource != subQueryFromClause;
        }
        return false;
    }

    private bool IsSubQueryParameter(Expression expr, MainFromClause subQueryFromClause)
    {
        if (expr is ParameterExpression pe)
        {
            return pe.Name == subQueryFromClause.ItemName && pe.Type == subQueryFromClause.ItemType;
        }
        if (expr is QuerySourceReferenceExpression qsr)
        {
            return qsr.ReferencedQuerySource == subQueryFromClause;
        }
        return false;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Member.Name == "Value" && node.Expression != null && Nullable.GetUnderlyingType(node.Expression.Type) != null)
        {
            if (node.Expression is MemberExpression innerMember && innerMember.Expression is QuerySourceReferenceExpression)
            {
                return Visit(node.Expression);
            }
        }
        return base.VisitMember(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var currentGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        var currentBooleanType = ors.Value > 0 ? BooleanType.Or : BooleanType.And;
        if (ors.Value > 0) ors.Decrement();

        if ((node.Method.Name == "StartsWith" || node.Method.Name == "EndsWith" || (node.Method.Name == "Contains" && node.Object?.Type == typeof(string)))
            && node.Object is MemberExpression memberObject && memberObject.Expression is QuerySourceReferenceExpression
            && node.Arguments.Count == 1)
        {
            var field = query.GetColumn(memberObject)?.DbName ?? memberObject.Member.Name;
            var valueArgument = Visit(node.Arguments[0])!;
            var value = query.GetValue(valueArgument);

            AddWhereToGroup(currentGroup, currentBooleanType, GetOperationForMethodName(node.Method.Name), field, value);
            return node;
        }
        else if (node.Method.IsGenericMethod &&
                 node.Method.GetGenericMethodDefinition().DeclaringType == typeof(Enumerable) &&
                 node.Method.Name == "Any" && node.Arguments.Count == 1)
        {
            var collectionExpr = Visit(node.Arguments[0])!;
            var collectionValue = query.GetValue(collectionExpr);
            var listToSearch = ConvertToList(collectionValue);

            Relation effectiveRelation;
            if (listToSearch.Length > 0)
                effectiveRelation = negations.Value > 0 ? Relation.AlwaysFalse : Relation.AlwaysTrue;
            else
                effectiveRelation = negations.Value > 0 ? Relation.AlwaysTrue : Relation.AlwaysFalse;

            if (negations.Value > 0) negations.Decrement();
            currentGroup.AddFixedCondition(effectiveRelation, currentBooleanType);
            return node;
        }

        throw new NotImplementedException($"Direct translation of method '{node.Method.Name}' with these arguments is not implemented. Expression: {node}");
    }


    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse)
        {
            bool isOuterOr = ors.Value > 0;
            if (isOuterOr) ors.Decrement();

            bool isOuterNot = negations.Value > 0;
            if (isOuterNot) negations.Decrement();

            var newGroup = isOuterNot
                ? query.AddWhereNotGroup(isOuterOr ? BooleanType.Or : BooleanType.And)
                : query.AddWhereGroup(isOuterOr ? BooleanType.Or : BooleanType.And);

            whereGroups.Push(newGroup);
            Visit(node.Left);

            if (node.NodeType == ExpressionType.OrElse)
                ors.Increment();

            Visit(node.Right);
            whereGroups.Pop();

            return node;
        }

        Expression left = Visit(node.Left)!;
        Expression right = Visit(node.Right)!;
        var fields = query.GetFields(left, right);

        var currentGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        var currentBooleanType = ors.Value > 0 ? BooleanType.Or : BooleanType.And;
        if (ors.Value > 0) ors.Decrement();

        if (node.NodeType == ExpressionType.NotEqual &&
            fields.Value is bool bVal && bVal == true &&
            (left as MemberExpression ?? right as MemberExpression)?.Type.IsNullableTypeWhereVisitor() == true) // Corrected: Using local extension
        {
            // Create a new group for the (IS NULL OR = false) logic
            var orGroupForNotTrue = new WhereGroup<T>(query);
            orGroupForNotTrue.AddWhere(fields.Key, null, BooleanType.And).EqualToNull(); // First part of the OR
            orGroupForNotTrue.AddWhere(fields.Key, null, BooleanType.Or).EqualTo(false);  // Second part of the OR

            // Add this newly created group to the current logical group
            currentGroup.AddWhereGroup(orGroupForNotTrue, currentBooleanType);
        }
        else
        {
            AddWhereToGroup(currentGroup, currentBooleanType, GetOperationForExpressionType(node.NodeType), fields.Key, fields.Value);
        }
        return node;
    }

    private void AddWhereToGroup(WhereGroup<T> group, BooleanType type, Operation operation, string field, params object?[] values)
    {
        var where = negations.Value > 0
            ? group.AddWhereNot(field, null, type)
            : group.AddWhere(field, null, type);

        if (negations.Value > 0) negations.Decrement();

        switch (operation)
        {
            case Operation.Equal: where.EqualTo(values[0]); break;
            case Operation.EqualNull: where.EqualToNull(); break;
            case Operation.NotEqual: where.NotEqualTo(values[0]); break;
            case Operation.NotEqualNull: where.NotEqualToNull(); break;
            case Operation.GreaterThan: where.GreaterThan(values[0]); break;
            case Operation.GreaterThanOrEqual: where.GreaterThanOrEqual(values[0]); break;
            case Operation.LessThan: where.LessThan(values[0]); break;
            case Operation.LessThanOrEqual: where.LessThanOrEqual(values[0]); break;
            case Operation.StartsWith: where.Like(values[0] + "%"); break;
            case Operation.EndsWith: where.Like("%" + values[0]); break;
            case Operation.StringContains: where.Like("%" + values[0] + "%"); break;
            case Operation.ListContains: where.In(values); break;
            default: throw new NotImplementedException($"Operation '{operation}' in AddWhereToGroup is not implemented.");
        }
    }

    private static Operation GetOperationForExpressionType(ExpressionType expressionType) => expressionType switch
    {
        ExpressionType.Equal => Operation.Equal,
        ExpressionType.NotEqual => Operation.NotEqual,
        ExpressionType.GreaterThan => Operation.GreaterThan,
        ExpressionType.GreaterThanOrEqual => Operation.GreaterThanOrEqual,
        ExpressionType.LessThan => Operation.LessThan,
        ExpressionType.LessThanOrEqual => Operation.LessThanOrEqual,
        _ => throw new NotImplementedException($"ExpressionType '{expressionType}' cannot be mapped to an Operation."),
    };

    private static Operation GetOperationForMethodName(string methodName) => methodName switch
    {
        "Contains" => Operation.StringContains,
        "StartsWith" => Operation.StartsWith,
        "EndsWith" => Operation.EndsWith,
        _ => throw new NotImplementedException($"Method name '{methodName}' cannot be mapped to an Operation."),
    };

    private enum Operation
    {
        Equal, EqualNull, NotEqual, NotEqualNull,
        GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
        StartsWith, EndsWith, StringContains, ListContains
    }

    private static object[] ConvertToList(object? obj)
    {
        return obj switch
        {
            null => [],
            object[] arr => arr,
            IEnumerable<object> enumerable => enumerable.ToArray(),
            IEnumerable enumerable => enumerable.Cast<object>().ToArray(),
            _ => throw new ArgumentException("Object is not a list or IEnumerable."),
        };
    }
}

// Renamed to avoid conflict if TypeExtensions exists elsewhere
internal static class TypeExtensionsWhereVisitor
{
    public static bool IsNullableTypeWhereVisitor(this Type type) // Renamed method
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }
}