using System;
using System.Data;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DataLinq.Tests.Unit.Core;

public sealed class CapturedSqlTests
{
    [Test]
    public async Task StatementAndArraysAreDetached_FromInputAndEveryMaterializedCopy()
    {
        var bytes = new byte[] { 1, 2 };
        var scalars = new[] { 3, 4 };
        var sql = new Sql("SELECT @a, @b").AddParameter("@a", bytes).AddParameter("@b", scalars);
        var captured = CapturedSql.Capture(sql);
        bytes[0] = 9;
        scalars[0] = 8;
        sql.AddText(" changed");
        sql.Parameters.Clear();
        var first = captured.ToSql();
        await Assert.That(first.Text).IsEqualTo("SELECT @a, @b");
        await Assert.That(((byte[])first.Parameters[0].Value!)[0]).IsEqualTo((byte)1);
        await Assert.That(((int[])first.Parameters[1].Value!)[0]).IsEqualTo(3);
        ((byte[])first.Parameters[0].Value!)[0] = 7;
        first.Parameters.Clear();
        first.AddText(" also changed");
        var second = captured.ToSql();
        await Assert.That(second.Parameters.Count).IsEqualTo(2);
        await Assert.That(((byte[])second.Parameters[0].Value!)[0]).IsEqualTo((byte)1);
        await Assert.That(second.Text).IsEqualTo("SELECT @a, @b");
    }

    [Test]
    public async Task CloneableProviderParameterCaptureRetainsProviderMetadataAndDetachesValues()
    {
        var bytes = new byte[] { 1, 2, 3 };
        IDataParameter parameter = new MySqlParameter("@value", MySqlDbType.Blob) { Value = bytes, Size = 17 };
        var captured = CapturedSql.Capture(new Sql("SELECT @value", parameter));
        parameter.ParameterName = "@later";
        bytes[0] = 9;
        ((IDbDataParameter)parameter).Size = 99;
        var copy = captured.ToSql().Parameters[0].ProviderParameter!;
        await Assert.That(copy).IsNotSameReferenceAs(parameter);
        await Assert.That(copy.ParameterName).IsEqualTo("@value");
        await Assert.That(((IDbDataParameter)copy).Size).IsEqualTo(17);
        await Assert.That(((byte[])copy.Value!)[0]).IsEqualTo((byte)1);
        await Assert.That(((MySqlParameter)copy).MySqlDbType).IsEqualTo(MySqlDbType.Blob);
        ((byte[])copy.Value!)[0] = 8;
        ((IDbDataParameter)copy).Size = 21;
        var next = captured.ToSql().Parameters[0].ProviderParameter!;
        await Assert.That(((byte[])next.Value!)[0]).IsEqualTo((byte)1);
        await Assert.That(((IDbDataParameter)next).Size).IsEqualTo(17);
    }

    [Test]
    public async Task NonCloneableProviderParameterIsRejectedWithoutDiscardingItsMetadata()
    {
        // Generated SQLite SQL uses ordinary bindings. Native parameter capture needs an
        // explicit provider policy; copying only DbType would silently lose SqliteType.
        var bytes = new byte[] { 1 };
        var parameter = new SqliteParameter("@value", SqliteType.Blob) { Value = bytes, Size = 17 };
        Exception? failure = null;
        try { CapturedSql.Capture(new Sql("SELECT @value", parameter)); }
        catch (Exception error) { failure = error; }
        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(parameter.Value).IsSameReferenceAs(bytes);
        await Assert.That(parameter.SqliteType).IsEqualTo(SqliteType.Blob);
        await Assert.That(parameter.Size).IsEqualTo(17);
    }

    [Test]
    public async Task ScalarReferenceContractIsNotReplacedByArbitraryDeepCloning()
    {
        var scalar = new object();
        var captured = CapturedSql.Capture(new Sql("SELECT @a").AddParameter("@a", scalar));
        await Assert.That(captured.ToSql().Parameters[0].Value).IsSameReferenceAs(scalar);
    }
}
