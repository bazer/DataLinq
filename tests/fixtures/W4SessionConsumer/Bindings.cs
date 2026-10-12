using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace SessionConsumer;

// Compile-only: starting these overlapping operations would intentionally violate
// transaction admission. The executable and provider tests verify actual ownership.
// Keep the call expressions identical across the four static source forms.
public static class Bindings
{
    public static void Base<TDatabase>(Database<TDatabase> database, TestRow model, MutableTestRow mutable, CancellationToken token, Transaction transaction)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        Action<MutableTestRow> changes = row => row.Value = "changed";
        TestRow[] sync = [
            mutable.Insert(changes: changes, transaction: transaction),
            transaction.Insert(model: mutable, changes: changes),
            model.Update(changes: changes, transaction: transaction),
            transaction.Update(model: model, changes: changes),
            model.Save(changes: changes, transaction: transaction),
            transaction.Save(model: model, changes: changes),
            mutable.Save(changes: changes, transaction: transaction),
            mutable.Save(transaction: transaction),
            transaction.Save(model: mutable, changes: changes),
            TestRowExtensions.Insert(mutable, changes, transaction),
            TestRowExtensions.Update(transaction, model, changes),
            TestRowExtensions.Save(mutable, transaction)
        ];
        Task<TestRow>[] async = [
            mutable.InsertAsync(changes: changes, transaction: transaction),
            transaction.InsertAsync(model: mutable, changes: changes, cancellationToken: token),
            model.UpdateAsync(changes: changes, transaction: transaction, cancellationToken: token),
            transaction.UpdateAsync(model: model, changes: changes),
            model.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            transaction.SaveAsync(model: model, changes: changes),
            mutable.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            mutable.SaveAsync(transaction: transaction),
            transaction.SaveAsync(model: mutable, changes: changes, cancellationToken: token),
            TestRowExtensions.InsertAsync(mutable, changes, transaction, token),
            TestRowExtensions.UpdateAsync(transaction, model, changes),
            TestRowExtensions.SaveAsync(mutable, transaction, token)
        ];
        Func<MutableTestRow, Action<MutableTestRow>, Transaction, TestRow> syncGroup = TestRowExtensions.Insert;
        Func<Transaction, TestRow, Action<MutableTestRow>, CancellationToken, Task<TestRow>> asyncGroup = TestRowExtensions.SaveAsync;
    }

    public static void Typed<TDatabase>(Database<TDatabase> database, TestRow model, MutableTestRow mutable, CancellationToken token, Transaction<TDatabase> transaction)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        Action<MutableTestRow> changes = row => row.Value = "changed";
        TestRow[] sync = [
            mutable.Insert(changes: changes, transaction: transaction),
            transaction.Insert(model: mutable, changes: changes),
            model.Update(changes: changes, transaction: transaction),
            transaction.Update(model: model, changes: changes),
            model.Save(changes: changes, transaction: transaction),
            transaction.Save(model: model, changes: changes),
            mutable.Save(changes: changes, transaction: transaction),
            mutable.Save(transaction: transaction),
            transaction.Save(model: mutable, changes: changes),
            TestRowExtensions.Insert(mutable, changes, transaction),
            TestRowExtensions.Update(transaction, model, changes),
            TestRowExtensions.Save(mutable, transaction)
        ];
        Task<TestRow>[] async = [
            mutable.InsertAsync(changes: changes, transaction: transaction),
            transaction.InsertAsync(model: mutable, changes: changes, cancellationToken: token),
            model.UpdateAsync(changes: changes, transaction: transaction, cancellationToken: token),
            transaction.UpdateAsync(model: model, changes: changes),
            model.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            transaction.SaveAsync(model: model, changes: changes),
            mutable.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            mutable.SaveAsync(transaction: transaction),
            transaction.SaveAsync(model: mutable, changes: changes, cancellationToken: token),
            TestRowExtensions.InsertAsync(mutable, changes, transaction, token),
            TestRowExtensions.UpdateAsync(transaction, model, changes),
            TestRowExtensions.SaveAsync(mutable, transaction, token)
        ];
        Func<MutableTestRow, Action<MutableTestRow>, Transaction<TDatabase>, TestRow> syncGroup = TestRowExtensions.Insert;
        Func<Transaction<TDatabase>, TestRow, Action<MutableTestRow>, CancellationToken, Task<TestRow>> asyncGroup = TestRowExtensions.SaveAsync;
    }

    public static void Participant<TDatabase>(Database<TDatabase> database, TestRow model, MutableTestRow mutable, CancellationToken token, IDataLinqSession<TDatabase> transaction)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        Action<MutableTestRow> changes = row => row.Value = "changed";
        TestRow[] sync = [
            mutable.Insert(changes: changes, transaction: transaction),
            transaction.Insert(model: mutable, changes: changes),
            model.Update(changes: changes, transaction: transaction),
            transaction.Update(model: model, changes: changes),
            model.Save(changes: changes, transaction: transaction),
            transaction.Save(model: model, changes: changes),
            mutable.Save(changes: changes, transaction: transaction),
            mutable.Save(transaction: transaction),
            transaction.Save(model: mutable, changes: changes),
            TestRowExtensions.Insert(mutable, changes, transaction),
            TestRowExtensions.Update(transaction, model, changes),
            TestRowExtensions.Save(mutable, transaction)
        ];
        Task<TestRow>[] async = [
            mutable.InsertAsync(changes: changes, transaction: transaction),
            transaction.InsertAsync(model: mutable, changes: changes, cancellationToken: token),
            model.UpdateAsync(changes: changes, transaction: transaction, cancellationToken: token),
            transaction.UpdateAsync(model: model, changes: changes),
            model.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            transaction.SaveAsync(model: model, changes: changes),
            mutable.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            mutable.SaveAsync(transaction: transaction),
            transaction.SaveAsync(model: mutable, changes: changes, cancellationToken: token),
            TestRowExtensions.InsertAsync(mutable, changes, transaction, token),
            TestRowExtensions.UpdateAsync(transaction, model, changes),
            TestRowExtensions.SaveAsync(mutable, transaction, token)
        ];
        Func<MutableTestRow, Action<MutableTestRow>, IDataLinqSession<TDatabase>, TestRow> syncGroup = TestRowExtensions.Insert;
        Func<IDataLinqSession<TDatabase>, TestRow, Action<MutableTestRow>, CancellationToken, Task<TestRow>> asyncGroup = TestRowExtensions.SaveAsync;
    }

    public static void Inferred<TDatabase>(Database<TDatabase> database, TestRow model, MutableTestRow mutable, CancellationToken token)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var transaction = database.Transaction();
        Action<MutableTestRow> changes = row => row.Value = "changed";
        TestRow[] sync = [
            mutable.Insert(changes: changes, transaction: transaction),
            transaction.Insert(model: mutable, changes: changes),
            model.Update(changes: changes, transaction: transaction),
            transaction.Update(model: model, changes: changes),
            model.Save(changes: changes, transaction: transaction),
            transaction.Save(model: model, changes: changes),
            mutable.Save(changes: changes, transaction: transaction),
            mutable.Save(transaction: transaction),
            transaction.Save(model: mutable, changes: changes),
            TestRowExtensions.Insert(mutable, changes, transaction),
            TestRowExtensions.Update(transaction, model, changes),
            TestRowExtensions.Save(mutable, transaction)
        ];
        Task<TestRow>[] async = [
            mutable.InsertAsync(changes: changes, transaction: transaction),
            transaction.InsertAsync(model: mutable, changes: changes, cancellationToken: token),
            model.UpdateAsync(changes: changes, transaction: transaction, cancellationToken: token),
            transaction.UpdateAsync(model: model, changes: changes),
            model.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            transaction.SaveAsync(model: model, changes: changes),
            mutable.SaveAsync(changes: changes, transaction: transaction, cancellationToken: token),
            mutable.SaveAsync(transaction: transaction),
            transaction.SaveAsync(model: mutable, changes: changes, cancellationToken: token),
            TestRowExtensions.InsertAsync(mutable, changes, transaction, token),
            TestRowExtensions.UpdateAsync(transaction, model, changes),
            TestRowExtensions.SaveAsync(mutable, transaction, token)
        ];
        Func<MutableTestRow, Action<MutableTestRow>, Transaction<TDatabase>, TestRow> syncGroup = TestRowExtensions.Insert;
        Func<Transaction<TDatabase>, TestRow, Action<MutableTestRow>, CancellationToken, Task<TestRow>> asyncGroup = TestRowExtensions.SaveAsync;
    }

}
