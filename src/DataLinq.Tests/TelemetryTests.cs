using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DataLinq.Tests;

public sealed class TelemetryTests : IDisposable
{
    public TelemetryTests()
    {
        DataLinqMetrics.Reset();
    }

    public void Dispose()
    {
        DataLinqMetrics.Reset();
    }

    [Fact]
    public void SnapshotCapturesCommandMetricsForSQLite()
    {
        using var db = CreateTelemetryDatabase();
        DataLinqMetrics.Reset();

        Assert.Equal(1, db.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('alpha')"));
        Assert.Equal(1L, Convert.ToInt64(db.Provider.DatabaseAccess.ExecuteScalar("SELECT COUNT(*) FROM telemetryrows")));

        using (var reader = db.Provider.DatabaseAccess.ExecuteReader("SELECT id, name FROM telemetryrows ORDER BY id"))
        {
            Assert.True(reader.ReadNextRow());
            Assert.Equal("alpha", reader.GetString(1));
            Assert.False(reader.ReadNextRow());
        }

        var snapshot = DataLinqMetrics.Snapshot();
        var provider = Assert.Single(snapshot.Providers);

        Assert.Equal(DatabaseType.SQLite, provider.DatabaseType);
        Assert.True(snapshot.Commands.NonQueryExecutions >= 1);
        Assert.Equal(1, snapshot.Commands.ScalarExecutions);
        Assert.Equal(1, snapshot.Commands.ReaderExecutions);
        Assert.True(snapshot.Commands.TotalExecutions >= 3);
        Assert.Equal(0, snapshot.Commands.Failures);
        Assert.True(snapshot.Commands.TotalDurationMicroseconds > 0);
        Assert.Equal(snapshot.Commands, provider.Commands);
    }

    [Fact]
    public void SnapshotCapturesTransactionMetricsForSQLite()
    {
        using var db = CreateTelemetryDatabase();
        DataLinqMetrics.Reset();

        using (var transaction = db.Transaction())
        {
            Assert.Equal(1, transaction.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('beta')"));
            transaction.Commit();
        }

        var snapshot = DataLinqMetrics.Snapshot();
        var provider = Assert.Single(snapshot.Providers);

        Assert.Equal(1, snapshot.Transactions.Starts);
        Assert.Equal(1, snapshot.Transactions.Commits);
        Assert.Equal(0, snapshot.Transactions.Rollbacks);
        Assert.Equal(0, snapshot.Transactions.Failures);
        Assert.True(snapshot.Transactions.TotalDurationMicroseconds > 0);
        Assert.Equal(snapshot.Transactions, provider.Transactions);
        Assert.True(snapshot.Commands.NonQueryExecutions >= 1);
    }

    [Fact]
    public void ActivitiesExposeCommandAndTransactionMetadataForSQLite()
    {
        using var db = CreateTelemetryDatabase();
        DataLinqMetrics.Reset();
        var stoppedActivities = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DataLinq",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stoppedActivities.Add(activity)
        };

        ActivitySource.AddActivityListener(listener);

        using (var transaction = db.Transaction())
        {
            Assert.Equal(1, transaction.DatabaseAccess.ExecuteNonQuery("INSERT INTO telemetryrows (name) VALUES ('gamma')"));
            transaction.Commit();
        }

        var commandActivities = stoppedActivities
            .Where(x => x.OperationName == "datalinq.db.command")
            .ToArray();

        Assert.Equal(2, commandActivities.Length);

        var setupCommandActivity = Assert.Single(commandActivities, x => Equals(x.GetTagItem("datalinq.transactional"), false));
        Assert.Equal(ActivityKind.Client, setupCommandActivity.Kind);
        Assert.Equal("sqlite", setupCommandActivity.GetTagItem("db.system"));
        Assert.Equal("non_query", setupCommandActivity.GetTagItem("datalinq.command.kind"));

        var transactionalCommandActivity = Assert.Single(commandActivities, x => Equals(x.GetTagItem("datalinq.transactional"), true));
        Assert.Equal(ActivityKind.Client, transactionalCommandActivity.Kind);
        Assert.Equal("sqlite", transactionalCommandActivity.GetTagItem("db.system"));
        Assert.Equal("non_query", transactionalCommandActivity.GetTagItem("datalinq.command.kind"));
        Assert.Equal("read_write", transactionalCommandActivity.GetTagItem("datalinq.transaction.type"));

        var transactionActivity = Assert.Single(stoppedActivities, x => x.OperationName == "datalinq.db.transaction");
        Assert.Equal(ActivityKind.Client, transactionActivity.Kind);
        Assert.Equal("sqlite", transactionActivity.GetTagItem("db.system"));
        Assert.Equal("read_write", transactionActivity.GetTagItem("datalinq.transaction.type"));
        Assert.Equal("commit", transactionActivity.GetTagItem("datalinq.outcome"));
    }

    private static SQLiteDatabase<SQLiteTelemetryDb> CreateTelemetryDatabase()
    {
        var databaseName = $"sqlite_telemetry_{Guid.NewGuid():N}";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseName,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ConnectionString;

        var db = new SQLiteDatabase<SQLiteTelemetryDb>(connectionString, databaseName);

        var createResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            db.Provider.Metadata,
            databaseName,
            connectionString,
            true);

        if (createResult.HasFailed)
        {
            db.Dispose();
            Assert.Fail(createResult.Failure.ToString());
        }

        return db;
    }
}

[Database("sqlitetelemetry")]
public sealed partial class SQLiteTelemetryDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<SQLiteTelemetryRow> Rows { get; } = new(dataSource);
}

[Table("telemetryrows")]
public abstract partial class SQLiteTelemetryRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<SQLiteTelemetryRow, SQLiteTelemetryDb>(rowData, dataSource), ITableModel<SQLiteTelemetryDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("id")]
    public abstract int? Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Column("name")]
    public abstract string Name { get; }
}
