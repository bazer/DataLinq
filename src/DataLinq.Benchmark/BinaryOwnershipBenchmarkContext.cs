using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Memory;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DataLinq.Benchmark;

internal sealed class BinaryOwnershipBenchmarkContext : IDisposable
{
    internal const string MemoryProvider = "memory";
    internal const string SQLiteProvider = "sqlite-memory";
    internal const string MySqlProvider = "mysql-8.4";
    private const string ProvidersEnvironmentVariable = "DATALINQ_BINARY_BENCHMARK_PROVIDERS";

    private readonly IDisposable? connection;
    private readonly IDisposable? command;
    private readonly IDataLinqDataReader reader;
    private readonly CanonicalProviderValueRow canonicalRow;
    private readonly RowData modelRow;
    private readonly DataLinqKey canonicalKey;
    private readonly BenchmarkBinaryImmutable immutable;

    internal BinaryOwnershipBenchmarkContext(string providerName, int payloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payloadSize);

        var payload = CreatePayload(payloadSize);
        var database = new MemoryDatabase<CanonicalKeyBenchmarkDatabase>();
        var table = database.Metadata.TableModels
            .Single(model => model.Model.CsType.Type == typeof(CanonicalKeyBenchmarkBinaryRow))
            .Table;

        (reader, connection, command) = CreateReader(providerName, payload, table);
        canonicalRow = CanonicalProviderValueRow.Create(table, [payload]);
        modelRow = ProviderRowMaterializer.Materialize(canonicalRow, "benchmark.binary");
        canonicalKey = canonicalRow.TryCreateCanonicalPrimaryKey(out var key)
            ? key
            : throw new InvalidOperationException("Binary allocation benchmark table has no primary key.");
        immutable = new BenchmarkBinaryImmutable(modelRow, database.ReadSource);
    }

    internal static IEnumerable<string> GetConfiguredProviderNames()
    {
        var configured = Environment.GetEnvironmentVariable(ProvidersEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return [MemoryProvider, SQLiteProvider];

        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MemoryProvider,
            SQLiteProvider,
            MySqlProvider
        };
        var providers = configured
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (providers.Length == 0 || providers.Any(provider => !supported.Contains(provider)))
        {
            throw new InvalidOperationException(
                $"Environment variable '{ProvidersEnvironmentVariable}' must contain one or more of: " +
                $"{string.Join(", ", supported)}.");
        }

        return providers;
    }

    internal byte[] ReadProviderBuffer()
    {
        if (reader.IsDbNull(0))
            throw new InvalidOperationException("Binary allocation benchmark unexpectedly read SQL NULL.");

        return reader is IDataLinqOwnedBinaryBufferReader ownedReader
            ? ownedReader.TakeOwnedBytes(0)
                ?? throw new InvalidOperationException("Owned reader returned SQL NULL.")
            : reader.GetBytes(0)
                ?? throw new InvalidOperationException("Reader returned SQL NULL.");
    }

    internal int DecodeCanonicalRow()
    {
        var row = ProviderRowDecoder.DecodeFullRow(reader, canonicalRow.Table, "benchmark.binary");
        return row.EstimatedPayloadSize;
    }

    internal int MaterializeModelRow() =>
        ProviderRowMaterializer.Materialize(canonicalRow, "benchmark.binary").Size;

    internal int PublishCachedRow()
    {
        var cache = new RowCache();
        if (!cache.TryAddRow(canonicalKey, modelRow, immutable))
            throw new InvalidOperationException("Binary allocation benchmark cache publication failed.");

        return cache.Count;
    }

    internal int ReadDetachedModelValue() =>
        ((byte[])modelRow.GetValue(0)!).Length;

    public void Dispose()
    {
        reader.Dispose();
        command?.Dispose();
        connection?.Dispose();
    }

    private static byte[] CreatePayload(int payloadSize)
    {
        var payload = new byte[payloadSize];
        for (var index = 0; index < payload.Length; index++)
            payload[index] = unchecked((byte)(index * 31));
        return payload;
    }

    private static (IDataLinqDataReader Reader, IDisposable? Connection, IDisposable? Command) CreateReader(
        string providerName,
        byte[] payload,
        TableDefinition table)
    {
        if (string.Equals(providerName, MemoryProvider, StringComparison.OrdinalIgnoreCase))
            return (new MemoryBinaryReader(payload, table.Columns[0]), null, null);

        if (string.Equals(providerName, SQLiteProvider, StringComparison.OrdinalIgnoreCase))
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT $payload";
            command.Parameters.AddWithValue("$payload", payload);
            var dataReader = command.ExecuteReader();
            dataReader.Read();
            return (new SQLiteDataLinqDataReader(dataReader), connection, command);
        }

        if (string.Equals(providerName, MySqlProvider, StringComparison.OrdinalIgnoreCase))
        {
            var connection = new MySqlConnection(
                "Server=127.0.0.1;Port=13307;User ID=datalinq;Password=datalinq;Database=mysql;SslMode=None");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT @payload";
            command.Parameters.AddWithValue("@payload", payload);
            var dataReader = command.ExecuteReader();
            dataReader.Read();
            return (new SqlDataLinqDataReader(dataReader), connection, command);
        }

        throw new InvalidOperationException($"Unsupported binary benchmark provider '{providerName}'.");
    }

    private sealed class BenchmarkBinaryImmutable(IRowData rowData, IDataLinqReadSource readSource)
        : Immutable<CanonicalKeyBenchmarkBinaryRow, CanonicalKeyBenchmarkDatabase>(rowData, readSource);

    private sealed class MemoryBinaryReader(byte[] payload, ColumnDefinition column) : IDataLinqDataReader
    {
        public object GetValue(int ordinal) => payload;
        public int GetOrdinal(string name) => name == column.DbName ? 0 : throw new IndexOutOfRangeException(name);
        public byte[]? GetBytes(int ordinal) => (byte[])payload.Clone();
        public long GetBytes(int ordinal, Span<byte> buffer)
        {
            var length = Math.Min(payload.Length, buffer.Length);
            payload.AsSpan(0, length).CopyTo(buffer);
            return length;
        }
        public T? GetValue<T>(ColumnDefinition requestedColumn) => GetValue<T>(requestedColumn, 0);
        public T? GetValue<T>(ColumnDefinition requestedColumn, int ordinal) => (T?)(object)payload;
        public bool IsDbNull(int ordinal) => false;
        public bool ReadNextRow() => true;
        public string GetString(int ordinal) => throw new NotSupportedException();
        public bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public int GetInt32(int ordinal) => throw new NotSupportedException();
        public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();
        public Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
