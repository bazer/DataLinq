using Castle.DynamicProxy;
using System;
using System.Linq;
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
            //return methodsNotToIntercept.Contains(methodInfo.Name);
            //return methodInfo.Name != "Equals" && methodInfo.Name != "get_EqualityContract";
        }
    }
}