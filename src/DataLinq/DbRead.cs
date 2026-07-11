using System;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq;

/// <summary>
/// Represents a class to connect the models to Linq.
/// </summary>
/// <typeparam name="T">The type of the model.</typeparam>
public class DbRead<T> : Queryable<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbRead{T}"/> class.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    public DbRead(DataSourceAccess transaction) : base(transaction, transaction.Provider.Metadata.GetTableModel(typeof(T)).Table) { }

    /// <summary>
    /// Initializes a generated query root from a backend-neutral read source.
    /// </summary>
    public DbRead(IDataLinqReadSource readSource)
        : base(
            readSource,
            (readSource ?? throw new ArgumentNullException(nameof(readSource)))
                .Metadata
                .GetTableModel(typeof(T))
                .Table)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DbRead{T}"/> class.
    /// </summary>
    /// <param name="provider">The query provider.</param>
    /// <param name="expression">The expression.</param>
    public DbRead(IQueryProvider provider, Expression expression) : base(provider, expression) { }
}
