using System;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace DataLinq.Utils;

public static class TypeUtils
{
    private static readonly ConcurrentDictionary<Type, Type> nullableTypes = new ConcurrentDictionary<Type, Type>();

    public static Type GetNullableConversionType(Type returnType)
    {
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition().Equals(typeof(Nullable<>)))
        {
            if (!nullableTypes.TryGetValue(returnType, out Type nullableType))
            {
                nullableType = new NullableConverter(returnType).UnderlyingType;
                nullableTypes.TryAdd(returnType, nullableType);
            }

            return nullableType;
        }

        return returnType;
    }
}