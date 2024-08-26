using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq;

public static class IModelExtensions
{
    public static T Insert<T>(this Mutable<T> model, Transaction transaction) where T : IImmutableInstance =>
        transaction.Insert(model);

    public static T Update<T>(this Mutable<T> model, Transaction transaction) where T : IImmutableInstance =>
        transaction.Update(model);

    public static T InsertOrUpdate<T>(this Mutable<T> model, Transaction transaction) where T : IImmutableInstance =>
        transaction.InsertOrUpdate(model);

    public static void Delete<T>(this T model) where T : IImmutableInstance => 
        model.GetDataSource().Provider.Commit(transaction => model.Delete(transaction));

    public static void Delete<T>(this T model, Transaction transaction) where T : IModelInstance => 
        transaction.Delete(model);
}
