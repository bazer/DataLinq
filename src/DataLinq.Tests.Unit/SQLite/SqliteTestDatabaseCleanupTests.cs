using System;
using System.IO;
using System.Threading.Tasks;
using DataLinq.Testing;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SqliteTestDatabaseCleanupTests
{
    [Test]
    public async Task DeleteSqliteFile_ClearsOnlyTheDeletedDatabasePool()
    {
        var root = Path.Combine(Path.GetTempPath(), $"datalinq-sqlite-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var deletedPath = Path.Combine(root, "deleted.db");
        using var deleted = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = deletedPath,
            Pooling = true
        }.ToString());
        using var unrelated = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "unrelated.db"),
            Pooling = true
        }.ToString());

        try
        {
            deleted.Open();
            var deletedHandle = deleted.Handle!;
            deleted.Close();
            unrelated.Open();
            var unrelatedHandle = unrelated.Handle!;
            unrelated.Close();

            TestDatabaseLifecycle.DeleteSqliteFile(deleted.ConnectionString);

            await Assert.That(File.Exists(deletedPath)).IsFalse();
            await Assert.That(deletedHandle.IsClosed).IsTrue();
            await Assert.That(unrelatedHandle.IsClosed).IsFalse();
            unrelated.Open();
            await Assert.That(unrelated.Handle).IsSameReferenceAs(unrelatedHandle);
            using var command = unrelated.CreateCommand();
            command.CommandText = "SELECT 1;";
            await Assert.That(Convert.ToInt64(command.ExecuteScalar())).IsEqualTo(1L);
        }
        finally
        {
            unrelated.Close();
            deleted.Close();
            SqliteConnection.ClearPool(unrelated);
            SqliteConnection.ClearPool(deleted);
            Directory.Delete(root, recursive: true);
        }
    }
}
