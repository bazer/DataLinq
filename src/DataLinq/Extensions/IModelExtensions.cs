using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using System;
using System.Linq;

namespace DataLinq
{
    public static class IModelExtensions
    {
        public static RowData RowData(this IModel model)
        {
            throw new NotImplementedException();
        }

        public static PrimaryKeys PrimaryKeys(this IModel model)
        {
            return new PrimaryKeys(model.Metadata().Table.PrimaryKeyColumns.Select(x => x.ValueProperty.GetValue(model)));
        }

        internal static PrimaryKeys PrimaryKeys(this IModel model, ModelMetadata metadata)
        {
            return new PrimaryKeys(metadata.Table.PrimaryKeyColumns.Select(x => x.ValueProperty.GetValue(model)));
        }

        public static bool HasPrimaryKeysSet(this IModel model)
        {
            return model.Metadata().Table
                .PrimaryKeyColumns
                .All(x => x.ValueProperty.GetValue(model) != default);
        }

        public static ModelMetadata Metadata(this IModel model)
        {
            var metadata = ModelMetadata.Find(model);

            if (metadata == null)
                throw new Exception($"Metadata not loaded for model with type {model.GetType()}");

            return metadata;
        }

        public static bool IsNewModel(this IModel model) =>
            !typeof(InstanceBase).IsAssignableFrom(model.GetType());

        public static T Mutate<T>(this T model) where T : IModel
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

        public static T Insert<T>(this T model, Transaction transaction) where T : IModel
        {
            return transaction.Insert(model);
        }

        public static T Update<T>(this T model, Transaction transaction) where T : IModel
        {
            return transaction.Update(model);
        }

        public static T Update<T>(this T model, Action<T> changes, Transaction transaction) where T : IModel
        {
            return transaction.Update(model, changes);
        }

        public static T InsertOrUpdate<T>(this T model, Transaction transaction) where T : IModel
        {
            return transaction.InsertOrUpdate(model);
        }

        public static T InsertOrUpdate<T>(this T model, Action<T> changes, Transaction transaction) where T : IModel, new()
        {
            return transaction.InsertOrUpdate(model, changes);
        }

        public static void Delete<T>(this T model, Transaction transaction) where T : IModel
        {
            transaction.Delete(model);
        }
    }
}
