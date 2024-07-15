using System;
using System.Data;
using DataLinq.Mutation;

namespace DataLinq;

public enum DatabaseTransactionStatus
{
    Closed,
    Open,
    Committed,
    RolledBack
}

public class DatabaseTransactionStatusChangeEventArgs : EventArgs
{
    public DatabaseTransactionStatus Status { get; set; }
}

public abstract class DatabaseTransaction : DatabaseAccess, IDisposable
{
    public DatabaseTransactionStatus Status { get; private set; } = DatabaseTransactionStatus.Closed;

    public event EventHandler<DatabaseTransactionStatusChangeEventArgs>? OnStatusChanged;
    public IDbTransaction? DbTransaction { get; protected set; }
    public TransactionType Type { get; protected set; }

    protected DatabaseTransaction(TransactionType type)
    {
        Type = type;
    }

    protected DatabaseTransaction(IDbTransaction dbTransaction, TransactionType type)
    {
        DbTransaction = dbTransaction ?? throw new ArgumentNullException(nameof(dbTransaction));
        Type = type;
    }

    protected void SetStatus(DatabaseTransactionStatus status)
    {
        this.Status = status;
        OnStatusChanged?.Invoke(this, new DatabaseTransactionStatusChangeEventArgs { Status = status });
    }

    public abstract void Rollback();
    public abstract void Commit();
    public abstract void Dispose();
}
