using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DataLinq.Exceptions;
using DataLinq.Instances;

namespace DataLinq.Linq.Planning;

internal static class QueryPlanProjectionRecipeEvaluator
{
    public static object? Evaluate(
        QueryPlanProjectionRecipe recipe,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues)
        => Evaluate(recipe, sourceValues, bindingValues, ProjectionEvaluationOptions.Default);

    public static object? Evaluate(
        QueryPlanProjectionRecipe recipe,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(sourceValues);
        ArgumentNullException.ThrowIfNull(bindingValues);

        return EvaluateCore(recipe, sourceValues, bindingValues, options);
    }

    private static object? EvaluateCore(
        QueryPlanProjectionRecipe recipe,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
        => recipe switch
        {
            QueryPlanProjectionRecipe.Source source => GetSourceValue(source.SourceSlot, sourceValues),
            QueryPlanProjectionRecipe.SourceColumn column => GetSourceColumnValue(column, sourceValues),
            QueryPlanProjectionRecipe.ScalarBinding binding => GetScalarBindingValue(binding, bindingValues),
            QueryPlanProjectionRecipe.Intrinsic intrinsic => GetIntrinsicValue(intrinsic),
            QueryPlanProjectionRecipe.Convert conversion => ConvertValue(
                EvaluateCore(conversion.Operand, sourceValues, bindingValues, options),
                conversion.ResultType),
            QueryPlanProjectionRecipe.Not not => EvaluateNot(not, sourceValues, bindingValues, options),
            QueryPlanProjectionRecipe.Binary binary => EvaluateBinary(binary, sourceValues, bindingValues, options),
            QueryPlanProjectionRecipe.SupportedMember member => EvaluateSupportedMember(
                member,
                sourceValues,
                bindingValues,
                options),
            QueryPlanProjectionRecipe.Function function => EvaluateFunction(
                function,
                sourceValues,
                bindingValues,
                options),
            QueryPlanProjectionRecipe.Conditional conditional => EvaluateConditional(
                conditional,
                sourceValues,
                bindingValues,
                options),
            QueryPlanProjectionRecipe.NewArray newArray => EvaluateNewArray(
                newArray,
                sourceValues,
                bindingValues,
                options),
            QueryPlanProjectionRecipe.CompatibilityConstructor constructor => EvaluateCompatibilityConstructor(
                constructor,
                sourceValues,
                bindingValues,
                options),
            QueryPlanProjectionRecipe.CompatibilityMember member => EvaluateCompatibilityMember(
                member,
                sourceValues,
                bindingValues,
                options),
            _ => throw new QueryTranslationException(
                $"Projection recipe node '{recipe.GetType().Name}' is not supported by the normalized recipe evaluator.")
        };

    private static object? GetSourceValue(
        QueryPlanSourceSlot source,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues)
    {
        if (sourceValues.TryGetValue(source, out var value))
            return value;

        throw new QueryTranslationException(
            $"Projection recipe references source slot '{source.Id}', but no row value was supplied for that source.");
    }

    private static object? GetSourceColumnValue(
        QueryPlanProjectionRecipe.SourceColumn column,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues)
    {
        var sourceValue = GetSourceValue(column.SourceSlot, sourceValues);
        if (sourceValue is not IModelInstance model)
        {
            throw new QueryTranslationException(
                $"Projection recipe source slot '{column.SourceSlot.Id}' must contain a model row to read column '{column.Column.DbName}'.");
        }

        return model.GetRowData().GetValue(column.Column);
    }

    private static object? GetScalarBindingValue(
        QueryPlanProjectionRecipe.ScalarBinding binding,
        QueryPlanBindingValues bindingValues)
    {
        if (!bindingValues.TryGet(binding.BindingId, out var value))
        {
            throw new QueryTranslationException(
                $"Projection recipe references scalar binding '{binding.BindingId}', but the invocation supplied no value.");
        }

        if (value is not QueryPlanInvocationValue.Scalar scalar)
        {
            throw new QueryTranslationException(
                $"Projection recipe binding '{binding.BindingId}' requires a scalar invocation value, but received '{value.Kind}'.");
        }

        return scalar.Value;
    }

    private static object? GetIntrinsicValue(QueryPlanProjectionRecipe.Intrinsic intrinsic)
        => intrinsic.IntrinsicKind switch
        {
            QueryPlanProjectionIntrinsicKind.Null => null,
            QueryPlanProjectionIntrinsicKind.BooleanTrue => true,
            QueryPlanProjectionIntrinsicKind.BooleanFalse => false,
            _ => throw new QueryTranslationException(
                $"Projection intrinsic '{intrinsic.IntrinsicKind}' is not supported by the normalized recipe evaluator.")
        };

    private static object? EvaluateNot(
        QueryPlanProjectionRecipe.Not not,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        var value = EvaluateCore(not.Operand, sourceValues, bindingValues, options);
        if (value is bool boolean)
            return !boolean;

        if (value is null && Nullable.GetUnderlyingType(not.ResultType) == typeof(bool))
            return null;

        throw new QueryTranslationException(
            $"Projection logical negation requires a Boolean operand, but recipe '{not.Kind}' produced '{value?.GetType().FullName ?? "null"}'.");
    }

    private static object? EvaluateBinary(
        QueryPlanProjectionRecipe.Binary binary,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        var left = EvaluateCore(binary.Left, sourceValues, bindingValues, options);

        if (binary.Operator == QueryPlanProjectionBinaryOperator.AndAlso &&
            !Convert.ToBoolean(left, CultureInfo.InvariantCulture))
        {
            return false;
        }

        if (binary.Operator == QueryPlanProjectionBinaryOperator.OrElse &&
            Convert.ToBoolean(left, CultureInfo.InvariantCulture))
        {
            return true;
        }

        var right = EvaluateCore(binary.Right, sourceValues, bindingValues, options);
        return binary.Operator switch
        {
            QueryPlanProjectionBinaryOperator.Add => Add(left, right, binary.ResultType),
            QueryPlanProjectionBinaryOperator.Equal => EvaluateEquality(binary, left, right),
            QueryPlanProjectionBinaryOperator.NotEqual => !EvaluateEquality(binary, left, right),
            QueryPlanProjectionBinaryOperator.AndAlso => Convert.ToBoolean(right, CultureInfo.InvariantCulture),
            QueryPlanProjectionBinaryOperator.OrElse => Convert.ToBoolean(right, CultureInfo.InvariantCulture),
            QueryPlanProjectionBinaryOperator.GreaterThan or
            QueryPlanProjectionBinaryOperator.GreaterThanOrEqual or
            QueryPlanProjectionBinaryOperator.LessThan or
            QueryPlanProjectionBinaryOperator.LessThanOrEqual => EvaluateRelational(
                binary.Operator,
                left,
                right,
                binary.ResultType),
            _ => throw new QueryTranslationException(
                $"Projection binary operator '{binary.Operator}' is not supported by the normalized recipe evaluator.")
        };
    }

    private static object? Add(object? left, object? right, Type resultType)
    {
        var targetType = Nullable.GetUnderlyingType(resultType) ?? resultType;
        if (targetType == typeof(string))
            return string.Concat(left, right);

        if (left is null || right is null)
        {
            if (Nullable.GetUnderlyingType(resultType) is not null)
                return null;

            throw new QueryTranslationException("Projection arithmetic does not support null operands for a non-nullable result.");
        }

        return Type.GetTypeCode(targetType) switch
        {
            TypeCode.Byte => unchecked((byte)(
                Convert.ToByte(left, CultureInfo.InvariantCulture) +
                Convert.ToByte(right, CultureInfo.InvariantCulture))),
            TypeCode.SByte => unchecked((sbyte)(
                Convert.ToSByte(left, CultureInfo.InvariantCulture) +
                Convert.ToSByte(right, CultureInfo.InvariantCulture))),
            TypeCode.Int16 => unchecked((short)(
                Convert.ToInt16(left, CultureInfo.InvariantCulture) +
                Convert.ToInt16(right, CultureInfo.InvariantCulture))),
            TypeCode.UInt16 => unchecked((ushort)(
                Convert.ToUInt16(left, CultureInfo.InvariantCulture) +
                Convert.ToUInt16(right, CultureInfo.InvariantCulture))),
            TypeCode.Int32 => unchecked(
                Convert.ToInt32(left, CultureInfo.InvariantCulture) +
                Convert.ToInt32(right, CultureInfo.InvariantCulture)),
            TypeCode.UInt32 => unchecked(
                Convert.ToUInt32(left, CultureInfo.InvariantCulture) +
                Convert.ToUInt32(right, CultureInfo.InvariantCulture)),
            TypeCode.Int64 => unchecked(
                Convert.ToInt64(left, CultureInfo.InvariantCulture) +
                Convert.ToInt64(right, CultureInfo.InvariantCulture)),
            TypeCode.UInt64 => unchecked(
                Convert.ToUInt64(left, CultureInfo.InvariantCulture) +
                Convert.ToUInt64(right, CultureInfo.InvariantCulture)),
            TypeCode.Single =>
                Convert.ToSingle(left, CultureInfo.InvariantCulture) +
                Convert.ToSingle(right, CultureInfo.InvariantCulture),
            TypeCode.Double =>
                Convert.ToDouble(left, CultureInfo.InvariantCulture) +
                Convert.ToDouble(right, CultureInfo.InvariantCulture),
            TypeCode.Decimal =>
                Convert.ToDecimal(left, CultureInfo.InvariantCulture) +
                Convert.ToDecimal(right, CultureInfo.InvariantCulture),
            _ => throw new QueryTranslationException(
                $"Projection Add does not support result type '{resultType.FullName}'.")
        };
    }

    private static int Compare(object? left, object? right)
    {
        if (left is IComparable comparable)
            return comparable.CompareTo(right);

        throw new QueryTranslationException(
            $"Projection comparison is not supported for value type '{left?.GetType().FullName ?? "null"}'.");
    }

    private static object? EvaluateRelational(
        QueryPlanProjectionBinaryOperator @operator,
        object? left,
        object? right,
        Type resultType)
    {
        if (left is null || right is null)
        {
            return Nullable.GetUnderlyingType(resultType) == typeof(bool)
                ? null
                : false;
        }

        if (left is double leftDouble && right is double rightDouble)
        {
            return @operator switch
            {
                QueryPlanProjectionBinaryOperator.GreaterThan => leftDouble > rightDouble,
                QueryPlanProjectionBinaryOperator.GreaterThanOrEqual => leftDouble >= rightDouble,
                QueryPlanProjectionBinaryOperator.LessThan => leftDouble < rightDouble,
                QueryPlanProjectionBinaryOperator.LessThanOrEqual => leftDouble <= rightDouble,
                _ => throw InvalidRelationalOperator(@operator)
            };
        }

        if (left is float leftSingle && right is float rightSingle)
        {
            return @operator switch
            {
                QueryPlanProjectionBinaryOperator.GreaterThan => leftSingle > rightSingle,
                QueryPlanProjectionBinaryOperator.GreaterThanOrEqual => leftSingle >= rightSingle,
                QueryPlanProjectionBinaryOperator.LessThan => leftSingle < rightSingle,
                QueryPlanProjectionBinaryOperator.LessThanOrEqual => leftSingle <= rightSingle,
                _ => throw InvalidRelationalOperator(@operator)
            };
        }

        var comparison = Compare(left, right);
        return @operator switch
        {
            QueryPlanProjectionBinaryOperator.GreaterThan => comparison > 0,
            QueryPlanProjectionBinaryOperator.GreaterThanOrEqual => comparison >= 0,
            QueryPlanProjectionBinaryOperator.LessThan => comparison < 0,
            QueryPlanProjectionBinaryOperator.LessThanOrEqual => comparison <= 0,
            _ => throw InvalidRelationalOperator(@operator)
        };
    }

    private static QueryTranslationException InvalidRelationalOperator(
        QueryPlanProjectionBinaryOperator @operator)
        => new($"Projection operator '{@operator}' is not relational.");

    private static bool EvaluateEquality(
        QueryPlanProjectionRecipe.Binary binary,
        object? left,
        object? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        var leftType = Nullable.GetUnderlyingType(binary.Left.ResultType) ?? binary.Left.ResultType;
        var rightType = Nullable.GetUnderlyingType(binary.Right.ResultType) ?? binary.Right.ResultType;

        if (leftType == typeof(double) && rightType == typeof(double))
            return (double)left == (double)right;

        if (leftType == typeof(float) && rightType == typeof(float))
            return (float)left == (float)right;

        if (leftType == typeof(string) && rightType == typeof(string))
            return string.Equals((string)left, (string)right, StringComparison.Ordinal);

        if (!leftType.IsValueType || !rightType.IsValueType)
            return ReferenceEquals(left, right);

        return Equals(left, right);
    }

    private static object? EvaluateSupportedMember(
        QueryPlanProjectionRecipe.SupportedMember member,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        var instance = EvaluateCore(member.Instance, sourceValues, bindingValues, options);
        return member.Member switch
        {
            QueryPlanProjectionSupportedMemberKind.NullableHasValue => instance is not null,
            QueryPlanProjectionSupportedMemberKind.NullableValue => instance ??
                throw new InvalidOperationException("Nullable object must have a value."),
            QueryPlanProjectionSupportedMemberKind.StringLength when instance is string text => text.Length,
            QueryPlanProjectionSupportedMemberKind.StringLength => throw new QueryTranslationException(
                $"Projection StringLength member requires a String instance, but received '{instance?.GetType().FullName ?? "null"}'."),
            _ => throw new QueryTranslationException(
                $"Projection supported member '{member.Member}' is not supported by the normalized recipe evaluator.")
        };
    }

    private static object? EvaluateFunction(
        QueryPlanProjectionRecipe.Function function,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        var arguments = function.Arguments
            .Select(argument => EvaluateCore(argument, sourceValues, bindingValues, options))
            .ToArray();

        return function.FunctionKind switch
        {
            QueryPlanProjectionFunctionKind.StringTrim => GetStringArgument(function, arguments, 1).Trim(),
            QueryPlanProjectionFunctionKind.StringToUpper => GetStringArgument(function, arguments, 1).ToUpper(CultureInfo.CurrentCulture),
            QueryPlanProjectionFunctionKind.StringToLower => GetStringArgument(function, arguments, 1).ToLower(CultureInfo.CurrentCulture),
            QueryPlanProjectionFunctionKind.StringSubstring => EvaluateSubstring(function, arguments),
            QueryPlanProjectionFunctionKind.DatePartYear => GetDatePart(function, arguments, static (dateTime, dateOnly) => dateTime?.Year ?? dateOnly!.Value.Year),
            QueryPlanProjectionFunctionKind.DatePartMonth => GetDatePart(function, arguments, static (dateTime, dateOnly) => dateTime?.Month ?? dateOnly!.Value.Month),
            QueryPlanProjectionFunctionKind.DatePartDay => GetDatePart(function, arguments, static (dateTime, dateOnly) => dateTime?.Day ?? dateOnly!.Value.Day),
            QueryPlanProjectionFunctionKind.DatePartDayOfYear => GetDatePart(function, arguments, static (dateTime, dateOnly) => dateTime?.DayOfYear ?? dateOnly!.Value.DayOfYear),
            QueryPlanProjectionFunctionKind.DatePartDayOfWeek => GetDatePart(function, arguments, static (dateTime, dateOnly) => dateTime?.DayOfWeek ?? dateOnly!.Value.DayOfWeek),
            QueryPlanProjectionFunctionKind.TimePartHour => GetTimePart(function, arguments, static (dateTime, timeOnly) => dateTime?.Hour ?? timeOnly!.Value.Hour),
            QueryPlanProjectionFunctionKind.TimePartMinute => GetTimePart(function, arguments, static (dateTime, timeOnly) => dateTime?.Minute ?? timeOnly!.Value.Minute),
            QueryPlanProjectionFunctionKind.TimePartSecond => GetTimePart(function, arguments, static (dateTime, timeOnly) => dateTime?.Second ?? timeOnly!.Value.Second),
            QueryPlanProjectionFunctionKind.TimePartMillisecond => GetTimePart(function, arguments, static (dateTime, timeOnly) => dateTime?.Millisecond ?? timeOnly!.Value.Millisecond),
            _ => throw new QueryTranslationException(
                $"Projection function '{function.FunctionKind}' is not supported by the normalized recipe evaluator.")
        };
    }

    private static string GetStringArgument(
        QueryPlanProjectionRecipe.Function function,
        IReadOnlyList<object?> arguments,
        int expectedCount)
    {
        if (arguments.Count != expectedCount || arguments[0] is not string text)
        {
            throw new QueryTranslationException(
                $"Projection function '{function.FunctionKind}' requires one String source argument.");
        }

        return text;
    }

    private static object EvaluateSubstring(
        QueryPlanProjectionRecipe.Function function,
        IReadOnlyList<object?> arguments)
    {
        if (arguments.Count is not 2 and not 3 || arguments[0] is not string text)
        {
            throw new QueryTranslationException(
                $"Projection function '{function.FunctionKind}' requires a String source, start index, and optional length.");
        }

        var startIndex = Convert.ToInt32(arguments[1], CultureInfo.InvariantCulture);
        return arguments.Count == 2
            ? text.Substring(startIndex)
            : text.Substring(startIndex, Convert.ToInt32(arguments[2], CultureInfo.InvariantCulture));
    }

    private static object GetDatePart(
        QueryPlanProjectionRecipe.Function function,
        IReadOnlyList<object?> arguments,
        Func<DateTime?, DateOnly?, object> selector)
    {
        if (arguments.Count != 1)
            throw InvalidPartSource(function);

        return arguments[0] switch
        {
            DateTime dateTime => selector(dateTime, null),
            DateOnly dateOnly => selector(null, dateOnly),
            _ => throw InvalidPartSource(function)
        };
    }

    private static object GetTimePart(
        QueryPlanProjectionRecipe.Function function,
        IReadOnlyList<object?> arguments,
        Func<DateTime?, TimeOnly?, object> selector)
    {
        if (arguments.Count != 1)
            throw InvalidPartSource(function);

        return arguments[0] switch
        {
            DateTime dateTime => selector(dateTime, null),
            TimeOnly timeOnly => selector(null, timeOnly),
            _ => throw InvalidPartSource(function)
        };
    }

    private static QueryTranslationException InvalidPartSource(QueryPlanProjectionRecipe.Function function)
        => new($"Projection function '{function.FunctionKind}' requires one supported date or time source argument.");

    private static object? EvaluateConditional(
        QueryPlanProjectionRecipe.Conditional conditional,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        var test = EvaluateCore(conditional.Test, sourceValues, bindingValues, options);
        return Convert.ToBoolean(test, CultureInfo.InvariantCulture)
            ? EvaluateCore(conditional.IfTrue, sourceValues, bindingValues, options)
            : EvaluateCore(conditional.IfFalse, sourceValues, bindingValues, options);
    }

    private static object EvaluateNewArray(
        QueryPlanProjectionRecipe.NewArray newArray,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        var values = newArray.Elements
            .Select(element => EvaluateCore(element, sourceValues, bindingValues, options))
            .ToArray();

        var elementType = newArray.ElementType;
        if (elementType == typeof(string))
            return values.Select(value => (string?)ConvertValue(value, elementType)).ToArray();
        if (elementType == typeof(int))
            return CreateArray<int>(values, elementType);
        if (elementType == typeof(long))
            return CreateArray<long>(values, elementType);
        if (elementType == typeof(short))
            return CreateArray<short>(values, elementType);
        if (elementType == typeof(byte))
            return CreateArray<byte>(values, elementType);
        if (elementType == typeof(bool))
            return CreateArray<bool>(values, elementType);
        if (elementType == typeof(decimal))
            return CreateArray<decimal>(values, elementType);
        if (elementType == typeof(double))
            return CreateArray<double>(values, elementType);
        if (elementType == typeof(float))
            return CreateArray<float>(values, elementType);
        if (elementType == typeof(Guid))
            return CreateArray<Guid>(values, elementType);
        if (elementType == typeof(DateTime))
            return CreateArray<DateTime>(values, elementType);
        if (elementType == typeof(DateOnly))
            return CreateArray<DateOnly>(values, elementType);
        if (elementType == typeof(TimeOnly))
            return CreateArray<TimeOnly>(values, elementType);
        if (elementType == typeof(object))
            return values;

        throw new QueryTranslationException(
            $"Projection array creation for element type '{elementType.FullName}' is not supported without runtime array activation.");
    }

    private static T[] CreateArray<T>(object?[] values, Type elementType)
        => values.Select(value => (T)ConvertValue(value, elementType)!).ToArray();

    private static object? EvaluateCompatibilityConstructor(
        QueryPlanProjectionRecipe.CompatibilityConstructor constructor,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        var arguments = constructor.Arguments
            .Select(argument => EvaluateCore(argument, sourceValues, bindingValues, options))
            .ToArray();

        if (!options.AllowCompatibilityObjectConstruction)
        {
            throw new QueryTranslationException(
                $"Projection object construction for '{constructor.ResultType.FullName}' requires compatibility constructor invocation.");
        }

        return constructor.Constructor.Invoke(arguments);
    }

    private static object? EvaluateCompatibilityMember(
        QueryPlanProjectionRecipe.CompatibilityMember member,
        IReadOnlyDictionary<QueryPlanSourceSlot, object?> sourceValues,
        QueryPlanBindingValues bindingValues,
        ProjectionEvaluationOptions options)
    {
        var instance = member.Instance is null
            ? null
            : EvaluateCore(member.Instance, sourceValues, bindingValues, options);

        if (!options.AllowCompatibilityMemberReflection)
        {
            throw new QueryTranslationException(
                $"Projection member '{member.Member.Name}' requires compatibility member reflection.");
        }

        return member.Member switch
        {
            FieldInfo field => field.GetValue(instance),
            PropertyInfo property => property.GetValue(instance),
            _ => throw new QueryTranslationException(
                $"Projection compatibility member '{member.Member.Name}' is not a supported field or property.")
        };
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (targetType == typeof(void))
            return null;

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (value is null)
        {
            return nullableType is not null || !targetType.IsValueType
                ? null
                : GetDefaultValue(targetType);
        }

        if (targetType.IsInstanceOfType(value))
            return value;

        var conversionType = nullableType ?? targetType;
        if (conversionType.IsEnum)
            return Enum.ToObject(conversionType, value);

        if (value is char character && conversionType != typeof(char))
            value = (ushort)character;

        return Convert.ChangeType(value, conversionType, CultureInfo.InvariantCulture);
    }

    private static object GetDefaultValue(Type targetType)
    {
        if (targetType == typeof(bool))
            return false;
        if (targetType == typeof(byte))
            return default(byte);
        if (targetType == typeof(sbyte))
            return default(sbyte);
        if (targetType == typeof(short))
            return default(short);
        if (targetType == typeof(ushort))
            return default(ushort);
        if (targetType == typeof(int))
            return default(int);
        if (targetType == typeof(uint))
            return default(uint);
        if (targetType == typeof(long))
            return default(long);
        if (targetType == typeof(ulong))
            return default(ulong);
        if (targetType == typeof(float))
            return default(float);
        if (targetType == typeof(double))
            return default(double);
        if (targetType == typeof(decimal))
            return default(decimal);
        if (targetType == typeof(char))
            return default(char);
        if (targetType == typeof(DateTime))
            return default(DateTime);
        if (targetType == typeof(DateOnly))
            return default(DateOnly);
        if (targetType == typeof(TimeOnly))
            return default(TimeOnly);
        if (targetType == typeof(Guid))
            return default(Guid);
        if (targetType.IsEnum)
            return Enum.ToObject(targetType, 0);

        throw new QueryTranslationException(
            $"Projection cannot materialize null as non-nullable value type '{targetType.FullName}' without runtime activation.");
    }
}
