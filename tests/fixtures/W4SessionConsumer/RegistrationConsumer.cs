using DataLinq;
using DataLinq.Attributes;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.SQLite;
using Microsoft.Extensions.DependencyInjection;
using PackedGenerated;

namespace PackedGenerated;

internal static class RegistrationConsumer
{
    internal static async Task Run()
    {
        foreach (var asynchronous in new[] { false, true })
        {
            var constructed = 0;
            CountingProvider? provider = null;
            var services = new ServiceCollection();
            services.AddDataLinq<AdvancedDatabase>("owned").AsDefault().UseFactory(_ =>
            {
                constructed++;
                return new CountingDatabase(provider = new());
            });
            var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            if (constructed != 0) throw new Exception("Registration eagerly constructed the database.");
            using (var scope = container.CreateScope())
            {
                var factory = scope.ServiceProvider.GetRequiredService<IDataLinqUnitOfWorkFactory<AdvancedDatabase>>();
                var database = scope.ServiceProvider.GetRequiredKeyedService<CountingDatabase>("owned");
                if (!ReferenceEquals(database, scope.ServiceProvider.GetRequiredService<Database<AdvancedDatabase>>()) ||
                    !ReferenceEquals(database.Query(), scope.ServiceProvider.GetRequiredService<AdvancedDatabase>()) ||
                    !ReferenceEquals(database.Provider.ReadOnlyAccess, scope.ServiceProvider.GetRequiredKeyedService<ReadOnlyAccess<AdvancedDatabase>>("owned")))
                    throw new Exception("Registration aliases lost singleton identity.");
                factory.Execute(_ => { });
                await factory.ExecuteAsync(_ => Task.CompletedTask);
            }
            if (provider!.Calls != 0 || constructed != 1) throw new Exception("Scope disposal changed root ownership.");
            if (asynchronous) await container.DisposeAsync(); else container.Dispose();
            if (provider.Calls != 1 || provider.WasAsync != asynchronous) throw new Exception("Custom provider cleanup was duplicated or used the wrong mode.");

            await using var borrowed = new SQLiteDatabase<DisposableReadDatabase>("Data Source=:memory:");
            var readModel = borrowed.Query();
            var borrowedServices = new ServiceCollection();
            borrowedServices.AddDataLinq<DisposableReadDatabase>("borrowed").UseInstance(borrowed).AsDefault();
            var borrowedContainer = borrowedServices.BuildServiceProvider();
            if (!ReferenceEquals(readModel, borrowedContainer.GetRequiredService<DisposableReadDatabase>()) ||
                !ReferenceEquals(readModel, borrowedContainer.GetRequiredKeyedService<DisposableReadDatabase>("borrowed")))
                throw new Exception("Borrowed disposable model identity changed.");
            if (asynchronous) await borrowedContainer.DisposeAsync(); else borrowedContainer.Dispose();
            if (readModel.Calls != 0) throw new Exception("The container disposed a borrowed generated model.");
            var invalid = new ServiceCollection().AddDataLinq<DisposableReadDatabase>();
            try
            {
                invalid.UseInstance(borrowed, DataLinqOwnership.Container);
                throw new Exception("Owned disposable model registration was accepted.");
            }
            catch (NotSupportedException) { }
            readModel.Dispose();
        }
    }

    private sealed class CountingDatabase(CountingProvider provider) : Database<AdvancedDatabase>(provider);
    private sealed class CountingProvider() : SQLiteProvider<AdvancedDatabase>("Data Source=:memory:")
    {
        internal int Calls { get; private set; }
        internal bool WasAsync { get; private set; }
        public override void Dispose() { Calls++; base.Dispose(); }
        public override ValueTask DisposeAsync() { Calls++; WasAsync = true; return base.DisposeAsync(); }
    }
}

[Database("w4_disposable_read")]
public sealed partial class DisposableReadDatabase(IDataLinqReadSource source) : IDatabaseModel, IDisposable, IAsyncDisposable
{
    public DbRead<DisposableReadRow> Rows { get; } = new(source);
    public int Calls { get; private set; }
    public void Dispose() => Calls++;
    public ValueTask DisposeAsync() { Calls++; return default; }
}

[Table("rows")]
public abstract partial class DisposableReadRow(IRowData row, IDataLinqReadSource source)
    : Immutable<DisposableReadRow, DisposableReadDatabase>(row, source), ITableModel<DisposableReadDatabase>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
}
