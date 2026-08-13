using System;
using System.Collections.Generic;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Linq.Planning.Sql;

/// <summary>
/// Owns model-to-canonical normalization and preserves the target column for later physical SQL encoding.
/// </summary>
internal static class QueryPlanSqlColumnValueNormalizer
{
    private const string ComparisonSource = "query-plan:comparison";
    private const string LocalSequenceSource = "query-plan:local-sequence";

    public static (Operand Left, Operand Right) NormalizeComparisonOperands(
        QueryPlanComparisonOperator comparisonOperator,
        Operand left,
        Operand right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        RejectUnsupportedConvertedOrdering(comparisonOperator, left, right);

        if (left is ColumnOperandWithDefinition leftColumn && right is ValueOperand rightValue)
            right = NormalizeValueOperand(leftColumn.ColumnDefinition, rightValue, ComparisonSource);

        if (right is ColumnOperandWithDefinition rightColumn && left is ValueOperand leftValue)
            left = NormalizeValueOperand(rightColumn.ColumnDefinition, leftValue, ComparisonSource);

        return (left, right);
    }

    public static ValueOperand NormalizeLocalSequenceValues(
        ColumnDefinition column,
        IReadOnlyList<object?> sourceValues)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(sourceValues);

        var values = new object?[sourceValues.Count];
        for (var index = 0; index < sourceValues.Count; index++)
            values[index] = NormalizeValue(column, sourceValues[index], LocalSequenceSource);

        return new CanonicalColumnValueOperand(column, values);
    }

    private static ValueOperand NormalizeValueOperand(
        ColumnDefinition column,
        ValueOperand valueOperand,
        string sourceName)
    {
        var sourceValues = valueOperand.Values;
        var values = new object?[sourceValues.Length];
        for (var index = 0; index < sourceValues.Length; index++)
            values[index] = NormalizeValue(column, sourceValues[index], sourceName);

        return new CanonicalColumnValueOperand(column, values);
    }

    private static object? NormalizeValue(
        ColumnDefinition column,
        object? modelValue,
        string sourceName)
    {
        var normalizedModelValue = NormalizeModelValue(column, modelValue);
        return column.HasScalarConverter
            ? ModelValueConverter.ToCanonicalProviderValue(column, normalizedModelValue, sourceName)
            : normalizedModelValue;
    }

    private static object? NormalizeModelValue(ColumnDefinition column, object? value)
    {
        var columnType = column.ModelClrType
            ?? throw new QueryTranslationException($"Column '{column.DbName}' has no model CLR type metadata.");
        columnType = Nullable.GetUnderlyingType(columnType) ?? columnType;

        return columnType == typeof(char)
            ? NormalizeCharComparisonValue(value)
            : value;
    }

    private static object? NormalizeCharComparisonValue(object? value) => value switch
    {
        int intValue when intValue >= char.MinValue && intValue <= char.MaxValue => (char)intValue,
        string stringValue when stringValue.Length == 1 => stringValue[0],
        _ => value
    };

    private static void RejectUnsupportedConvertedOrdering(
        QueryPlanComparisonOperator comparisonOperator,
        Operand left,
        Operand right)
    {
        if (comparisonOperator is QueryPlanComparisonOperator.Equal or QueryPlanComparisonOperator.NotEqual)
            return;

        var convertedColumn = left is ColumnOperandWithDefinition { ColumnDefinition.HasScalarConverter: true } leftColumn
            ? leftColumn.ColumnDefinition
            : right is ColumnOperandWithDefinition { ColumnDefinition.HasScalarConverter: true } rightColumn
                ? rightColumn.ColumnDefinition
                : null;

        if (convertedColumn is null)
            return;

        throw new QueryTranslationException(
            $"Ordered comparison '{comparisonOperator}' is not supported for converter-backed column " +
            $"'{convertedColumn.Table.DbName}.{convertedColumn.DbName}'. Scalar converters do not declare " +
            "whether they preserve ordering; use direct equality or local membership instead.");
    }
}
