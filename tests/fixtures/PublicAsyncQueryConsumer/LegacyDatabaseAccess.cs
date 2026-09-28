using System;
using System.Collections.Generic;
using System.Data;
using DataLinq;
using DataLinq.Interfaces;

internal sealed class LegacyDatabaseAccess : IDatabaseAccess
{
    public int ExecuteNonQuery(IDbCommand command) => throw new Exception("sync");
    public int ExecuteNonQuery(string query) => throw new Exception("sync");
    public object? ExecuteScalar(IDbCommand command) => throw new Exception("sync");
    public object? ExecuteScalar(string query) => throw new Exception("sync");
    public T ExecuteScalar<T>(IDbCommand command) => throw new Exception("sync");
    public T ExecuteScalar<T>(string query) => throw new Exception("sync");
    public IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new Exception("sync");
    public IDataLinqDataReader ExecuteReader(string query) => throw new Exception("sync");
    public IEnumerable<IDataLinqDataReader> ReadReader(IDbCommand command) => throw new Exception("sync");
    public IEnumerable<IDataLinqDataReader> ReadReader(string query) => throw new Exception("sync");
}

internal sealed class LegacyCommand : IDbCommand
{
    private string text = "sql";
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public string CommandText { get => text; set => text = value ?? string.Empty; }
    public int CommandTimeout { get; set; }
    public CommandType CommandType { get; set; }
    public IDbConnection? Connection { get; set; }
    public IDataParameterCollection Parameters => throw new Exception("sync");
    public IDbTransaction? Transaction { get; set; }
    public UpdateRowSource UpdatedRowSource { get; set; }
    public void Cancel() => throw new Exception("sync");
    public IDbDataParameter CreateParameter() => throw new Exception("sync");
    public int ExecuteNonQuery() => throw new Exception("sync");
    public IDataReader ExecuteReader() => throw new Exception("sync");
    public IDataReader ExecuteReader(CommandBehavior behavior) => throw new Exception("sync");
    public object? ExecuteScalar() => throw new Exception("sync");
    public void Prepare() => throw new Exception("sync");
    public void Dispose() => throw new Exception("sync");
}
