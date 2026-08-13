using System;
using System.Collections.Generic;
using System.Globalization;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class QueryPlanSqlValueRenderer(
    IDataSourceAccess dataSource,
    QueryPlanSqlSourceMap sourceMap,
    QueryPlanBindingValues bindingValues,
    QueryPlanDerivedColumnMap? derivedColumns = null)
{
    public Operand RenderOperand(QueryPlanValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            QueryPlanColumnValue column => RenderColumnOperand(column),
            QueryPlanIntrinsicValue intrinsic => Operand.Value(GetIntrinsicValue(intrinsic)),
            QueryPlanScalarBindingReference scalar => Operand.Value(GetScalarBinding(scalar).Value),
            QueryPlanFunctionValue function => Operand.RawSql(RenderFunctionSql(function)),
            QueryPlanConvertedValue converted => RenderOperand(converted.Value),
            QueryPlanGroupKeyValue groupKey => Operand.RawSql(RenderSqlExpression(groupKey.Key)),
            QueryPlanGroupedAggregateValue groupedAggregate => Operand.RawSql(RenderGroupedAggregateSql(groupedAggregate)),
            QueryPlanLocalSequenceBindingReference => throw new QueryTranslationException("Local sequence binding references can only be rendered in IN predicates."),
            _ => throw new QueryTranslationException($"Query plan value '{value.Kind}' is not supported by SQL rendering.")
        };
    }

    public ColumnOperandWithDefinition RenderColumnOperand(QueryPlanColumnValue column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (derivedColumns is not null &&
            derivedColumns.TryGetAlias(column, out var derivedAlias))
        {
            return new ColumnOperandWithDefinition(column.Column, derivedAlias, derivedColumns.SourceAlias);
        }

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
        var selectorSql = GetAggregateColumnExpression(selector, aggregate.Aggregate.ToString());

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

        if (derivedColumns is not null &&
            derivedColumns.TryGetAlias(column, out var derivedAlias))
        {
            var derivedEscape = dataSource.Provider.Constants.EscapeCharacter;
            return $"{derivedColumns.SourceAlias}.{derivedEscape}{derivedAlias}{derivedEscape}";
        }

        var source = sourceMap.Get(column.Source);
        var escape = dataSource.Provider.Constants.EscapeCharacter;
        return $"{source.Alias}.{escape}{column.Column.DbName}{escape}";
    }

    public object? GetScalarValue(QueryPlanValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            QueryPlanIntrinsicValue intrinsic => GetIntrinsicValue(intrinsic),
            QueryPlanScalarBindingReference scalar => GetScalarBinding(scalar).Value,
            QueryPlanConvertedValue converted => ConvertScalarValue(GetScalarValue(converted.Value), converted.TargetType),
            _ => throw new QueryTranslationException($"Query plan value '{value.Kind}' cannot be used as a scalar SQL value.")
        };
    }

    public IReadOnlyList<object?> GetLocalSequenceValues(QueryPlanLocalSequenceBindingReference sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        var binding = GetBinding(sequence.BindingId);
        if (binding is not QueryPlanInvocationValue.LocalSequence localSequence)
            throw new QueryTranslationException($"Query plan binding '{sequence.BindingId}' is not a local sequence binding.");

        return localSequence.Values;
    }

    public (Operand Left, Operand Right) NormalizeComparisonOperands(
        QueryPlanComparisonOperator comparisonOperator,
        Operand left,
        Operand right) =>
        QueryPlanSqlColumnValueNormalizer.NormalizeComparisonOperands(comparisonOperator, left, right);

    public ValueOperand NormalizeLocalSequenceValues(
        ColumnDefinition column,
        IReadOnlyList<object?> sourceValues) =>
        QueryPlanSqlColumnValueNormalizer.NormalizeLocalSequenceValues(column, sourceValues);

    private QueryPlanInvocationValue.Scalar GetScalarBinding(QueryPlanScalarBindingReference scalar)
    {
        var binding = GetBinding(scalar.BindingId);
        if (binding is not QueryPlanInvocationValue.Scalar scalarValue)
            throw new QueryTranslationException($"Query plan binding '{scalar.BindingId}' is not a scalar binding.");

        return scalarValue;
    }

    private QueryPlanInvocationValue GetBinding(string bindingId)
    {
        if (bindingValues.TryGet(bindingId, out var binding))
            return binding;

        throw new QueryTranslationException($"Query plan binding '{bindingId}' is not available to SQL rendering.");
    }

    private static object? GetIntrinsicValue(QueryPlanIntrinsicValue intrinsic)
        => intrinsic.Intrinsic switch
        {
            QueryPlanIntrinsicKind.Null => null,
            QueryPlanIntrinsicKind.BooleanTrue => true,
            QueryPlanIntrinsicKind.BooleanFalse => false,
            _ => throw new QueryTranslationException($"Query plan intrinsic '{intrinsic.Intrinsic}' is not supported by SQL rendering.")
        };

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
            QueryPlanColumnValue column when derivedColumns is not null &&
                derivedColumns.TryGetAlias(column, out _) => RenderColumnSql(column),
            QueryPlanColumnValue column => RenderColumnSql(column),
            QueryPlanFunctionValue function => RenderFunctionSql(function),
            QueryPlanConvertedValue converted => RenderProviderFunctionArgument(converted.Value),
            _ => throw new QueryTranslationException($"Query plan value '{value.Kind}' cannot be used as a SQL function source.")
        };
    }

    private string GetAggregateColumnExpression(QueryPlanValue selector, string operatorName)
    {
        var column = QueryPlanAggregateSelectorValidator.RequireDirectNumericColumn(selector, operatorName);
        return RenderColumnSql(column);
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
