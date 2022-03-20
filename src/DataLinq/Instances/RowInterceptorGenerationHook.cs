using Castle.DynamicProxy;
using System;
using System.Reflection;

namespace DataLinq.Instances
{
    internal class RowInterceptorGenerationHook : IProxyGenerationHook
    {
        static string[] methodsNotToIntercept = { "ToString", "Equals", "get_EqualityContract", "PrintMembers" };

        public void MethodsInspected()
        {
        }

        public void NonProxyableMemberNotification(Type type, MemberInfo memberInfo)
        {
        }

        public bool ShouldInterceptMethod(Type type, MethodInfo methodInfo)
        {
            return Array.IndexOf(methodsNotToIntercept, methodInfo.Name) == -1;
        }

        public override bool Equals(object? obj)
        {
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}