using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Exceptions;
using DataLinq.Query;
using DataLinq.Utils;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace DataLinq.Linq.Visitors;

internal class WhereVisitor<T> : ExpressionVisitor
{
    protected SqlQuery<T> query;
    private NonNegativeInt negations = new NonNegativeInt(0); // Tracks pending NOT operations
    private NonNegativeInt ors = new NonNegativeInt(0);       // Tracks if the next item should be ORed
    private Stack<WhereGroup<T>> whereGroups = new Stack<WhereGroup<T>>(); // Manages logical grouping (parentheses)

    internal WhereVisitor(SqlQuery<T> query)
    {
        this.query = query;
    }

    internal void Parse(WhereClause whereClause)
    {
        Visit(whereClause.Predicate);
    }

    protected override Expression VisitConstant(ConstantExpression node) => base.VisitConstant(node);
    protected override Expression VisitConditional(ConditionalExpression node) => base.VisitConditional(node);

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            negations.Increment(); // Mark that the next logical unit is negated
        }
        else if (node.NodeType == ExpressionType.Convert)
        {
            // Strip away convert operations to get to the underlying expression
            Expression currentOperand = node.Operand;
            while (currentOperand is UnaryExpression unaryOperand && unaryOperand.NodeType == ExpressionType.Convert)
            {
                currentOperand = unaryOperand.Operand;
            }
            return Visit(currentOperand);
        }
        return base.VisitUnary(node); // Visit operand for NOT or other unary ops
    }

    protected override Expression VisitExtension(Expression node)
    {
        if (node.CanReduce) return Visit(node.Reduce());
        if (node is QuerySourceReferenceExpression querySourceRef) return querySourceRef;

        if (node is SubQueryExpression subQuery)
        {
            // Determine how this subquery-based condition connects to the previous condition in the current group
            var currentParentGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
            // Determine connection type using the refined logic
            BooleanType connectionType;
            if (currentParentGroup.Length == 0)
            {
                connectionType = BooleanType.And;
            }
            else
            {
                if (ors.Value > 0)
                {
                    connectionType = BooleanType.Or;
                    ors.Decrement();
                }
                else
                {
                    connectionType = currentParentGroup.InternalJoinType;
                }
            }

            // Evaluate the collection part of the subquery (e.g., the list in list.Contains(x.Prop))
            // This is done ONCE for the subquery.
            var collectionExpr = Visit(subQuery.QueryModel.MainFromClause.FromExpression)!;
            var collectionValue = query.GetValue(collectionExpr);
            var listToProcess = ConvertToList(collectionValue);

            bool isSubQueryGloballyNegated = negations.Value > 0; // Negation applies to the whole subquery result
            if (isSubQueryGloballyNegated) negations.Decrement();

            foreach (var resultOperator in subQuery.QueryModel.ResultOperators)
            {
                if (resultOperator is ContainsResultOperator containsResultOperator)
                {
                    // bool isSubQueryGloballyNegated was already determined above and applies to the whole Contains operation.

                    if (listToProcess.Length == 0)
                    {
                        // list.Contains(item) is FALSE. If negated (!list.Contains), it's TRUE.
                        var effectiveRelation = isSubQueryGloballyNegated ? Relation.AlwaysTrue : Relation.AlwaysFalse;
                        currentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
                        // DO NOT VISIT containsResultOperator.Item if the list is empty.
                    }
                    else
                    {
                        // List is not empty, now it's safe to evaluate the item to find.
                        var itemToFindInListExpression = Visit(containsResultOperator.Item)!;

                        if (itemToFindInListExpression is MemberExpression memberOuter &&
                            memberOuter.Expression is QuerySourceReferenceExpression)
                        {
                            var fieldName = query.GetColumn(memberOuter)?.DbName ?? memberOuter.Member.Name;
                            var whereClause = currentParentGroup.AddWhere(fieldName, null, connectionType);
                            if (isSubQueryGloballyNegated) whereClause.NotIn(listToProcess);
                            else whereClause.In(listToProcess);
                        }
                        else if (itemToFindInListExpression is ConstantExpression constantItem)
                        {
                            object? itemValue = constantItem.Value;
                            bool found = false;
                            foreach (var listItem in listToProcess)
                            {
                                if (object.Equals(listItem, itemValue))
                                {
                                    found = true;
                                    break;
                                }
                            }
                            // If Contains is true (found) AND subquery is negated -> result is False (1=0)
                            // If Contains is true (found) AND subquery is NOT negated -> result is True (1=1)
                            // If Contains is false (not found) AND subquery is negated -> result is True (1=1)
                            // If Contains is false (not found) AND subquery is NOT negated -> result is False (1=0)
                            // This is equivalent to: (found XOR isSubQueryGloballyNegated) ? Relation.AlwaysTrue : Relation.AlwaysFalse,
                            // but simpler: (found == !isSubQueryGloballyNegated)
                            var effectiveRelation = (found ^ isSubQueryGloballyNegated) ? Relation.AlwaysFalse : Relation.AlwaysTrue;
                            if (isSubQueryGloballyNegated)
                            { // Corrected logic for direct boolean to Relation.
                                effectiveRelation = found ? Relation.AlwaysFalse : Relation.AlwaysTrue;
                            }
                            else
                            {
                                effectiveRelation = found ? Relation.AlwaysTrue : Relation.AlwaysFalse;
                            }

                            currentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
                        }
                        else
                        {
                            throw new NotImplementedException($"Contains operator's item expression '{itemToFindInListExpression}' resolved to an unhandled type. It should be a direct member access on an outer query source or a constant.");
                        }
                    }
                    return node; // Processed ContainsResultOperator
                }
                else if (resultOperator is AnyResultOperator)
                {
                    // bool isSubQueryGloballyNegated is already determined

                    if (subQuery.QueryModel.BodyClauses.Count == 0) // .Any() without predicate
                    {
                        bool listHasItems = listToProcess.Length > 0;
                        Relation effectiveRelation;
                        if (isSubQueryGloballyNegated)
                        {
                            effectiveRelation = listHasItems ? Relation.AlwaysFalse : Relation.AlwaysTrue;
                        }
                        else
                        {
                            effectiveRelation = listHasItems ? Relation.AlwaysTrue : Relation.AlwaysFalse;
                        }
                        currentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
                        return node;
                    }

                    // .Any(predicate)
                    if (listToProcess.Length == 0)
                    {
                        var effectiveRelation = isSubQueryGloballyNegated ? Relation.AlwaysTrue : Relation.AlwaysFalse;
                        currentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
                        return node;
                    }
                    else // listToProcess is not empty, and there's a predicate
                    {
                        if (subQuery.QueryModel.BodyClauses.Count == 1 &&
                            subQuery.QueryModel.BodyClauses[0] is WhereClause subWhereClause &&
                            subWhereClause.Predicate is BinaryExpression binary && binary.NodeType == ExpressionType.Equal)
                        {
                            Expression? outerQuerySide = null;
                            // Check which side of the binary.Equal is the outer member access
                            if (IsOuterQueryMember(binary.Left, subQuery.QueryModel.MainFromClause) &&
                                IsSubQueryParameter(binary.Right, subQuery.QueryModel.MainFromClause))
                            {
                                outerQuerySide = binary.Left;
                            }
                            else if (IsOuterQueryMember(binary.Right, subQuery.QueryModel.MainFromClause) &&
                                     IsSubQueryParameter(binary.Left, subQuery.QueryModel.MainFromClause))
                            {
                                outerQuerySide = binary.Right;
                            }

                            MemberExpression? finalOuterMemberAccess = null;
                            if (outerQuerySide is MemberExpression mo)
                            {
                                finalOuterMemberAccess = mo;
                            }
                            else if (outerQuerySide is UnaryExpression uo &&
                                     uo.NodeType == ExpressionType.Convert &&
                                     uo.Operand is MemberExpression umo)
                            {
                                // Ensure the conversion is to a compatible type if needed for stripping,
                                // but usually, we just want the underlying MemberExpression.
                                finalOuterMemberAccess = umo;
                            }

                            if (finalOuterMemberAccess != null)
                            {
                                // Ensure the MemberExpression is accessing a property of the outer query's source
                                if (finalOuterMemberAccess.Expression is QuerySourceReferenceExpression qsr &&
                                    qsr.ReferencedQuerySource != subQuery.QueryModel.MainFromClause)
                                {
                                    var fieldName = query.GetColumn(finalOuterMemberAccess)?.DbName ?? finalOuterMemberAccess.Member.Name;
                                    var whereClause = currentParentGroup.AddWhere(fieldName, null, connectionType);
                                    if (isSubQueryGloballyNegated) whereClause.NotIn(listToProcess);
                                    else whereClause.In(listToProcess);
                                    return node;
                                }
                            }
                        }
                        // If structure doesn't match, fall through to the exception
                        throw new NotImplementedException($"Translation for 'Any(predicate)' with a non-empty list and this predicate structure is not implemented. Predicate: {subQuery.QueryModel.BodyClauses.FirstOrDefault()}");
                    }
                }
                else throw new NotImplementedException($"Result operator '{resultOperator}' within SubQuery is not implemented.");
            }
            return node; // Should be unreachable if a result operator was present and handled.
                         // If no result operator, subQuery might be part of a JOIN or other structure.
        }
        return base.VisitExtension(node);
    }

    private bool IsOuterQueryMember(Expression expr, MainFromClause subQueryFromClause)
    {
        // Strip UnaryExpression (like Convert) if present
        if (expr is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            expr = unary.Operand;
        }

        if (expr is MemberExpression me && me.Expression is QuerySourceReferenceExpression qsr)
            return qsr.ReferencedQuerySource != subQueryFromClause;
        return false;
    }

    private bool IsSubQueryParameter(Expression expr, MainFromClause subQueryFromClause)
    {
        // Strip UnaryExpression (like Convert) if present
        if (expr is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            expr = unary.Operand;
        }

        if (expr is ParameterExpression pe)
            return pe.Name == subQueryFromClause.ItemName && pe.Type == subQueryFromClause.ItemType;

        // Sometimes the parameter from the subquery's MainFromClause is wrapped in a QuerySourceReferenceExpression
        // when used in the predicate of a WhereClause within that same subquery.
        if (expr is QuerySourceReferenceExpression qsr)
            return qsr.ReferencedQuerySource == subQueryFromClause;

        return false;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // Handles something like x.NullableInt.Value -> treats it as x.NullableInt
        if (node.Member.Name == "Value" && node.Expression != null && Nullable.GetUnderlyingType(node.Expression.Type) != null)
        {
            if (node.Expression is MemberExpression innerMember && innerMember.Expression is QuerySourceReferenceExpression)
            {
                return Visit(node.Expression); // Effectively "unwraps" .Value
            }
        }
        return base.VisitMember(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Determine how this method call condition connects to the previous one in the current group
        var currentParentGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        var connectionType = ors.Value > 0 ? BooleanType.Or : BooleanType.And; // Default to AND if not first in an OR sequence
        if (ors.Value > 0) ors.Decrement(); // Consume OR state

        // Handle string methods like StartsWith, EndsWith, Contains
        if ((node.Method.Name == "StartsWith" || node.Method.Name == "EndsWith" || (node.Method.Name == "Contains" && node.Object?.Type == typeof(string)))
            && node.Object is MemberExpression memberObject && memberObject.Expression is QuerySourceReferenceExpression
            && node.Arguments.Count == 1)
        {
            var field = query.GetColumn(memberObject)?.DbName ?? memberObject.Member.Name;
            var valueArgument = Visit(node.Arguments[0])!;
            var value = query.GetValue(valueArgument);

            AddWhereToGroup(currentParentGroup, connectionType, GetOperationForMethodName(node.Method.Name), field, value);
            return node;
        }
        // Handle Enumerable.Any<TSource>(IEnumerable<TSource>) (without predicate)
        else if (node.Method.IsGenericMethod &&
                 node.Method.GetGenericMethodDefinition().DeclaringType == typeof(Enumerable) &&
                 node.Method.Name == "Any" && node.Arguments.Count == 1)
        {
            var collectionExpr = Visit(node.Arguments[0])!;
            var collectionValue = query.GetValue(collectionExpr);
            var listToProcess = ConvertToList(collectionValue);

            bool isCallNegated = negations.Value > 0;
            if (isCallNegated) negations.Decrement();

            Relation effectiveRelation;
            if (listToProcess.Length > 0) // collection.Any() is TRUE if list has items
                effectiveRelation = isCallNegated ? Relation.AlwaysFalse : Relation.AlwaysTrue;
            else // collection.Any() is FALSE if list is empty
                effectiveRelation = isCallNegated ? Relation.AlwaysTrue : Relation.AlwaysFalse;

            currentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
            return node;
        }

        throw new NotImplementedException($"Direct translation of method '{node.Method.Name}' with these arguments is not implemented. Expression: {node}");
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        // Check for member-to-member or member-to-function before handling logical operators
        if (node.Left is MemberExpression leftMember && node.Right is MemberExpression rightMember)
        {
            if (leftMember.Expression is QuerySourceReferenceExpression && rightMember.Expression is QuerySourceReferenceExpression)
            {
                return HandleMemberToMemberComparison(node, leftMember, rightMember);
            }
            else if (leftMember.Expression is QuerySourceReferenceExpression)
            {
                return HandleMemberToFunctionComparison(node, leftMember, rightMember);
            }
            else if (rightMember.Expression is QuerySourceReferenceExpression)
            {
                return HandleMemberToFunctionComparison(node, rightMember, leftMember);
            }
        }

        // WhereVisitor.VisitBinary for AndAlso/OrElse
        if (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse)
        {
            var currentParentGroupForThisOperation = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();

            bool isThisOperationGroupNegated = negations.Value > 0;
            if (isThisOperationGroupNegated) negations.Decrement();

            BooleanType internalJoinTypeForThisOperationGroup = (node.NodeType == ExpressionType.OrElse) ? BooleanType.Or : BooleanType.And;
            var newGroupForThisOperation = new WhereGroup<T>(query, internalJoinTypeForThisOperationGroup, isThisOperationGroupNegated);

            BooleanType connectionToOuterFlow;
            if (currentParentGroupForThisOperation.Length == 0)
            {
                if (ors.Value > 0)
                { // Check 'ors' if this is the first child of its parent
                    connectionToOuterFlow = BooleanType.Or;
                    ors.Decrement();
                }
                else
                {
                    connectionToOuterFlow = BooleanType.And;
                }
            }
            else // Subsequent child in parent
            {
                if (ors.Value > 0)
                { // 'ors' for *this specific connection* takes precedence
                    connectionToOuterFlow = BooleanType.Or;
                    ors.Decrement();
                }
                else
                {
                    connectionToOuterFlow = currentParentGroupForThisOperation.InternalJoinType;
                }
            }
            currentParentGroupForThisOperation.AddSubGroup(newGroupForThisOperation, connectionToOuterFlow);

            whereGroups.Push(newGroupForThisOperation);

            Visit(node.Left); // First child added to newGroupForThisOperation

            if (node.NodeType == ExpressionType.OrElse)
            {
                ors.Increment(); // Signal that node.Right should be OR'd
            }

            Visit(node.Right); // Second child, will check 'ors'

            // 'ors' should be consumed by the Visit(node.Right) if it was a simple/method/subquery
            // or by the connectionToOuterFlow logic if node.Right was another compound.
            // If 'ors' was incremented, ensure it's correctly decremented by the consumer.
            // This implicit consumption is what the next section relies on.

            whereGroups.Pop();
            return node;
        }

        // For simple binary expressions (==, !=, >, < etc.)
        Expression left = Visit(node.Left)!;
        Expression right = Visit(node.Right)!;
        var fields = query.GetFields(left, right);

        // To be used in VisitBinary (simple ops), VisitMethodCall, VisitExtension (for subquery conditions)
        var currentActiveGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        BooleanType connectionTypeToUse;

        if (ors.Value > 0) // Higher priority: if an 'OR' is explicitly signaled for this connection
        {
            connectionTypeToUse = BooleanType.Or;
            ors.Decrement(); // Consume the 'ors' signal for this specific connection
        }
        else // No explicit 'OR' signaled, use group's logic
        {
            if (currentActiveGroup.Length == 0)
            {
                // First child in the current group.
                connectionTypeToUse = BooleanType.And;
            }
            else
            {
                // Subsequent child in currentActiveGroup, use its InternalJoinType.
                connectionTypeToUse = currentActiveGroup.InternalJoinType;
            }
        }

        if (node.NodeType == ExpressionType.NotEqual &&
            fields.Value is bool bVal && bVal == true &&
            (left as MemberExpression ?? right as MemberExpression)?.Type.IsNullableTypeWhereVisitor() == true)
        {
            bool isOuterNegationAppliedToGroup = negations.Value > 0;
            if (isOuterNegationAppliedToGroup) negations.Decrement();

            // Create a new group for (IS NULL OR = false), its internal join is OR.
            var orGroupForNotTrue = new WhereGroup<T>(query, BooleanType.Or, isOuterNegationAppliedToGroup);
            orGroupForNotTrue.AddWhere(fields.Key, null, BooleanType.And).EqualToNull(); // First in this sub-group
            orGroupForNotTrue.AddWhere(fields.Key, null, BooleanType.Or).EqualTo(false);   // Second, ORed to first

            currentActiveGroup.AddSubGroup(orGroupForNotTrue, connectionTypeToUse);
        }
        else
        {
            // AddWhereToGroup already handles negations for the simple condition itself.
            AddWhereToGroup(currentActiveGroup, connectionTypeToUse, GetOperationForExpressionType(node.NodeType), fields.Key, fields.Value);
        }
        return node;
    }

    private Expression HandleMemberToMemberComparison(BinaryExpression node, MemberExpression leftMember, MemberExpression rightMember)
    {
        var currentActiveGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        var connectionTypeToUse = (ors.Value > 0) ? BooleanType.Or : currentActiveGroup.InternalJoinType;
        if (ors.Value > 0) ors.Decrement();

        var leftColumnName = query.GetColumn(leftMember)?.DbName ?? leftMember.Member.Name;
        var rightColumnName = query.GetColumn(rightMember)?.DbName ?? rightMember.Member.Name;
        
        var where = currentActiveGroup.AddWhere(leftColumnName, null, connectionTypeToUse);

        switch (node.NodeType)
        {
            case ExpressionType.Equal:              where.EqualToColumn(rightColumnName); break;
            case ExpressionType.NotEqual:           where.NotEqualToColumn(rightColumnName); break;
            case ExpressionType.GreaterThan:        where.GreaterThanColumn(rightColumnName); break;
            case ExpressionType.GreaterThanOrEqual: where.GreaterThanOrEqualToColumn(rightColumnName); break;
            case ExpressionType.LessThan:           where.LessThanColumn(rightColumnName); break;
            case ExpressionType.LessThanOrEqual:    where.LessThanOrEqualToColumn(rightColumnName); break;
            default: throw new NotImplementedException($"Binary operator '{node.NodeType}' between two members is not supported.");
        }
        return node;
    }

    private Expression HandleMemberToFunctionComparison(BinaryExpression node, MemberExpression memberExpr, MemberExpression functionExpr)
    {
        if (functionExpr.Expression is not MemberExpression innerMember)
            throw new InvalidQueryException("Function call on a non-member expression is not supported.");

        var columnName = query.GetColumn(memberExpr)?.DbName ?? memberExpr.Member.Name;
        var functionOnColumnName = $"{query.EscapeCharacter}{query.GetColumn(innerMember)?.DbName ?? innerMember.Member.Name}{query.EscapeCharacter}";

        SqlFunctionType? functionType = null;
        if (innerMember.Type == typeof(DateOnly) || innerMember.Type == typeof(DateTime))
        {
            functionType = functionExpr.Member.Name switch
            {
                "Year" => SqlFunctionType.DatePartYear,
                "Month" => SqlFunctionType.DatePartMonth,
                "Day" => SqlFunctionType.DatePartDay,
                "DayOfYear" => SqlFunctionType.DatePartDayOfYear,
                "DayOfWeek" => SqlFunctionType.DatePartDayOfWeek,
                _ => null
            };
        }

        if (functionType == null && (innerMember.Type == typeof(TimeOnly) || innerMember.Type == typeof(DateTime)))
        {
            functionType = functionExpr.Member.Name switch
            {
                "Hour" => SqlFunctionType.TimePartHour,
                "Minute" => SqlFunctionType.TimePartMinute,
                "Second" => SqlFunctionType.TimePartSecond,
                "Millisecond" => SqlFunctionType.TimePartMillisecond,
                _ => null
            };
        }

        if (functionType == null)
            throw new NotImplementedException($"Function '{functionExpr.Member.Name}' on type '{innerMember.Type.Name}' is not supported.");

        string sqlFunction = query.DataSource.Provider.GetSqlForFunction(functionType.Value, functionOnColumnName);

        var currentActiveGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        var connectionTypeToUse = (ors.Value > 0) ? BooleanType.Or : currentActiveGroup.InternalJoinType;
        if (ors.Value > 0) ors.Decrement();

        var where = currentActiveGroup.AddWhere(columnName, null, connectionTypeToUse);

        switch (node.NodeType)
        {
            case ExpressionType.Equal: where.EqualToRaw(sqlFunction); break;
            case ExpressionType.NotEqual: where.NotEqualToRaw(sqlFunction); break;
            case ExpressionType.GreaterThan: where.GreaterThanRaw(sqlFunction); break;
            case ExpressionType.GreaterThanOrEqual: where.GreaterThanOrEqualToRaw(sqlFunction); break;
            case ExpressionType.LessThan: where.LessThanRaw(sqlFunction); break;
            case ExpressionType.LessThanOrEqual: where.LessThanOrEqualToRaw(sqlFunction); break;
            default: throw new NotImplementedException($"Binary operator '{node.NodeType}' between a member and a function is not supported.");
        }

        return node;
    }

    private void AddWhereToGroup(WhereGroup<T> group, BooleanType connectionType, Operation operation, string field, params object?[] values)
    {
        // 'negations' applies to the individual 'where' condition being added.
        bool isConditionNegated = negations.Value > 0;
        if (isConditionNegated) negations.Decrement();

        Where<T> where;
        if (isConditionNegated)
            where = group.AddWhereNot(field, null, connectionType);
        else
            where = group.AddWhere(field, null, connectionType);

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
            case Operation.ListContains: where.In(values); break; // This is for pre-evaluated list.Contains results
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

internal static class TypeExtensionsWhereVisitor
{
    public static bool IsNullableTypeWhereVisitor(this Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }
}