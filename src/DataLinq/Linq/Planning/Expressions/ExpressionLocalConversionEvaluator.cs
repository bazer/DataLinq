using System;
using System.Globalization;
using System.Reflection;

namespace DataLinq.Linq.Planning.Expressions;

internal static class ExpressionLocalConversionEvaluator
{
    internal static bool IsFrameworkNumericConversion(MethodInfo method)
        => method.Name is "op_Implicit" or "op_Explicit" &&
           (method.DeclaringType == typeof(decimal) ||
            method.DeclaringType == typeof(nint) ||
            method.DeclaringType == typeof(nuint));

    internal static object? ConvertBuiltIn(
        object? value,
        Type sourceType,
        Type targetType,
        bool checkOverflow)
    {
        var nullableSourceType = Nullable.GetUnderlyingType(sourceType);
        var nullableTargetType = Nullable.GetUnderlyingType(targetType);

        if (value is null)
        {
            if (nullableTargetType is not null || !targetType.IsValueType)
                return null;

            if (nullableSourceType is not null)
                throw new InvalidOperationException("Nullable object must have a value.");

            throw new NullReferenceException(
                $"Cannot convert null to non-nullable type '{targetType.FullName}'.");
        }

        var effectiveSourceType = nullableSourceType ?? sourceType;
        var effectiveTargetType = nullableTargetType ?? targetType;

        if (effectiveSourceType == effectiveTargetType)
            return value;

        if (!effectiveTargetType.IsValueType)
        {
            if (effectiveTargetType.IsInstanceOfType(value))
                return value;

            throw InvalidConversion(sourceType, targetType);
        }

        if (!effectiveSourceType.IsValueType)
        {
            if (value.GetType() == effectiveTargetType)
                return value;

            throw InvalidConversion(sourceType, targetType);
        }

        if (effectiveSourceType.IsEnum)
        {
            value = GetEnumStorageValue(value, effectiveSourceType);
            effectiveSourceType = Enum.GetUnderlyingType(effectiveSourceType);
        }

        if (effectiveTargetType.IsEnum)
        {
            var storageType = Enum.GetUnderlyingType(effectiveTargetType);
            var storageValue = ConvertNumeric(value, effectiveSourceType, storageType, checkOverflow);
            return Enum.ToObject(effectiveTargetType, storageValue);
        }

        if (IsNumericType(effectiveSourceType) && IsNumericType(effectiveTargetType))
            return ConvertNumeric(value, effectiveSourceType, effectiveTargetType, checkOverflow);

        throw InvalidConversion(sourceType, targetType);
    }

    private static object ConvertNumeric(
        object value,
        Type sourceType,
        Type targetType,
        bool checkOverflow)
    {
        // System.Convert rounds floating-point values and always checks narrowing conversions.
        // Dispatch to typed casts so Convert and ConvertChecked retain their distinct CLR semantics.
        if (sourceType == typeof(sbyte))
            return ConvertFromInt64((sbyte)value, targetType, checkOverflow);
        if (sourceType == typeof(short))
            return ConvertFromInt64((short)value, targetType, checkOverflow);
        if (sourceType == typeof(int))
            return ConvertFromInt64((int)value, targetType, checkOverflow);
        if (sourceType == typeof(long))
            return ConvertFromInt64((long)value, targetType, checkOverflow);
        if (sourceType == typeof(nint))
            return ConvertFromInt64((nint)value, targetType, checkOverflow);
        if (sourceType == typeof(byte))
            return ConvertFromUInt64((byte)value, targetType, checkOverflow);
        if (sourceType == typeof(ushort))
            return ConvertFromUInt64((ushort)value, targetType, checkOverflow);
        if (sourceType == typeof(char))
            return ConvertFromUInt64((char)value, targetType, checkOverflow);
        if (sourceType == typeof(uint))
            return ConvertFromUInt64((uint)value, targetType, checkOverflow);
        if (sourceType == typeof(ulong))
            return ConvertFromUInt64((ulong)value, targetType, checkOverflow);
        if (sourceType == typeof(nuint))
            return ConvertFromUInt64((nuint)value, targetType, checkOverflow);
        if (sourceType == typeof(float))
            return ConvertFromSingle((float)value, targetType, checkOverflow);
        if (sourceType == typeof(double))
            return ConvertFromDouble((double)value, targetType, checkOverflow);
        if (sourceType == typeof(decimal))
            return ConvertFromDecimal((decimal)value, targetType);

        throw InvalidConversion(sourceType, targetType);
    }

    private static object ConvertFromInt64(long value, Type targetType, bool checkOverflow)
    {
        if (targetType == typeof(sbyte))
            return checkOverflow ? checked((sbyte)value) : unchecked((sbyte)value);
        if (targetType == typeof(byte))
            return checkOverflow ? checked((byte)value) : unchecked((byte)value);
        if (targetType == typeof(short))
            return checkOverflow ? checked((short)value) : unchecked((short)value);
        if (targetType == typeof(ushort))
            return checkOverflow ? checked((ushort)value) : unchecked((ushort)value);
        if (targetType == typeof(char))
            return checkOverflow ? checked((char)value) : unchecked((char)value);
        if (targetType == typeof(int))
            return checkOverflow ? checked((int)value) : unchecked((int)value);
        if (targetType == typeof(uint))
            return checkOverflow ? checked((uint)value) : unchecked((uint)value);
        if (targetType == typeof(long))
            return value;
        if (targetType == typeof(ulong))
            return checkOverflow ? checked((ulong)value) : unchecked((ulong)value);
        if (targetType == typeof(nint))
            return checkOverflow ? checked((nint)value) : unchecked((nint)value);
        if (targetType == typeof(nuint))
            return checkOverflow ? checked((nuint)value) : unchecked((nuint)value);
        if (targetType == typeof(float))
            return (float)value;
        if (targetType == typeof(double))
            return (double)value;
        if (targetType == typeof(decimal))
            return (decimal)value;

        throw InvalidConversion(typeof(long), targetType);
    }

    private static object ConvertFromUInt64(ulong value, Type targetType, bool checkOverflow)
    {
        if (targetType == typeof(sbyte))
            return checkOverflow ? checked((sbyte)value) : unchecked((sbyte)value);
        if (targetType == typeof(byte))
            return checkOverflow ? checked((byte)value) : unchecked((byte)value);
        if (targetType == typeof(short))
            return checkOverflow ? checked((short)value) : unchecked((short)value);
        if (targetType == typeof(ushort))
            return checkOverflow ? checked((ushort)value) : unchecked((ushort)value);
        if (targetType == typeof(char))
            return checkOverflow ? checked((char)value) : unchecked((char)value);
        if (targetType == typeof(int))
            return checkOverflow ? checked((int)value) : unchecked((int)value);
        if (targetType == typeof(uint))
            return checkOverflow ? checked((uint)value) : unchecked((uint)value);
        if (targetType == typeof(long))
            return checkOverflow ? checked((long)value) : unchecked((long)value);
        if (targetType == typeof(ulong))
            return value;
        if (targetType == typeof(nint))
            return checkOverflow ? checked((nint)value) : unchecked((nint)value);
        if (targetType == typeof(nuint))
            return checkOverflow ? checked((nuint)value) : unchecked((nuint)value);
        if (targetType == typeof(float))
            return (float)value;
        if (targetType == typeof(double))
            return (double)value;
        if (targetType == typeof(decimal))
            return (decimal)value;

        throw InvalidConversion(typeof(ulong), targetType);
    }

    private static object ConvertFromSingle(float value, Type targetType, bool checkOverflow)
    {
        if (targetType == typeof(sbyte))
            return checkOverflow ? checked((sbyte)value) : unchecked((sbyte)value);
        if (targetType == typeof(byte))
            return checkOverflow ? checked((byte)value) : unchecked((byte)value);
        if (targetType == typeof(short))
            return checkOverflow ? checked((short)value) : unchecked((short)value);
        if (targetType == typeof(ushort))
            return checkOverflow ? checked((ushort)value) : unchecked((ushort)value);
        if (targetType == typeof(char))
            return checkOverflow ? checked((char)value) : unchecked((char)value);
        if (targetType == typeof(int))
            return checkOverflow ? checked((int)value) : unchecked((int)value);
        if (targetType == typeof(uint))
            return checkOverflow ? checked((uint)value) : unchecked((uint)value);
        if (targetType == typeof(long))
            return checkOverflow ? checked((long)value) : unchecked((long)value);
        if (targetType == typeof(ulong))
            return checkOverflow ? checked((ulong)value) : unchecked((ulong)value);
        if (targetType == typeof(nint))
            return checkOverflow ? checked((nint)value) : unchecked((nint)value);
        if (targetType == typeof(nuint))
            return checkOverflow ? checked((nuint)value) : unchecked((nuint)value);
        if (targetType == typeof(float))
            return value;
        if (targetType == typeof(double))
            return (double)value;
        if (targetType == typeof(decimal))
            return (decimal)value;

        throw InvalidConversion(typeof(float), targetType);
    }

    private static object ConvertFromDouble(double value, Type targetType, bool checkOverflow)
    {
        if (targetType == typeof(sbyte))
            return checkOverflow ? checked((sbyte)value) : unchecked((sbyte)value);
        if (targetType == typeof(byte))
            return checkOverflow ? checked((byte)value) : unchecked((byte)value);
        if (targetType == typeof(short))
            return checkOverflow ? checked((short)value) : unchecked((short)value);
        if (targetType == typeof(ushort))
            return checkOverflow ? checked((ushort)value) : unchecked((ushort)value);
        if (targetType == typeof(char))
            return checkOverflow ? checked((char)value) : unchecked((char)value);
        if (targetType == typeof(int))
            return checkOverflow ? checked((int)value) : unchecked((int)value);
        if (targetType == typeof(uint))
            return checkOverflow ? checked((uint)value) : unchecked((uint)value);
        if (targetType == typeof(long))
            return checkOverflow ? checked((long)value) : unchecked((long)value);
        if (targetType == typeof(ulong))
            return checkOverflow ? checked((ulong)value) : unchecked((ulong)value);
        if (targetType == typeof(nint))
            return checkOverflow ? checked((nint)value) : unchecked((nint)value);
        if (targetType == typeof(nuint))
            return checkOverflow ? checked((nuint)value) : unchecked((nuint)value);
        if (targetType == typeof(float))
            return (float)value;
        if (targetType == typeof(double))
            return value;
        if (targetType == typeof(decimal))
            return (decimal)value;

        throw InvalidConversion(typeof(double), targetType);
    }

    private static object ConvertFromDecimal(decimal value, Type targetType)
    {
        if (targetType == typeof(sbyte))
            return (sbyte)value;
        if (targetType == typeof(byte))
            return (byte)value;
        if (targetType == typeof(short))
            return (short)value;
        if (targetType == typeof(ushort))
            return (ushort)value;
        if (targetType == typeof(char))
            return (char)value;
        if (targetType == typeof(int))
            return (int)value;
        if (targetType == typeof(uint))
            return (uint)value;
        if (targetType == typeof(long))
            return (long)value;
        if (targetType == typeof(ulong))
            return (ulong)value;
        if (targetType == typeof(nint))
            return (nint)value;
        if (targetType == typeof(nuint))
            return (nuint)value;
        if (targetType == typeof(float))
            return (float)value;
        if (targetType == typeof(double))
            return (double)value;
        if (targetType == typeof(decimal))
            return value;

        throw InvalidConversion(typeof(decimal), targetType);
    }

    private static object GetEnumStorageValue(object value, Type enumType)
    {
        return Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte => Convert.ToSByte(value, CultureInfo.InvariantCulture),
            TypeCode.Byte => Convert.ToByte(value, CultureInfo.InvariantCulture),
            TypeCode.Int16 => Convert.ToInt16(value, CultureInfo.InvariantCulture),
            TypeCode.UInt16 => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
            TypeCode.Int32 => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            TypeCode.UInt32 => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
            TypeCode.Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            TypeCode.UInt64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
            _ => throw InvalidConversion(enumType, Enum.GetUnderlyingType(enumType))
        };
    }

    private static bool IsNumericType(Type type)
        => type == typeof(sbyte) ||
           type == typeof(byte) ||
           type == typeof(short) ||
           type == typeof(ushort) ||
           type == typeof(char) ||
           type == typeof(int) ||
           type == typeof(uint) ||
           type == typeof(long) ||
           type == typeof(ulong) ||
           type == typeof(nint) ||
           type == typeof(nuint) ||
           type == typeof(float) ||
           type == typeof(double) ||
           type == typeof(decimal);

    private static InvalidCastException InvalidConversion(Type sourceType, Type targetType)
        => new($"Cannot convert local value from '{sourceType.FullName}' to '{targetType.FullName}'.");
}
