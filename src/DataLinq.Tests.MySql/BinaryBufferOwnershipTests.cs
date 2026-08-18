using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataLinq.MySql;
using DataLinq.Testing;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public sealed class BinaryBufferOwnershipTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ReaderTransfersExactBinaryBuffersAndPreservesEmptyVersusNull(
        TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ReaderTransfersExactBinaryBuffersAndPreservesEmptyVersusNull),
            """
            CREATE TABLE binary_buffer_ownership (
                id INT PRIMARY KEY,
                payload LONGBLOB NULL
            );
            """);
        var payloads = new byte[]?[]
        {
            CreatePayload(32),
            CreatePayload(4096),
            CreatePayload(65536),
            Array.Empty<byte>(),
            null
        };

        using var connection = new MySqlConnection(schema.Connection.ConnectionString);
        connection.Open();
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO binary_buffer_ownership (id, payload) VALUES (@id, @payload)";
            var idParameter = insert.Parameters.Add("@id", MySqlDbType.Int32);
            var payloadParameter = insert.Parameters.Add("@payload", MySqlDbType.LongBlob);
            for (var index = 0; index < payloads.Length; index++)
            {
                idParameter.Value = index;
                payloadParameter.Value = (object?)payloads[index] ?? DBNull.Value;
                insert.ExecuteNonQuery();
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM binary_buffer_ownership ORDER BY id";
        using var mysqlReader = command.ExecuteReader();
        using var reader = new SqlDataLinqDataReader(mysqlReader);
        var ownedReader = (IDataLinqOwnedBinaryBufferReader)reader;
        var transferred = new List<byte[]?>();

        while (reader.ReadNextRow())
            transferred.Add(ownedReader.TakeOwnedBytes(0));

        await Assert.That(transferred.Count).IsEqualTo(payloads.Length);
        for (var index = 0; index < payloads.Length; index++)
        {
            if (payloads[index] is null)
            {
                await Assert.That(transferred[index]).IsNull();
                continue;
            }

            await Assert.That(transferred[index]).IsNotNull();
            await Assert.That(transferred[index]!.Length).IsEqualTo(payloads[index]!.Length);
            await Assert.That(transferred[index]!).IsEquivalentTo(payloads[index]!);
        }

        // Advancing through every server row must not mutate or invalidate a transferred buffer.
        payloads[0]![0] ^= 0xFF;
        await Assert.That(transferred[0]![0]).IsNotEqualTo(payloads[0]![0]);
        await Assert.That(transferred[3]).IsNotNull();
        await Assert.That(transferred[3]!).IsEmpty();
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        for (var index = 0; index < payload.Length; index++)
            payload[index] = unchecked((byte)(index * 31));
        return payload;
    }
}
