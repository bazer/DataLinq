using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Core;

public class DatabaseAccessReaderLifetimeTests
{
    [Test]
    public async Task ReadReader_DisposesReaderAfterCompleteEnumeration()
    {
        var reader = new TrackingDataReader(rowCount: 2);
        var access = new TrackingDatabaseAccess(reader);

        var rows = access.ReadReader("SELECT 1").ToArray();

        await Assert.That(rows.Length).IsEqualTo(2);
        await Assert.That(reader.IsDisposed).IsTrue();
    }

    [Test]
    public async Task ReadReader_DisposesReaderWhenEnumerationStopsEarly()
    {
        var reader = new TrackingDataReader(rowCount: 2);
        var access = new TrackingDatabaseAccess(reader);
        var enumerator = access.ReadReader("SELECT 1").GetEnumerator();

        try
        {
            await Assert.That(enumerator.MoveNext()).IsTrue();
            await Assert.That(reader.IsDisposed).IsFalse();
        }
        finally
        {
            enumerator.Dispose();
        }

        await Assert.That(reader.IsDisposed).IsTrue();
    }

    [Test]
    public async Task ReadReader_DisposesReaderWhenReadingThrows()
    {
        var reader = new TrackingDataReader(rowCount: 2, throwOnReadCall: 2);
        var access = new TrackingDatabaseAccess(reader);
        InvalidOperationException? exception = null;

        try
        {
            _ = access.ReadReader("SELECT 1").ToArray();
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).IsEqualTo("Synthetic reader failure.");
        await Assert.That(reader.IsDisposed).IsTrue();
    }

    private sealed class TrackingDatabaseAccess(TrackingDataReader reader) : DatabaseAccess
    {
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => reader;

        public override IDataLinqDataReader ExecuteReader(string query) => reader;

        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();

        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();

        public override object? ExecuteScalar(string query) => throw new NotSupportedException();

        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();

        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();

        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
    }

    private sealed class TrackingDataReader(int rowCount, int? throwOnReadCall = null) : IDataLinqDataReader
    {
        private int remainingRows = rowCount;
        private int readCalls;

        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;

        public bool ReadNextRow()
        {
            readCalls++;
            if (readCalls == throwOnReadCall)
                throw new InvalidOperationException("Synthetic reader failure.");

            if (remainingRows == 0)
                return false;

            remainingRows--;
            return true;
        }

        public object GetValue(int ordinal) => throw new NotSupportedException();

        public int GetOrdinal(string name) => throw new NotSupportedException();

        public string GetString(int ordinal) => throw new NotSupportedException();

        public bool GetBoolean(int ordinal) => throw new NotSupportedException();

        public int GetInt32(int ordinal) => throw new NotSupportedException();

        public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();

        public Guid GetGuid(int ordinal) => throw new NotSupportedException();

        public byte[]? GetBytes(int ordinal) => throw new NotSupportedException();

        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();

        public T? GetValue<T>(ColumnDefinition column) => throw new NotSupportedException();

        public T? GetValue<T>(ColumnDefinition column, int ordinal) => throw new NotSupportedException();

        public bool IsDbNull(int ordinal) => throw new NotSupportedException();
    }
}
