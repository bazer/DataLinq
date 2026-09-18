using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq;

public abstract partial class Database<T>
    where T : class, IDatabaseModel<T>
{
    // Internal W1 orchestration. Native binding and public overloads remain W2/W3.
    internal Task<TModel> InsertAsyncCore<TModel>(Mutable<TModel> model,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken token = default)
        where TModel : class, IImmutableInstance
        => Transaction(transactionType).RunMutationHelperAsyncCore<TModel>(model, TransactionChangeType.Insert, token);

    internal Task<TModel> UpdateAsyncCore<TModel>(Mutable<TModel> model,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken token = default)
        where TModel : class, IImmutableInstance
        => Transaction(transactionType).RunMutationHelperAsyncCore<TModel>(model, TransactionChangeType.Update, token);

    internal Task<TModel> SaveAsyncCore<TModel>(Mutable<TModel> model,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken token = default)
        where TModel : class, IImmutableInstance
        => Transaction(transactionType).RunMutationHelperAsyncCore<TModel>(model, null, token);

    internal Task DeleteAsyncCore(IModelInstance model,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken token = default)
        => Transaction(transactionType).RunDeleteHelperAsyncCore(model, token);
}
