using System.Collections.Generic;
using System.Data;

namespace DataLinq.Interfaces;

public interface IDatabaseAccess
{
    /// <summary>Executes a caller-owned command. The caller must dispose the returned reader and command.</summary>
    IDataLinqDataReader ExecuteReader(IDbCommand command);
    /// <summary>Creates a command owned by the returned reader. Dispose the reader to release both resources.</summary>
    IDataLinqDataReader ExecuteReader(string query);
    object? ExecuteScalar(IDbCommand command);
    T ExecuteScalar<T>(IDbCommand command);
    object? ExecuteScalar(string query);
    T ExecuteScalar<T>(string query);
    int ExecuteNonQuery(IDbCommand command);
    int ExecuteNonQuery(string query);
    IEnumerable<IDataLinqDataReader> ReadReader(IDbCommand command);
    IEnumerable<IDataLinqDataReader> ReadReader(string query);
}
