using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Query;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace DataLinq.Linq.Visitors;

internal class WhereVisitor<T> : ExpressionVisitor
{
    protected QueryBuilder<T> builder;

    internal WhereVisitor(QueryBuilder<T> queryBuilder)
    {
        this.builder = queryBuilder;
    }

    internal void Parse(WhereClause whereClause)
    {
        Visit(whereClause.Predicate);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        // Handle constant boolean expressions so they are not ignored (e.g. reduced Contains / Any)
        if (node.Type == typeof(bool) && node.Value is bool b)
        {
            var connectionType = builder.GetNextConnectionType();
            builder.CurrentParentGroup.AddFixedCondition(b ? Operator.AlwaysTrue : Operator.AlwaysFalse, connectionType);
            return node; // Fully handled
        }
        return base.VisitConstant(node);
    }

    protected override Expression VisitConditional(ConditionalExpression node) => base.VisitConditional(node);

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            builder.IncrementNegations(); // Mark that the next logical unit is negated
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
            //var currentParentGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
            // Determine connection type using the refined logic
            BooleanType connectionType;
            if (builder.CurrentParentGroup.Length == 0)
            {
                connectionType = BooleanType.And;
            }
            else
            {
                if (builder.ORs > 0)
                {
                    connectionType = BooleanType.Or;
                    builder.DecrementORs();
                }
                else
                {
                    connectionType = builder.CurrentParentGroup.InternalJoinType;
                }
            }

            // Evaluate the collection part of the subquery (e.g., the list in list.Contains(x.Prop))
            // This is done ONCE for the subquery.
            var collectionExpr = Visit(subQuery.QueryModel.MainFromClause.FromExpression)!;
            var collectionValue = builder.GetConstant(collectionExpr);
            var listToProcess = ConvertToList(collectionValue);

            bool isSubQueryGloballyNegated = builder.Negations > 0; // Negation applies to the whole subquery result
            if (isSubQueryGloballyNegated)
                builder.DecrementNegations();

            foreach (var resultOperator in subQuery.QueryModel.ResultOperators)
            {
                if (resultOperator is ContainsResultOperator containsResultOperator)
                {
                    // bool isSubQueryGloballyNegated was already determined above and applies to the whole Contains operation.

                    if (listToProcess.Length == 0)
                    {
                        // list.Contains(item) is FALSE. If negated (!list.Contains), it's TRUE.
                        var effectiveRelation = isSubQueryGloballyNegated ? Operator.AlwaysTrue : Operator.AlwaysFalse;
                        builder.CurrentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
                        // DO NOT VISIT containsResultOperator.Item if the list is empty.
                    }
                    else
                    {
                        // List is not empty, now it's safe to evaluate the item to find.
                        var itemToFindInListExpression = Visit(containsResultOperator.Item)!;

                        if (itemToFindInListExpression is MemberExpression memberOuter &&
                            memberOuter.Expression is QuerySourceReferenceExpression)
                        {
                            var fieldName = builder.GetColumnMaybe(memberOuter)?.DbName ?? memberOuter.Member.Name;
                            var whereClause = builder.CurrentParentGroup.AddWhere(fieldName, null, connectionType);
                            
                            if (isSubQueryGloballyNegated)
                                whereClause.NotIn(listToProcess);
                            else
                                whereClause.In(listToProcess);
                        }
                        else if (itemToFindInListExpression is ConstantExpression constantItem)
                        {
                            // Both collection and item are constants (no outer query member). Evaluate immediately.
                            object? itemValue = constantItem.Value;
                            bool found = listToProcess.Any(li => object.Equals(li, itemValue));
                            // If list DOES NOT contain item -> AlwaysFalse (unless negated)
                            // If list DOES contain item -> AlwaysTrue (unless negated)
                            Operator effectiveRelation = isSubQueryGloballyNegated
                                ? (found ? Operator.AlwaysFalse : Operator.AlwaysTrue)
                                : (found ? Operator.AlwaysTrue : Operator.AlwaysFalse);
                            builder.CurrentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
                            return node; // Short-circuit: fully evaluated
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
                        Operator effectiveRelation = isSubQueryGloballyNegated
                            ? listHasItems ? Operator.AlwaysFalse : Operator.AlwaysTrue
                            : listHasItems ? Operator.AlwaysTrue : Operator.AlwaysFalse;
                        builder.CurrentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
                        return node;
                    }

                    // .Any(predicate)
                    if (listToProcess.Length == 0)
                    {
                        var effectiveRelation = isSubQueryGloballyNegated ? Operator.AlwaysTrue : Operator.AlwaysFalse;
                        builder.CurrentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
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
                                    var fieldName = builder.GetColumnMaybe(finalOuterMemberAccess)?.DbName ?? finalOuterMemberAccess.Member.Name;
                                    var whereClause = builder.CurrentParentGroup.AddWhere(fieldName, null, connectionType);
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

    protected bool IsOuterQueryMember(Expression expr, MainFromClause subQueryFromClause)
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

    protected bool IsSubQueryParameter(Expression expr, MainFromClause subQueryFromClause)
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
        var connectionType = builder.GetNextConnectionType();

        // Handle static string.IsNullOrEmpty and string.IsNullOrWhiteSpace
        if (node.Method.IsStatic && node.Method.DeclaringType == typeof(string) && (node.Method.Name == "IsNullOrEmpty" || node.Method.Name == "IsNullOrWhiteSpace"))
        {
            var member = (MemberExpression)node.Arguments[0];
            var field = builder.GetColumn(member).DbName;

            builder.AddWhereToGroup(builder.CurrentParentGroup, connectionType, SqlOperation.GetOperationForMethodName(node.Method.Name), field, []);
        }
        // Handle string methods like StartsWith, EndsWith, Contains
        else if ((node.Method.Name == "StartsWith" || node.Method.Name == "EndsWith" || (node.Method.Name == "Contains" && node.Object?.Type == typeof(string)))
            && node.Object is MemberExpression memberObject && memberObject.Expression is QuerySourceReferenceExpression
            && node.Arguments.Count == 1)
        {
            var field = builder.GetColumnMaybe(memberObject)?.DbName ?? memberObject.Member.Name;
            var valueArgument = Visit(node.Arguments[0])!;
            var value = builder.GetConstant(valueArgument);
            builder.AddWhereToGroup(builder.CurrentParentGroup, connectionType, SqlOperation.GetOperationForMethodName(node.Method.Name), field, value);
        }
        else if (node.Method.Name == "Contains" && node.Arguments.Count == 2 && node.Object == null)
        {
            var collectionExpr = Visit(node.Arguments[0])!;
            var collectionValue = builder.GetConstant(collectionExpr);
            var listToProcess = ConvertToList(collectionValue);

            bool isCallNegated = builder.Negations > 0;
            if (isCallNegated)
                builder.DecrementNegations();

            Expression itemExpr = node.Arguments[1];
            if (itemExpr is UnaryExpression u && u.NodeType == ExpressionType.Convert)
                itemExpr = u.Operand;

            if (listToProcess.Length == 0)
            {
                var effectiveRelation = isCallNegated ? Operator.AlwaysTrue : Operator.AlwaysFalse;
                builder.CurrentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
            }
            else if (itemExpr is MemberExpression member && member.Expression is QuerySourceReferenceExpression)
            {
                var fieldName = builder.GetColumnMaybe(member)?.DbName ?? member.Member.Name;
                var whereClause = builder.CurrentParentGroup.AddWhere(fieldName, null, connectionType);
                if (isCallNegated)
                    whereClause.NotIn(listToProcess);
                else
                    whereClause.In(listToProcess);
            }
            else if (itemExpr is ConstantExpression constItem)
            {
                // Contains on fully constant collection & item -> evaluate immediately
                bool found = listToProcess.Any(li => object.Equals(li, constItem.Value));
                Operator effectiveRelation = isCallNegated
                    ? (found ? Operator.AlwaysFalse : Operator.AlwaysTrue)
                    : (found ? Operator.AlwaysTrue : Operator.AlwaysFalse);
                builder.CurrentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
                return node; // Short-circuit
            }
            else
            {
                throw new NotImplementedException($"Contains item expression '{itemExpr}' is not supported. Expected member access on query source or constant.");
            }
        }
        // Gracefully handle op_Implicit wrapping an array/span; just visit its single argument
        else if (node.Method.Name == "op_Implicit" && node.Arguments.Count == 1)
        {
            return Visit(node.Arguments[0]);
        }
        // Handle Enumerable.Any<TSource>(IEnumerable<TSource>) (without predicate)
        else if (node.Method.IsGenericMethod &&
                 node.Method.GetGenericMethodDefinition().DeclaringType == typeof(Enumerable) &&
                 node.Method.Name == "Any" && node.Arguments.Count == 1)
        {
            var collectionExpr = Visit(node.Arguments[0])!;
            var collectionValue = builder.GetConstant(collectionExpr);
            var listToProcess = ConvertToList(collectionValue);

            bool isCallNegated = builder.Negations > 0;
            if (isCallNegated)
                builder.DecrementNegations();
            
            Operator effectiveRelation = listToProcess.Length > 0
                ? isCallNegated ? Operator.AlwaysFalse : Operator.AlwaysTrue // collection.Any() is TRUE if list has items
                : isCallNegated ? Operator.AlwaysTrue : Operator.AlwaysFalse; // collection.Any() is FALSE if list is empty

            builder.CurrentParentGroup.AddFixedCondition(effectiveRelation, connectionType);
        }
        else
            throw new NotImplementedException($"Direct translation of method '{node.Method.Name}' with these arguments is not implemented. Expression: {node}");

        return node;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        // WhereVisitor.VisitBinary for AndAlso/OrElse
        if (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse)
        {
            var newGroupForThisOperation = builder.AddNewSubGroup(node);

            builder.PushWhereGroup(newGroupForThisOperation);

            Visit(node.Left); // First child added to newGroupForThisOperation

            if (node.NodeType == ExpressionType.OrElse)
            {
                builder.IncrementORs(); // Signal that node.Right should be OR'd
            }

            Visit(node.Right); // Second child, will check 'ors'

            // 'ors' should be consumed by the Visit(node.Right) if it was a simple/method/subquery
            // or by the connectionToOuterFlow logic if node.Right was another compound.
            // If 'ors' was incremented, ensure it's correctly decremented by the consumer.
            // This implicit consumption is what the next section relies on.

            builder.PopWhereGroup();
            return node;
        }

        // For all other binary expressions, they must be comparisons. Let the builder handle them.
        var comparison = builder.ParseComparison(node);
        if (comparison != null)
        {
            builder.AddComparison(comparison);
        }
        else // Fallback for Value-vs-Value
        {
            var leftVal = builder.GetConstant(node.Left);
            var rightVal = builder.GetConstant(node.Right);
            bool result = (bool)Expression.Lambda(Expression.MakeBinary(node.NodeType, Expression.Constant(leftVal), Expression.Constant(rightVal))).Compile().DynamicInvoke();
            builder.CurrentParentGroup.AddFixedCondition(result ? Operator.AlwaysTrue : Operator.AlwaysFalse, builder.GetNextConnectionType());
        }

        return node;
    }

    protected static object[] ConvertToList(object? obj)
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