using Slim.Instances;
using Slim.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Slim.Extensions
{
    public static class IModelExtensions
    {
        public static RowData RowData(this IModel model)
        {
            throw new NotImplementedException();
        }

        public static bool IsNew(this IModel model)
        {
            var type = model.GetType();

            if (type.GetProperty("IsNew") == null)
                return true;

            var method = type
                .GetProperty("IsNew")
                .GetGetMethod();

            var obj = method
                .Invoke(model, new object[] { });

            return obj is bool result && result;


            //var table = DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == model.GetType() || x.Model.ProxyType == model.GetType());
        }

        public static T Mutate<T>(this T model) where T:IModel
        {
            var type = model.GetType();

            if (type.GetProperty("Mutate") == null)
                return model;

            var method = type
                .GetProperty("Mutate")
                .GetGetMethod();

            var obj = method
                .Invoke(model, new object[] { });

            return (T)obj;
        }
    }
}
