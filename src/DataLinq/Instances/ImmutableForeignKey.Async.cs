using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public partial class ImmutableForeignKey<T, TKey>
    where T : IImmutableInstance where TKey : notnull
{
    internal Task<T?> GetValueAsyncCore(CancellationToken token = default)
        => LoadValueAsyncCore(required: false, token);

    internal async Task<T> GetRequiredValueAsyncCore(CancellationToken token = default)
        => (await LoadValueAsyncCore(required: true, token).ConfigureAwait(false))!;

    private async Task<T?> LoadValueAsyncCore(bool required, CancellationToken token)
    {
        var source = GetDataSource();
        using var read = DataSourceAccess.BeginRead(source, "load an asynchronous relation reference");
        try
        {
            if (ProviderKeyComponents.HasNullReferenceComponent(foreignKey))
            {
                token.ThrowIfCancellationRequested();
                return RequireValue(default);
            }
            var table = GetTableCache(source);
            var prepared = table.PrepareRelationRowsAsyncCore(foreignKey, property, source, read?.Step);
            token.ThrowIfCancellationRequested();
            var current = Volatile.Read(ref valueHolder);
            if (current is not null && ReferenceEquals(current.Source, source))
            {
                table.MetricsHandle.RecordRelationReferenceCacheHit();
                return RequireValue(current.Value);
            }
            await loadSlot.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                current = Volatile.Read(ref valueHolder);
                if (current is not null && ReferenceEquals(current.Source, source))
                {
                    table.MetricsHandle.RecordRelationReferenceCacheHit();
                    return RequireValue(current.Value);
                }
                object generation;
                lock (loadLock) generation = clearGeneration;
                var readGeneration = table.CaptureReadGeneration();
                return await table.ExecuteRelationRowsAsyncCore(prepared, rows =>
                {
                    token.ThrowIfCancellationRequested();
                    var instance = RequireValue(rows.Length == 0 ? default : (T?)rows.Single());
                    return PublishInstance(source, table, instance, generation, readGeneration);
                }, token).ConfigureAwait(false);
            }
            finally { loadSlot.Release(); }
        }
        catch (Exception failure) { read?.ReportFailure(failure); throw; }

        T? RequireValue(T? value) => required && value is null
            ? throw new InvalidOperationException($"Required relation '{property.Model.CsType.Name}.{property.PropertyName}' did not resolve to a target row.")
            : value;
    }
}
