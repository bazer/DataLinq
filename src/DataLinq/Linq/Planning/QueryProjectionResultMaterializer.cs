using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DataLinq.Exceptions;

namespace DataLinq.Linq.Planning;

internal static class QueryProjectionResultMaterializer
{
    public static TResult ConvertResult<TResult>(object? value)
    {
        if (value is null)
            return default!;

        if (value is TResult typed)
            return typed;

        var targetType = Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult);
        return (TResult)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    public static object? ConvertValue(object? value, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        if (value is DBNull)
            value = null;

        if (value is null)
            return null;

        var nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableTarget.IsInstanceOfType(value))
            return value;

        if (nonNullableTarget.IsEnum)
        {
            return value is string stringValue
                ? Enum.Parse(nonNullableTarget, stringValue, ignoreCase: false)
                : Enum.ToObject(nonNullableTarget, value);
        }

        return Convert.ChangeType(value, nonNullableTarget, CultureInfo.InvariantCulture);
    }

    public static TResult CreateRow<TResult>(
        ConstructorInfo constructor,
        IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        ArgumentNullException.ThrowIfNull(values);

        var parameters = constructor.GetParameters();
        if (parameters.Length != values.Count)
        {
            throw new QueryTranslationException(
                $"Projection constructor expects {parameters.Length} values, but the query plan supplied {values.Count}.");
        }

        var arguments = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
            arguments[index] = ConvertValue(values[index], parameters[index].ParameterType);

        return ConvertResult<TResult>(constructor.Invoke(arguments));
    }
}
