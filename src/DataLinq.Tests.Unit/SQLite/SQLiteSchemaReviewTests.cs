using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Extensions.Hosting;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Query;
using DataLinq.SQLite;
using DataLinq.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SQLiteSchemaReviewTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AnalyzeDoesNotAddApplicationTables(bool memory, bool asynchronous)
    {
        using var fixture = new ReviewFixture(memory);
        await Assert.That((await Validate(fixture, asynchronous)).HasFailures).IsFalse();
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("ANALYZE");
        await Assert.That(fixture.Provider.TableExists("sqlite_stat1")).IsTrue();
        var result = await Validate(fixture, asynchronous);
        await Assert.That(result.HasFailures).IsFalse();
        await Assert.That(result.DatabaseTableCount).IsEqualTo(1);

        // The underscore in SQLite's reserved prefix is literal. A user table
        // beginning with sqlite followed by another character must remain visible.
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE sqliteXstatistics(id INTEGER PRIMARY KEY)");
        var drift = await Validate(fixture, asynchronous);
        await Assert.That(drift.Differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.ExtraTable);
        await Assert.That(drift.Differences.Single().Path).IsEqualTo("sqliteXstatistics");
        var scoped = await Validate(fixture, asynchronous, options => options.Include = ["åROWS"]);
        await Assert.That(scoped.HasFailures).IsFalse();
        await Assert.That(scoped.DatabaseTableCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(DataLinqSchemaStartupMode.FailStartup)]
    [Arguments(DataLinqSchemaStartupMode.WarningOnly)]
    public async Task AnalyzeDoesNotPreventHostStartup(DataLinqSchemaStartupMode mode)
    {
        using var fixture = new ReviewFixture(true);
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("ANALYZE");
        var services = new ServiceCollection();
        services.AddDataLinq<SQLiteReviewDb>("review").UseInstance(new ReviewDatabase(fixture.Provider))
            .AsDefault().ValidateSchemaOnStartup(options => options.Mode = mode);
        using var container = services.BuildServiceProvider();
        await ((IHostedLifecycleService)container.GetServices<IHostedService>().Single()).StartingAsync(default);
        await Assert.That(fixture.Provider.TableExists("årows")).IsTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task NonAsciiTableRenameIsDrift(bool memory, bool asynchronous)
    {
        using var fixture = new ReviewFixture(memory);
        await Assert.That((await Validate(fixture, asynchronous)).HasFailures).IsFalse();
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("ALTER TABLE \"årows\" RENAME TO \"Årows\"");
        await Assert.That(() => fixture.Provider.DatabaseAccess.ExecuteScalar("SELECT count(*) FROM \"årows\""))
            .Throws<SqliteException>();
        var result = await Validate(fixture, asynchronous);
        await Assert.That(result.HasFailures).IsTrue();
        await Assert.That(result.Issues).IsEmpty();
        await Assert.That(result.Differences.Select(difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingTable, SchemaDifferenceKind.ExtraTable]);
        var scoped = await Validate(fixture, asynchronous, options => options.Include = ["åROWS"]);
        await Assert.That(scoped.DatabaseTableCount).IsEqualTo(0);
        await Assert.That(scoped.Differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.MissingTable);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NonAsciiColumnRenameIsDrift(bool asynchronous)
    {
        using var fixture = new ReviewFixture(true);
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("ALTER TABLE \"årows\" RENAME COLUMN \"åvalue\" TO \"Åvalue\"");
        var result = await Validate(fixture, asynchronous);
        await Assert.That(result.Differences.Select(difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingColumn, SchemaDifferenceKind.ExtraColumn]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IncludeRejectsNonAsciiCaseVariantBeforeReading(bool asynchronous)
    {
        using var fixture = new ReviewFixture(true);
        // A valid empty live database still must not make this an accepted scope.
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE \"årows\"");
        await Assert.That(async () => { await Validate(fixture, asynchronous, options => options.Include = ["Årows"]); })
            .Throws<ArgumentException>();
    }

    private static Task<DataLinqSchemaValidationResult> Validate(ReviewFixture fixture, bool asynchronous,
        Action<DataLinqSchemaValidationOptions>? configure = null)
    {
        var database = new ReviewDatabase(fixture.Provider);
        return asynchronous ? database.ValidateSchemaAsync(configure) : Task.FromResult(database.ValidateSchema(configure));
    }

    private sealed class ReviewDatabase(SQLiteProvider<SQLiteReviewDb> provider) : Database<SQLiteReviewDb>(provider);

    private sealed class ReviewFixture : IDisposable
    {
        private readonly string? path;
        internal SQLiteProvider<SQLiteReviewDb> Provider { get; }

        internal ReviewFixture(bool memory)
        {
            var name = "w5_review_" + Guid.NewGuid().ToString("N");
            path = memory ? null : Path.Combine(Path.GetTempPath(), name + ".db");
            Provider = new(new SqliteConnectionStringBuilder
            {
                DataSource = path ?? name, Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
                Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Private, Pooling = false
            }.ConnectionString, name);
            try { Provider.DatabaseAccess.ExecuteNonQuery(Provider.GetCreateSql().Text); }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            Provider.Dispose();
            if (path is null) return;
            File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
        }
    }
}

[Database("sqlite_review")]
public partial class SQLiteReviewDb(IDataLinqReadSource source) : IDatabaseModel
{
    public DbRead<SQLiteReviewRow> Rows { get; } = new(source);
}

[Table("årows")]
public abstract partial class SQLiteReviewRow(IRowData data, IDataSourceAccess source)
    : Immutable<SQLiteReviewRow, SQLiteReviewDb>(data, source), ITableModel<SQLiteReviewDb>
{
    [PrimaryKey, Column("id"), Type(DatabaseType.SQLite, "integer")]
    public abstract int Id { get; }
    [Column("åvalue"), Type(DatabaseType.SQLite, "integer")]
    public abstract int Value { get; }
}
