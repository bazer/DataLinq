using System;
using System.Reflection;
using Castle.DynamicProxy;

namespace DataLinq.Instances;

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

internal ref struct InvocationInfo
{
    private const string getPrefix = "get_";
    private const string setPrefix = "set_";

    internal CallType CallType { get; }
    internal MethodType MethodType { get; }
    internal ReadOnlyMemory<char> Name { get; }
    internal object? Value { get; }
    internal object[]? Arguments { get; }

    internal InvocationInfo(IInvocation invocation)
    {
        var name = invocation.Method.Name.AsMemory();

        if (name[..4].Span.SequenceEqual(setPrefix.AsSpan()))
            this.CallType = CallType.Set;
        else if (name[..4].Span.SequenceEqual(getPrefix.AsSpan()))
            this.CallType = CallType.Get;
        else if (invocation.Method.MemberType == MemberTypes.Method)
            this.CallType = CallType.Method;
        else
            throw new NotImplementedException();

        if (((int)this.CallType == (int)CallType.Get && invocation.Arguments.Length == 1) || ((int)this.CallType == (int)CallType.Set && invocation.Arguments.Length == 2))
        {
            this.MethodType = MethodType.Indexer;
            this.Name = ((string)invocation.Arguments[0]).AsMemory();
        }
        else if ((int)this.CallType == (int)CallType.Method)
        {
            this.MethodType = MethodType.Method;
            this.Name = name;
        }
        else
        {
            this.MethodType = MethodType.Property;
            this.Name = name[4..];
        }

        if ((int)this.CallType == (int)CallType.Set && (int)this.MethodType == (int)MethodType.Property)
            this.Value = invocation.Arguments[0];
        else if ((int)this.CallType == (int)CallType.Set && (int)this.MethodType == (int)MethodType.Indexer)
            this.Value = invocation.Arguments[1];
        else if ((int)this.CallType == (int)CallType.Method && invocation.Arguments.Length > 0)
            this.Arguments = invocation.Arguments;
        else
            this.Value = null;
    }
}