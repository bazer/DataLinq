using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using PackedGenerated;

// Compile every accepted M10 receiver using the generator delivered by the nupkg.
// This method is not executed: simultaneously starting conflicting mutations would
// intentionally violate ownership. Runtime lifetime cases live in the provider tests.
internal static class GeneratedBindings
{
    internal static Task<Child>[] Mutations(Child model, MutableChild mutable,
        Database<AdvancedDatabase> database, Transaction transaction, CancellationToken token)
    {
        Action<MutableChild> changes = row => row.Name = "edited";
        return [
            mutable.InsertAsync(database: database),
            mutable.InsertAsync(changes: changes, transaction: transaction, cancellationToken: token),
            mutable.InsertAsync(changes: changes, database: database, cancellationToken: token),
            transaction.InsertAsync(model: mutable, changes: changes, cancellationToken: token),
            model.UpdateAsync(changes: changes),
            model.UpdateAsync(changes: changes, transaction: transaction, cancellationToken: token),
            database.UpdateAsync(model: model, changes: changes, cancellationToken: token),
            transaction.UpdateAsync(model: model, changes: changes, cancellationToken: token),
            mutable.UpdateAsync(database: database, cancellationToken: token),
            model.SaveAsync(changes: changes),
            model.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            database.SaveAsync(model: model, changes: changes, cancellationToken: token),
            transaction.SaveAsync(model: model, changes: changes, cancellationToken: token),
            model.SaveAsync(changes: changes, database: database, cancellationToken: token),
            mutable.SaveAsync(database: database),
            mutable.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            mutable.SaveAsync(changes: changes, database: database, cancellationToken: token),
            mutable.SaveAsync(transaction: transaction, cancellationToken: token),
            transaction.SaveAsync(model: mutable, changes: changes, cancellationToken: token)
        ];
    }

    internal static void Keys(ChildId id, IDataSourceAccess source, Database<AdvancedDatabase> database,
        Transaction<AdvancedDatabase> transaction, CancellationToken token)
    {
        ValueTask<Child?> a = Child.GetAsync(tenant: 7, id: id, dataSource: source);
        ValueTask<Child?> b = Child.GetAsync(tenant: 7, id: id, database: database, cancellationToken: token);
        ValueTask<Child?> c = Child.GetAsync(tenant: 7, id: id, transaction: transaction, cancellationToken: token);
    }

    internal static void Navigation(Child child, CancellationToken token)
    {
        ValueTask<Parent> required = child.ParentAsync(cancellationToken: token);
        ValueTask<Parent?> optional = child.OptionalParentAsync(cancellationToken: token);
    }
}
