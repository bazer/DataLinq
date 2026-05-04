using System;
using System.Collections.Concurrent;

namespace DataLinq.Utils;

public static class TypeUtils
{
    private static readonly ConcurrentDictionary<Type, Type> nullableTypes = new ConcurrentDictionary<Type, Type>();

    public static Type GetNullableConversionType(Type returnType)
    {
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition().Equals(typeof(Nullable<>)))
        {
            if (!nullableTypes.TryGetValue(returnType, out Type? nullableType))
            {
                nullableType = Nullable.GetUnderlyingType(returnType) ?? returnType;
                nullableTypes.TryAdd(returnType, nullableType);
            }

            return nullableType;
        }

        return returnType;
    }
}
