using System.Collections.Generic;
using System.Data;
using DataLinq.Interfaces;

namespace DataLinq;

public abstract class DatabaseAccess : IDatabaseAccess
{
    public abstract IDataLinqDataReader ExecuteReader(IDbCommand command);
    public abstract IDataLinqDataReader ExecuteReader(string query);
    public abstract object? ExecuteScalar(IDbCommand command);
    public abstract T ExecuteScalar<T>(IDbCommand command);
    public abstract object? ExecuteScalar(string query);
    public abstract T ExecuteScalar<T>(string query);
    public abstract int ExecuteNonQuery(IDbCommand command);
    public abstract int ExecuteNonQuery(string query);

    public IEnumerable<IDataLinqDataReader> ReadReader(IDbCommand command)
    {
        using var reader = ExecuteReader(command);

        while (reader.ReadNextRow())
            yield return reader;
    }

    public IEnumerable<IDataLinqDataReader> ReadReader(string query)
    {
        using var reader = ExecuteReader(query);

        while (reader.ReadNextRow())
            yield return reader;
    }
}
