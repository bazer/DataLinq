using System;
using System.Collections.Generic;
using System.Linq;
using Castle.DynamicProxy;
using Modl.Db.Query;
using Slim.Metadata;

namespace Slim.Instances
{
    internal class ImmutableRowInterceptor : IInterceptor
    {
        public ImmutableRowInterceptor(RowData rowData)
        {
            RowData = rowData;
        }

        protected List<Property> Properties =>
            RowData.Table.Model.Properties;

        protected RowData RowData { get; }

        public void Intercept(IInvocation invocation)
        {
            var name = invocation.Method.Name;

            if (name.StartsWith("set_", StringComparison.Ordinal))
                throw new Exception("Call to setter not allowed on an immutable type");

            if (!name.StartsWith("get_", StringComparison.Ordinal))
                throw new NotImplementedException();

            name = name.Substring(4);

            var property = Properties.Single(x => x.CsName == name);

            if (property.Type == PropertyType.Value)
            {
                invocation.ReturnValue = RowData.Data[property.Column.DbName];
            }
            else
            {
                var column = property.Column;
                var result = column.Table.Cache.GetRows(column, RowData.Data[property.RelationPart.Column.DbName]);

                //var column = property.Column;
                //var select = new Select(RowData.Table.Database.DatabaseProvider, column.Table)
                //    .Where(column.DbName).EqualTo(RowData.Data[property.RelationPart.Column.DbName]);

                //var result = select
                //    .ReadInstances()
                //    .Select(InstanceFactory.NewImmutableRow);

                if (property.RelationPart.Type == RelationPartType.ForeignKey)
                {
                    invocation.ReturnValue = result.SingleOrDefault();
                }
                else
                {
                    var list = result.ToList();

                    if (list.Count != 0)
                    {
                        invocation.ReturnValue = typeof(Enumerable)
                            .GetMethod("Cast")
                            .MakeGenericMethod(list[0].GetType())
                            .Invoke(null, new object[] { list });
                    }
                }
            }
        }
    }
}