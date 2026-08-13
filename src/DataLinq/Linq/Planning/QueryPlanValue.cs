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

internal sealed record QueryPlanIntrinsicValue(QueryPlanIntrinsicKind Intrinsic, Type ClrType)
    : QueryPlanValue(QueryPlanValueKind.Intrinsic, ClrType)
;

internal sealed record QueryPlanScalarBindingReference(string BindingId, Type ClrType)
    : QueryPlanValue(QueryPlanValueKind.ScalarBinding, ClrType)
;

internal sealed record QueryPlanLocalSequenceBindingReference(string BindingId, Type ElementType)
    : QueryPlanValue(QueryPlanValueKind.LocalSequenceBinding, typeof(QueryPlanLocalSequenceBindingReference))
;

internal sealed record QueryPlanFunctionValue : QueryPlanValue
{
    public QueryPlanFunctionValue(QueryPlanFunctionKind function, IEnumerable<QueryPlanValue> arguments, Type clrType)
        : base(QueryPlanValueKind.Function, clrType)
    {
        if (!Enum.IsDefined(function))
            throw new ArgumentOutOfRangeException(nameof(function), function, "Unknown query plan function.");

        Function = function;
        Arguments = Freeze(arguments, nameof(arguments));
        if (Arguments.Count == 0)
            throw new ArgumentException("Query plan functions must contain at least one argument.", nameof(arguments));
    }

    public QueryPlanFunctionKind Function { get; }

    public IReadOnlyList<QueryPlanValue> Arguments { get; }

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
    Intrinsic,
    ScalarBinding,
    LocalSequenceBinding,
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
