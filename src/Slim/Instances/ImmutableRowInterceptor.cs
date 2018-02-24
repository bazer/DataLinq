using Castle.DynamicProxy;
using Slim.Interfaces;
using System;

namespace Slim.Instances
{
    internal class ImmutableRowInterceptor : IInterceptor
    {
        public RowData InstanceData { get; }

        public ImmutableRowInterceptor(RowData rowData)
        {
            InstanceData = rowData;
        }

        public void Intercept(IInvocation invocation)
        {
            var name = invocation.Method.Name;

            if (name.StartsWith("set_", StringComparison.Ordinal))
                throw new Exception("Call to setter not allowed on an immutable type");

            if (!name.StartsWith("get_", StringComparison.Ordinal))
                throw new NotImplementedException();

            name = name.Substring(4);

            invocation.ReturnValue = InstanceData.Data[name];

            //if (name == "Modl")
            //    invocation.ReturnValue = ModlData;
            //else if (name == "IsMutable")
            //    invocation.ReturnValue = false;
            //else if (name == "IsNew")
            //    invocation.ReturnValue = ModlData.Backer.IsNew;
            //else
            //{
            //    var value = ModlData.Backer.SimpleValueBacker.GetValue(name).Get();
            //    invocation.ReturnValue = value;
            //}
        }
    }
}
