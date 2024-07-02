using System.Linq;
using System.Linq.Expressions;
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
    public DbRead(DataSourceAccess transaction) : base(transaction, transaction.Provider.Metadata.TableModels.Single(x => x.Model.CsType == typeof(T)).Table)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DbRead{T}"/> class.
    /// </summary>
    /// <param name="provider">The query provider.</param>
    /// <param name="expression">The expression.</param>
    public DbRead(IQueryProvider provider, Expression expression) : base(provider, expression)
    {
    }
}