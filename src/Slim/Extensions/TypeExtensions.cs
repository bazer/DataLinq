using System;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace Slim.Extensions
{
    internal static class TypeExtensions
    {
        private static readonly ConcurrentDictionary<Type, Type> nullableTypes = new ConcurrentDictionary<Type, Type>();

        internal static Type GetNullableConversionType(this Type returnType)
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
}