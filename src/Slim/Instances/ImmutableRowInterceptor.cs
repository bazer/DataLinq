using System;
using Castle.DynamicProxy;

namespace Slim.Instances
{
    internal class ImmutableRowInterceptor : IInterceptor
    {
        public ImmutableRowInterceptor(RowData rowData)
        {
            RowData = rowData;
        }

        public RowData RowData { get; }

        public void Intercept(IInvocation invocation)
        {
            var name = invocation.Method.Name;

            if (name.StartsWith("set_", StringComparison.Ordinal))
                throw new Exception("Call to setter not allowed on an immutable type");

            if (!name.StartsWith("get_", StringComparison.Ordinal))
                throw new NotImplementedException();

            name = name.Substring(4);
            invocation.ReturnValue = RowData.Data[name];
        }
    }
}