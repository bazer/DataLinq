using System.Collections.Generic;
using System.Data;

namespace DataLinq.Interfaces;

public interface IDatabaseAccess
{
    IDataLinqDataReader ExecuteReader(IDbCommand command);
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