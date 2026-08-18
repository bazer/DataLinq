using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning.Expressions;

internal readonly record struct ExactPrimaryKeyTerminalMatch(
    TableDefinition Table,
    object? CanonicalProviderKey,
    QueryPlanResultKind ResultKind);

internal static class ExactPrimaryKeyTerminalQuery
{
    private const string ConversionSourceName = "linq:exact-primary-key-terminal";

    internal static bool TryMatch(
        DatabaseDefinition metadata,
        IQueryProvider provider,
        Expression expression,
        Type resultType,
        out ExactPrimaryKeyTerminalMatch match)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resultType);

        match = default;
        expression = UnwrapCompilerConversion(expression);
        if (expression is not MethodCallExpression methodCall ||
            !TryGetResultKind(methodCall, out var resultKind) ||
            methodCall.Arguments.Count != 2 ||
            !TryGetRoot(methodCall.Arguments[0], provider, resultType, out var root))
        {
            return false;
        }

        if (!metadata.TryGetTableModel(root.ElementType, out var tableModel))
            return false;

        var table = tableModel.Table;
        if (!ReferenceEquals(table.Database, metadata) ||
            table.PrimaryKeyColumns.Count != 1 ||
            !table.PrimaryKeyShape.SupportsScalarProviderKeyStore)
        {
            return false;
        }

        var predicate = UnwrapCompilerConversion(methodCall.Arguments[1]) as LambdaExpression;
        if (predicate is null || predicate.Parameters.Count != 1 || predicate.ReturnType != typeof(bool))
            return false;

        var comparison = UnwrapCompilerConversion(predicate.Body) as BinaryExpression;
        if (comparison is null ||
            comparison.NodeType != ExpressionType.Equal ||
            !IsSupportedEqualityOperator(comparison.Method))
        {
            return false;
        }

        var primaryKeyColumn = table.PrimaryKeyColumns[0];
        if (!TryGetValueExpression(
                comparison.Left,
                comparison.Right,
                predicate.Parameters[0],
                table,
                primaryKeyColumn,
                out var valueExpression) &&
            !TryGetValueExpression(
                comparison.Right,
                comparison.Left,
                predicate.Parameters[0],
                table,
                primaryKeyColumn,
                out valueExpression))
        {
            return false;
        }

        if (!IsSafeCapturedValue(valueExpression))
            return false;

        // Eligibility is completely established before evaluation. The captured value is read
        // exactly once, normalized through the same model/provider conversion boundary used by
        // generated keys, and is never retained by the provider.
        var modelValue = EvaluateSafeCapturedValue(valueExpression);
        var canonicalProviderKey = primaryKeyColumn.HasScalarConverter
            ? ModelValueConverter.ToCanonicalProviderValue(
                primaryKeyColumn,
                modelValue,
                ConversionSourceName)
            : modelValue;
        if (canonicalProviderKey is not null &&
            !table.PrimaryKeyShape.SupportsScalarProviderKey(canonicalProviderKey.GetType()))
        {
            return false;
        }

        match = new ExactPrimaryKeyTerminalMatch(table, canonicalProviderKey, resultKind);
        return true;
    }

    private static bool TryGetResultKind(
        MethodCallExpression methodCall,
        out QueryPlanResultKind resultKind)
    {
        resultKind = default;
        if (!methodCall.Method.IsGenericMethod ||
            methodCall.Method.DeclaringType != typeof(Queryable))
        {
            return false;
        }

        resultKind = methodCall.Method.Name switch
        {
            nameof(Queryable.Single) => QueryPlanResultKind.Single,
            nameof(Queryable.SingleOrDefault) => QueryPlanResultKind.SingleOrDefault,
            _ => default
        };

        return resultKind is QueryPlanResultKind.Single or QueryPlanResultKind.SingleOrDefault;
    }

    private static bool TryGetRoot(
        Expression expression,
        IQueryProvider provider,
        Type resultType,
        out IQueryable root)
    {
        expression = UnwrapCompilerConversion(expression);
        if (expression is ConstantExpression { Value: IQueryable queryable } &&
            ReferenceEquals(queryable.Provider, provider) &&
            queryable.ElementType == resultType &&
            UnwrapCompilerConversion(queryable.Expression) is ConstantExpression rootConstant &&
            ReferenceEquals(rootConstant.Value, queryable))
        {
            root = queryable;
            return true;
        }

        root = null!;
        return false;
    }

    private static bool TryGetValueExpression(
        Expression columnCandidate,
        Expression valueCandidate,
        ParameterExpression parameter,
        TableDefinition table,
        ColumnDefinition primaryKeyColumn,
        out Expression valueExpression)
    {
        if (columnCandidate is MemberExpression member &&
            ReferenceEquals(member.Expression, parameter) &&
            table.TryGetColumnByPropertyName(member.Member.Name, out var column) &&
            ReferenceEquals(column, primaryKeyColumn))
        {
            valueExpression = valueCandidate;
            return true;
        }

        valueExpression = null!;
        return false;
    }

    private static bool IsSafeCapturedValue(Expression expression)
    {
        expression = UnwrapCompilerConversion(expression);
        return expression switch
        {
            ConstantExpression => true,
            MemberExpression { Member: FieldInfo field, Expression: null } => field.IsStatic,
            MemberExpression { Member: FieldInfo, Expression: not null } member =>
                IsSafeCapturedValue(member.Expression),
            _ => false
        };
    }

    private static object? EvaluateSafeCapturedValue(Expression expression)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            return EvaluateSafeCapturedValue(quote.Operand);

        if (expression is UnaryExpression unary &&
            unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            return IsSupportedNullableLift(unary.Type, unary.Operand.Type)
                ? EvaluateSafeCapturedValue(unary.Operand)
                : ExpressionLocalValueEvaluator.Evaluate(unary);
        }

        return expression switch
        {
            ConstantExpression constant => constant.Value,
            MemberExpression { Member: FieldInfo field, Expression: null } => field.GetValue(null),
            MemberExpression { Member: FieldInfo field, Expression: not null } member =>
                field.GetValue(EvaluateSafeCapturedValue(member.Expression)),
            _ => ExpressionLocalValueEvaluator.Evaluate(expression)
        };
    }

    private static bool IsSupportedNullableLift(Type nullableType, Type valueType) =>
        (nullableType == typeof(int?) && valueType == typeof(int)) ||
        (nullableType == typeof(long?) && valueType == typeof(long)) ||
        (nullableType == typeof(Guid?) && valueType == typeof(Guid));

    private static bool IsSupportedEqualityOperator(MethodInfo? method)
    {
        if (method is null)
            return true;

        return method.IsStatic &&
            method.IsSpecialName &&
            method.Name == "op_Equality" &&
            method.ReturnType == typeof(bool) &&
            method.GetParameters().Length == 2;
    }

    private static Expression UnwrapCompilerConversion(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote &&
               (unary.Method is null ||
                ExpressionLocalConversionEvaluator.IsFrameworkNumericConversion(unary.Method)))
        {
            expression = unary.Operand;
        }

        return expression;
    }
}
