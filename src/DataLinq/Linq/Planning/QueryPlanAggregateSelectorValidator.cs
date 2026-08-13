using System;
using DataLinq.Exceptions;

namespace DataLinq.Linq.Planning;

internal static class QueryPlanAggregateSelectorValidator
{
    public static void RejectConverterBackedColumn(QueryPlanValue selector, string operatorName)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);

        if (UnwrapConversions(selector) is not QueryPlanColumnValue { Column.HasScalarConverter: true } column)
            return;

        throw new QueryTranslationException(
            $"Aggregate operator '{operatorName}' is not supported for converter-backed column " +
            $"'{column.Source.Table.DbName}.{column.Column.DbName}'. Scalar converters declare value conversion only, " +
            "not the algebraic or ordering semantics required to aggregate provider values as model values. " +
            "Materialize model values before aggregating or use an unconverted numeric column.");
    }

    public static QueryPlanColumnValue RequireDirectNumericColumn(QueryPlanValue selector, string operatorName)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var unwrapped = UnwrapConversions(selector);
        if (unwrapped is not QueryPlanColumnValue column)
        {
            throw new QueryTranslationException(
                $"Query plan aggregate selector '{unwrapped.Kind}' is not supported. " +
                "Only direct numeric source-slot columns are supported.");
        }

        RejectConverterBackedColumn(unwrapped, operatorName);

        if (!IsNumericType(unwrapped.ClrType))
        {
            throw new QueryTranslationException(
                $"Query plan aggregate selector column '{column.Column.DbName}' must be numeric. " +
                $"Selector type: {unwrapped.ClrType}");
        }

        return column;
    }

    private static QueryPlanValue UnwrapConversions(QueryPlanValue value)
    {
        while (value is QueryPlanConvertedValue converted)
            value = converted.Value;

        return value;
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
}
