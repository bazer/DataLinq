using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Interfaces;

namespace DataLinq;

public abstract partial class Database<T> where T : class, IDatabaseModel<T>
{
    /// <summary>Looks up a model by canonical provider key; returns null when absent.</summary>
    /// <remarks>Generated typed-key helpers accept model-side keys. This method accepts provider-side keys.</remarks>
    public ValueTask<M?> GetAsync<M>(DataLinqKey key, CancellationToken cancellationToken = default)
        where M : IImmutableInstance =>
        new(AsyncModelLookup.GetByProviderKeyAsyncCore<M>(key, Provider.ReadOnlyAccess, cancellationToken));
}
