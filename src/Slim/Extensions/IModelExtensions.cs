using Slim.Instances;
using Slim.Interfaces;
using Slim.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Slim
{
    public static class IModelExtensions
    {
        public static RowData RowData(this IModel model)
        {
            throw new NotImplementedException();
        }

        public static PrimaryKeys PrimaryKeys(this IModel model)
        {
            var metadata = Model.Find(model);

            if (metadata == null)
                throw new Exception($"Metadata not loaded for model with type {model.GetType()}");

            return new PrimaryKeys(metadata.Table.PrimaryKeyColumns.Select(x => x.ValueProperty.GetValue(model)));
        }

        public static bool HasPrimaryKeysSet(this IModel model)
        {
            var metadata = Model.Find(model);

            if (metadata == null)
                throw new Exception($"Metadata not loaded for model with type {model.GetType()}");

            return metadata.Table
                .PrimaryKeyColumns
                .All(x => x.ValueProperty.GetValue(model) != default);
        }

        public static bool IsNewModel(this IModel model) =>
            model.GetType().GetProperty("Mutate") == null;

        public static T Mutate<T>(this T model) where T:IModel
        {
            var type = model.GetType();

            if (model.IsNewModel())
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
