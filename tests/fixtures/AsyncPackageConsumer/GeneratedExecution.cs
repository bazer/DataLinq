using DataLinq;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.SQLite;
using PackedGenerated;

internal static class GeneratedExecution
{
    internal static async Task RunAsync()
    {
        var name = "generated_" + Guid.NewGuid().ToString("N");
        await using var database = new SQLiteDatabase<AdvancedDatabase>($"Data Source={name};Mode=Memory;Cache=Shared", name);
        var created = await PluginHook.CreateDatabaseFromMetadataAsync(DatabaseType.SQLite,
            database.Provider.Metadata, name, database.Provider.ConnectionString, true);
        if (created.HasFailed) throw new Exception(created.Failure.ToString());
        var parent = await database.InsertAsync(new MutableParent { Id = 3 });
        var child = await new MutableChild { Tenant = 7, Id = new(17), Name = "child", ParentId = 3 }
            .InsertAsync(changes: model => model.Name = "edited", database: database);
        if ((await Child.GetAsync(7, new(17), database))?.Name != "edited") throw new Exception("Composite/converted key lookup failed.");
        if ((await Child.GetAsync(7, new(17), database.Provider.ReadOnlyAccess))?.Id != new ChildId(17)) throw new Exception("Source lookup failed.");
        if ((await child.ParentAsync()).Id != parent.Id || await child.OptionalParentAsync() is not null) throw new Exception("Required/optional navigation failed.");
        if (child.Parent.Id != parent.Id || child.OptionalParent is not null) throw new Exception("Shared sync navigation failed.");
        if (await parent.Children.CountAsync() != 1 || (await parent.Children.ToFrozenDictionaryAsync()).Count != 1) throw new Exception("Composite-key relation failed.");
        await using var transaction = database.Transaction();
        if ((await Child.GetAsync(7, new(17), transaction))?.Tenant != 7) throw new Exception("Transaction key lookup failed.");
        await transaction.RollbackAsync();
    }
}
