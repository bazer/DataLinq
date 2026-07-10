using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning;

internal abstract record QueryPlanValue
{
    protected QueryPlanValue(QueryPlanValueKind kind, Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        Kind = kind;
        ClrType = clrType;
    }

    public QueryPlanValueKind Kind { get; }

    public Type ClrType { get; }
}

internal sealed record QueryPlanColumnValue(
    QueryPlanSourceSlot Source,
    ColumnDefinition Column,
    Type ClrType) : QueryPlanValue(QueryPlanValueKind.Column, ClrType)
{
    public QueryPlanColumnValue(QueryPlanSourceSlot source, ColumnDefinition column)
        : this(source, column, column.ValueProperty?.CsType.Type ?? typeof(object))
    {
    }
}

internal sealed record QueryPlanConstantValue(object? Value, Type ClrType) : QueryPlanValue(QueryPlanValueKind.Constant, ClrType)
{
    public QueryPlanConstantValue(object? value)
        : this(value, value?.GetType() ?? typeof(object))
    {
    }
}

internal sealed record QueryPlanIntrinsicValue(QueryPlanIntrinsicKind Intrinsic, Type ClrType)
    : QueryPlanValue(QueryPlanValueKind.Intrinsic, ClrType)
;

internal sealed record QueryPlanScalarBindingReference(string BindingId, Type ClrType)
    : QueryPlanValue(QueryPlanValueKind.ScalarBinding, ClrType)
;

internal sealed record QueryPlanLocalSequenceBindingReference(string BindingId, Type ElementType)
    : QueryPlanValue(QueryPlanValueKind.LocalSequenceBinding, typeof(QueryPlanLocalSequenceBindingReference))
;

internal sealed record QueryPlanCapturedValue(string BindingId, Type ClrType) : QueryPlanValue(QueryPlanValueKind.CapturedValue, ClrType)
;

internal sealed record QueryPlanLocalSequenceValue(string BindingId, Type ElementType, int Count)
    : QueryPlanValue(QueryPlanValueKind.LocalSequence, typeof(QueryPlanLocalSequenceValue))
;

internal sealed record QueryPlanFunctionValue(
    QueryPlanFunctionKind Function,
    IReadOnlyList<QueryPlanValue> Arguments,
    Type ClrType) : QueryPlanValue(QueryPlanValueKind.Function, ClrType)
{
    public QueryPlanFunctionValue(QueryPlanFunctionKind function, IEnumerable<QueryPlanValue> arguments, Type clrType)
        : this(function, Freeze(arguments, nameof(arguments)), clrType)
    {
    }

    private static ReadOnlyCollection<QueryPlanValue> Freeze(IEnumerable<QueryPlanValue> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var array = values.ToArray();
        if (array.Any(static value => value is null))
            throw new ArgumentException("Function values cannot contain null arguments.", parameterName);

        return Array.AsReadOnly(array);
    }
}

internal sealed record QueryPlanConvertedValue(QueryPlanValue Value, Type TargetType)
    : QueryPlanValue(QueryPlanValueKind.Converted, TargetType)
;

internal sealed record QueryPlanGroupKeyValue(QueryPlanValue Key, Type ClrType)
    : QueryPlanValue(QueryPlanValueKind.GroupKey, ClrType)
;

internal sealed record QueryPlanGroupedAggregateValue(
    QueryPlanGroupedAggregateKind Aggregate,
    Type ClrType,
    QueryPlanValue? Selector = null)
    : QueryPlanValue(QueryPlanValueKind.GroupedAggregate, ClrType)
;

internal enum QueryPlanValueKind
{
    Column,
    Constant,
    Intrinsic,
    ScalarBinding,
    LocalSequenceBinding,
    CapturedValue,
    LocalSequence,
    Function,
    Converted,
    GroupKey,
    GroupedAggregate
}

internal enum QueryPlanIntrinsicKind
{
    Null,
    BooleanTrue,
    BooleanFalse
}

internal enum QueryPlanGroupedAggregateKind
{
    Count,
    Sum,
    Min,
    Max,
    Average
}

internal enum QueryPlanFunctionKind
{
    ClientExpression,
    StringStartsWith,
    StringEndsWith,
    StringContains,
    StringIsNullOrEmpty,
    StringIsNullOrWhiteSpace,
    StringLength,
    StringTrim,
    StringToUpper,
    StringToLower,
    StringSubstring,
    DatePartYear,
    DatePartMonth,
    DatePartDay,
    DatePartDayOfYear,
    DatePartDayOfWeek,
    TimePartHour,
    TimePartMinute,
    TimePartSecond,
    TimePartMillisecond
}
