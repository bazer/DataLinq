using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using DataLinq.Interfaces;

namespace DataLinq.Mutation;

public abstract partial class DataSourceAccess
{
    /// <summary>Enumerates models from captured SQL without synchronous fallback.</summary>
    /// <remarks>Legacy custom sources must explicitly implement asynchronous raw model reads.</remarks>
    public virtual IAsyncEnumerable<T> GetFromQueryAsync<T>(string query, CancellationToken cancellationToken = default) where T : IModel
        => throw new NotSupportedException("This data source does not support asynchronous raw model reads.");

    /// <summary>Enumerates models using a borrowed command, which must remain stable while active.</summary>
    /// <remarks>The sequence owns its reader but does not dispose the caller's command or transaction.</remarks>
    public virtual IAsyncEnumerable<T> GetFromCommandAsync<T>(IDbCommand dbCommand, CancellationToken cancellationToken = default) where T : IModel
        => throw new NotSupportedException("This data source does not support asynchronous raw model reads.");
}

public partial class ReadOnlyAccess
{
    /// <inheritdoc/>
    public override IAsyncEnumerable<T> GetFromQueryAsync<T>(string query, CancellationToken cancellationToken = default)
        => GetFromQueryAsyncCore<T>(query, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<T> GetFromCommandAsync<T>(IDbCommand dbCommand, CancellationToken cancellationToken = default)
        => GetFromCommandAsyncCore<T>(dbCommand, cancellationToken);
}

public partial class Transaction
{
    /// <inheritdoc/>
    public override IAsyncEnumerable<T> GetFromQueryAsync<T>(string query, CancellationToken cancellationToken = default)
        => GetFromQueryAsyncCore<T>(query, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<T> GetFromCommandAsync<T>(IDbCommand dbCommand, CancellationToken cancellationToken = default)
        => GetFromCommandAsyncCore<T>(dbCommand, cancellationToken);
}
