using Castle.DynamicProxy;
using System;
using System.Reflection;

namespace DataLinq.Instances
{
    internal enum CallType
    {
        Get,
        Set,
        Method
    }

    internal enum MethodType
    {
        Property,
        Indexer,
        Method
    }

    internal struct InvocationInfo
    {
        internal CallType CallType { get; }
        internal MethodType MethodType { get; }
        internal string Name { get; }
        internal object? Value { get; }

        internal InvocationInfo(IInvocation invocation)
        {
            var name = invocation.Method.Name;

            if (name.StartsWith("set_", StringComparison.Ordinal))
                this.CallType = CallType.Set;
            else if (name.StartsWith("get_", StringComparison.Ordinal))
                this.CallType = CallType.Get;
            else if (invocation.Method.MemberType == MemberTypes.Method)
                this.CallType = CallType.Method;
            else
                throw new NotImplementedException();

            if ((this.CallType == CallType.Get && invocation.Arguments.Length == 1) || (this.CallType == CallType.Set && invocation.Arguments.Length == 2))
            {
                this.MethodType = MethodType.Indexer;
                this.Name = (string)invocation.Arguments[0];
            }
            else if (this.CallType == CallType.Method)
            {
                this.MethodType = MethodType.Method;
                this.Name = name;
            }
            else
            {
                this.MethodType = MethodType.Property;
                this.Name = name.Substring(4);
            }

            if (this.CallType == CallType.Set && this.MethodType == MethodType.Property)
                this.Value = invocation.Arguments[0];
            else if (this.CallType == CallType.Set && this.MethodType == MethodType.Indexer)
                this.Value = invocation.Arguments[1];
            else if (this.CallType == CallType.Method && invocation.Arguments.Length > 0)
                this.Value = invocation.Arguments[0];
            else
                this.Value = null;
        }
    }
}