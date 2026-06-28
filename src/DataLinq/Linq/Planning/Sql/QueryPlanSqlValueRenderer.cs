using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class QueryPlanSqlValueRenderer(
    IDataSourceAccess dataSource,
    QueryPlanSqlSourceMap sourceMap,
    QueryPlanBindingFrame bindings)
{
    public Operand RenderOperand(QueryPlanValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            QueryPlanColumnValue column => RenderColumnOperand(column),
            QueryPlanConstantValue constant => Operand.Value(constant.Value),
            QueryPlanCapturedValue captured => Operand.Value(GetScalarBinding(captured).Value),
            QueryPlanFunctionValue function => Operand.RawSql(RenderFunctionSql(function)),
            QueryPlanConvertedValue converted => RenderOperand(converted.Value),
            QueryPlanGroupKeyValue groupKey => Operand.RawSql(RenderSqlExpression(groupKey.Key)),
            QueryPlanGroupedAggregateValue groupedAggregate => Operand.RawSql(RenderGroupedAggregateSql(groupedAggregate)),
            QueryPlanLocalSequenceValue => throw new QueryTranslationException("Local sequence query plan values can only be rendered in IN predicates."),
            _ => throw new QueryTranslationException($"Query plan value '{value.Kind}' is not supported by SQL rendering.")
        };
    }

    public ColumnOperandWithDefinition RenderColumnOperand(QueryPlanColumnValue column)
    {
        ArgumentNullException.ThrowIfNull(column);

        var source = sourceMap.Get(column.Source);
        return Operand.Column(column.Column, source.Alias);
    }

    public string RenderSqlExpression(QueryPlanValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            QueryPlanColumnValue column => RenderColumnSql(column),
            QueryPlanFunctionValue function => RenderFunctionSql(function),
            QueryPlanConvertedValue converted => RenderSqlExpression(converted.Value),
            QueryPlanGroupKeyValue groupKey => RenderSqlExpression(groupKey.Key),
            QueryPlanGroupedAggregateValue groupedAggregate => RenderGroupedAggregateSql(groupedAggregate),
            _ => throw new QueryTranslationException($"Query plan value '{value.Kind}' cannot be rendered as a SQL expression.")
        };
    }

    public string RenderGroupedAggregateSql(QueryPlanGroupedAggregateValue aggregate)
    {
        if (aggregate.Aggregate == QueryPlanGroupedAggregateKind.Count)
            return "COUNT(*)";

        var selector = aggregate.Selector
            ?? throw new QueryTranslationException($"Grouped aggregate '{aggregate.Aggregate}' requires a selector.");
        var selectorSql = GetAggregateColumnExpression(selector);

        return aggregate.Aggregate switch
        {
            QueryPlanGroupedAggregateKind.Sum => $"COALESCE(SUM({selectorSql}), 0)",
            QueryPlanGroupedAggregateKind.Min => $"MIN({selectorSql})",
            QueryPlanGroupedAggregateKind.Max => $"MAX({selectorSql})",
            QueryPlanGroupedAggregateKind.Average => $"AVG({selectorSql})",
            _ => throw new QueryTranslationException($"Grouped aggregate '{aggregate.Aggregate}' is not supported by SQL rendering.")
        };
    }

    public string RenderColumnSql(QueryPlanColumnValue column)
    {
        ArgumentNullException.ThrowIfNull(column);

        var source = sourceMap.Get(column.Source);
        var escape = dataSource.Provider.Constants.EscapeCharacter;
        return $"{source.Alias}.{escape}{column.Column.DbName}{escape}";
    }

    public object? GetScalarValue(QueryPlanValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            QueryPlanConstantValue constant => constant.Value,
            QueryPlanCapturedValue captured => GetScalarBinding(captured).Value,
            QueryPlanConvertedValue converted => ConvertScalarValue(GetScalarValue(converted.Value), converted.TargetType),
            _ => throw new QueryTranslationException($"Query plan value '{value.Kind}' cannot be used as a scalar SQL value.")
        };
    }

    public object?[] GetLocalSequenceValues(QueryPlanLocalSequenceValue sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        var binding = GetBinding(sequence.BindingId);
        if (binding.Kind != QueryPlanBindingKind.LocalSequence)
            throw new QueryTranslationException($"Query plan binding '{sequence.BindingId}' is not a local sequence binding.");

        return binding.Values?.ToArray()
            ?? throw new QueryTranslationException($"Query plan local sequence binding '{sequence.BindingId}' has no values.");
    }

    public static (Operand left, Operand right) NormalizeValueOperandsForColumnTypes(Operand left, Operand right)
    {
        if (left is ColumnOperandWithDefinition leftColumn && right is ValueOperand rightValue)
            right = NormalizeValueOperandForColumnType(leftColumn.ColumnDefinition, rightValue);

        if (right is ColumnOperandWithDefinition rightColumn && left is ValueOperand leftValue)
            left = NormalizeValueOperandForColumnType(rightColumn.ColumnDefinition, leftValue);

        return (left, right);
    }

    public static object? NormalizeValueForColumnType(ColumnDefinition column, object? value)
    {
        var columnType = GetNonNullableColumnType(column);
        return columnType == typeof(char)
            ? NormalizeCharComparisonValue(value)
            : value;
    }

    private QueryPlanBinding GetScalarBinding(QueryPlanCapturedValue captured)
    {
        var binding = GetBinding(captured.BindingId);
        if (binding.Kind != QueryPlanBindingKind.Scalar)
            throw new QueryTranslationException($"Query plan binding '{captured.BindingId}' is not a scalar binding.");

        return binding;
    }

    private QueryPlanBinding GetBinding(string bindingId)
    {
        var matching = bindings.Bindings.Where(binding => binding.Id == bindingId).Take(2).ToArray();
        return matching.Length switch
        {
            1 => matching[0],
            0 => throw new QueryTranslationException($"Query plan binding '{bindingId}' is not available to SQL rendering."),
            _ => throw new QueryTranslationException($"Query plan binding '{bindingId}' is duplicated.")
        };
    }

    private string RenderFunctionSql(QueryPlanFunctionValue function)
    {
        return function.Function switch
        {
            QueryPlanFunctionKind.StringLength => RenderProviderFunction(function, SqlFunctionType.StringLength),
            QueryPlanFunctionKind.StringToUpper => RenderProviderFunction(function, SqlFunctionType.StringToUpper),
            QueryPlanFunctionKind.StringToLower => RenderProviderFunction(function, SqlFunctionType.StringToLower),
            QueryPlanFunctionKind.StringTrim => RenderProviderFunction(function, SqlFunctionType.StringTrim),
            QueryPlanFunctionKind.StringSubstring => RenderSubstringFunction(function),
            QueryPlanFunctionKind.DatePartYear => RenderProviderFunction(function, SqlFunctionType.DatePartYear),
            QueryPlanFunctionKind.DatePartMonth => RenderProviderFunction(function, SqlFunctionType.DatePartMonth),
            QueryPlanFunctionKind.DatePartDay => RenderProviderFunction(function, SqlFunctionType.DatePartDay),
            QueryPlanFunctionKind.DatePartDayOfYear => RenderProviderFunction(function, SqlFunctionType.DatePartDayOfYear),
            QueryPlanFunctionKind.DatePartDayOfWeek => RenderProviderFunction(function, SqlFunctionType.DatePartDayOfWeek),
            QueryPlanFunctionKind.TimePartHour => RenderProviderFunction(function, SqlFunctionType.TimePartHour),
            QueryPlanFunctionKind.TimePartMinute => RenderProviderFunction(function, SqlFunctionType.TimePartMinute),
            QueryPlanFunctionKind.TimePartSecond => RenderProviderFunction(function, SqlFunctionType.TimePartSecond),
            QueryPlanFunctionKind.TimePartMillisecond => RenderProviderFunction(function, SqlFunctionType.TimePartMillisecond),
            QueryPlanFunctionKind.StringStartsWith or
            QueryPlanFunctionKind.StringEndsWith or
            QueryPlanFunctionKind.StringContains or
            QueryPlanFunctionKind.StringIsNullOrEmpty or
            QueryPlanFunctionKind.StringIsNullOrWhiteSpace => throw new QueryTranslationException($"Boolean query plan function '{function.Function}' must be rendered as a predicate."),
            QueryPlanFunctionKind.ClientExpression => throw new QueryTranslationException("Client-expression query plan values cannot be rendered to SQL."),
            _ => throw new QueryTranslationException($"Query plan function '{function.Function}' is not supported by SQL rendering.")
        };
    }

    private string RenderProviderFunction(QueryPlanFunctionValue function, SqlFunctionType functionType)
    {
        if (function.Arguments.Count != 1)
            throw new QueryTranslationException($"Query plan function '{function.Function}' expects one SQL argument.");

        var sqlArgument = RenderProviderFunctionArgument(function.Arguments[0]);
        return dataSource.Provider.GetSqlForFunction(functionType, sqlArgument, null);
    }

    private string RenderSubstringFunction(QueryPlanFunctionValue function)
    {
        if (function.Arguments.Count != 3)
            throw new QueryTranslationException("Query plan StringSubstring function expects source, start index, and length arguments.");

        var sqlArgument = RenderProviderFunctionArgument(function.Arguments[0]);
        var startIndex = Convert.ToInt32(GetScalarValue(function.Arguments[1]), CultureInfo.InvariantCulture) + 1;
        var length = Convert.ToInt32(GetScalarValue(function.Arguments[2]), CultureInfo.InvariantCulture);
        return dataSource.Provider.GetSqlForFunction(SqlFunctionType.StringSubstring, sqlArgument, [startIndex, length]);
    }

    private string RenderProviderFunctionArgument(QueryPlanValue value)
    {
        return value switch
        {
            QueryPlanColumnValue column => column.Column.DbName,
            QueryPlanFunctionValue function => RenderFunctionSql(function),
            QueryPlanConvertedValue converted => RenderProviderFunctionArgument(converted.Value),
            _ => throw new QueryTranslationException($"Query plan value '{value.Kind}' cannot be used as a SQL function source.")
        };
    }

    private static ValueOperand NormalizeValueOperandForColumnType(ColumnDefinition column, ValueOperand valueOperand)
        => Operand.Value(valueOperand.Values.Select(value => NormalizeValueForColumnType(column, value)).ToArray());

    private static Type GetNonNullableColumnType(ColumnDefinition column)
    {
        var columnType = column.ValueProperty.CsType.Type
            ?? throw new QueryTranslationException($"Column '{column.DbName}' has no CLR type metadata.");

        return Nullable.GetUnderlyingType(columnType) ?? columnType;
    }

    private static object? NormalizeCharComparisonValue(object? value) => value switch
    {
        int intValue when intValue >= char.MinValue && intValue <= char.MaxValue => (char)intValue,
        string stringValue when stringValue.Length == 1 => stringValue[0],
        _ => value
    };

    private string GetAggregateColumnExpression(QueryPlanValue selector)
    {
        var unwrapped = selector is QueryPlanConvertedValue converted
            ? converted.Value
            : selector;

        if (unwrapped is not QueryPlanColumnValue column)
        {
            throw new QueryTranslationException(
                $"Query plan aggregate selector '{unwrapped.Kind}' is not supported. " +
                "Only direct numeric source-slot columns are supported.");
        }

        if (!IsNumericType(unwrapped.ClrType))
        {
            throw new QueryTranslationException(
                $"Query plan aggregate selector column '{column.Column.DbName}' must be numeric. " +
                $"Selector type: {unwrapped.ClrType}");
        }

        return RenderColumnSql(column);
    }

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
            return false;

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte or
            TypeCode.SByte or
            TypeCode.Int16 or
            TypeCode.UInt16 or
            TypeCode.Int32 or
            TypeCode.UInt32 or
            TypeCode.Int64 or
            TypeCode.UInt64 or
            TypeCode.Single or
            TypeCode.Double or
            TypeCode.Decimal => true,
            _ => false
        };
    }

    private static object? ConvertScalarValue(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return nonNullableTarget.IsInstanceOfType(value)
            ? value
            : Convert.ChangeType(value, nonNullableTarget, CultureInfo.InvariantCulture);
    }
}
