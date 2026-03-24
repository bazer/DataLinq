using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using DataLinq.Attributes;
using DataLinq.Config;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Remotion.Linq.Parsing.Structure;
using Xunit;

namespace DataLinq.Tests;

public class CharPredicateTests : BaseTests
{
    public static IEnumerable<object[]> GetConfiguredConnections()
    {
        foreach (var connection in Fixture.EmployeeConnections
            .GroupBy(x => x.Type)
            .Select(group => group.Key == DatabaseType.SQLite
                ? group.FirstOrDefault(IsInMemorySQLiteConnection) ?? group.First()
                : group.First()))
        {
            yield return [connection];
        }
    }

    [Theory]
    [MemberData(nameof(GetConfiguredConnections), DisableDiscoveryEnumeration = true)]
    public void SqlQueryCharPredicateMatchesRawTextChar(DataLinqDatabaseConnection connection)
    {
        using var tempDatabase = TempCharPredicateDatabase.Create(connection);

        var inserted = tempDatabase.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO charpredicaterows (status) VALUES ('N')");

        Assert.Equal(1, inserted);

        var keys = new SqlQuery<CharPredicateRow>(tempDatabase.Database.Provider.ReadOnlyAccess)
            .Where("status")
            .EqualTo('N')
            .SelectQuery()
            .ReadKeys()
            .ToArray();

        Assert.Single(keys);
    }

    [Theory]
    [MemberData(nameof(GetConfiguredConnections), DisableDiscoveryEnumeration = true)]
    public void LinqCharPredicateMatchesRawTextChar(DataLinqDatabaseConnection connection)
    {
        using var tempDatabase = TempCharPredicateDatabase.Create(connection);

        var inserted = tempDatabase.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO charpredicaterows (status) VALUES ('N')");

        Assert.Equal(1, inserted);

        var row = tempDatabase.Database.Query().Rows.SingleOrDefault(x => x.Status == 'N');
        Assert.NotNull(row);
        Assert.Equal('N', row.Status);
    }

    [Fact]
    public void LinqCharPredicateTranslatesDifferentlyThanDirectSqlQuery()
    {
        var sqliteConnection = Fixture.EmployeeConnections
            .First(IsInMemorySQLiteConnection);

        using var tempDatabase = TempCharPredicateDatabase.Create(sqliteConnection);

        var directSelect = new SqlQuery<CharPredicateRow>(tempDatabase.Database.Provider.ReadOnlyAccess)
            .Where("status")
            .EqualTo('N')
            .SelectQuery();

        var linqQuery = tempDatabase.Database.Query().Rows.Where(x => x.Status == 'N');
        var linqSelect = BuildLinqSelect(tempDatabase.Database, linqQuery);

        var directSql = directSelect.ToSql();
        var linqSql = linqSelect.ToSql();

        Assert.Equal(directSql.Text, linqSql.Text);
        Assert.Equal(directSql.Parameters.Single().Value, linqSql.Parameters.Single().Value);
        Assert.Equal(directSql.Parameters.Single().Value?.GetType(), linqSql.Parameters.Single().Value?.GetType());
    }

    private sealed class TempCharPredicateDatabase : IDisposable
    {
        private readonly DataLinqDatabaseConnection sourceConnection;
        private readonly string cleanupTarget;
        private readonly bool cleanupFile;

        public Database<CharPredicateDb> Database { get; }

        private TempCharPredicateDatabase(
            DataLinqDatabaseConnection sourceConnection,
            Database<CharPredicateDb> database,
            string cleanupTarget,
            bool cleanupFile)
        {
            this.sourceConnection = sourceConnection;
            Database = database;
            this.cleanupTarget = cleanupTarget;
            this.cleanupFile = cleanupFile;
        }

        public static TempCharPredicateDatabase Create(DataLinqDatabaseConnection connection)
        {
            var providerCreator = PluginHook.DatabaseProviders[connection.Type];
            string connectionString = connection.ConnectionString.Original;
            string creationConnectionString = connection.ConnectionString.Original;
            string databaseName;
            string cleanupTarget;
            bool cleanupFile;

            if (connection.Type == DatabaseType.SQLite)
            {
                var builder = new SqliteConnectionStringBuilder(connection.ConnectionString.Original);
                if (builder.Mode == SqliteOpenMode.Memory ||
                    builder.DataSource == ":memory:" ||
                    builder.DataSource.Equals("memory", StringComparison.OrdinalIgnoreCase))
                {
                    databaseName = $"char_predicate_{Guid.NewGuid():N}";
                    builder.DataSource = databaseName;
                    builder.Mode = SqliteOpenMode.Memory;
                    builder.Cache = SqliteCacheMode.Shared;
                    cleanupTarget = databaseName;
                    cleanupFile = false;
                }
                else
                {
                    var filePath = Path.Combine(Path.GetTempPath(), $"char_predicate_{Guid.NewGuid():N}.db");
                    builder.DataSource = filePath;
                    cleanupTarget = filePath;
                    cleanupFile = true;
                }

                connectionString = builder.ConnectionString;
                databaseName = builder.DataSource;
            }
            else
            {
                databaseName = $"datalinq_char_predicate_{Guid.NewGuid():N}";
                var builder = new MySqlConnectionStringBuilder(connection.ConnectionString.Original)
                {
                    Database = databaseName
                };
                connectionString = builder.ConnectionString;
                creationConnectionString = connection.ConnectionString.Original;
                cleanupTarget = databaseName;
                cleanupFile = false;
            }

            var database = providerCreator.GetDatabaseProvider<CharPredicateDb>(connectionString, databaseName);
            var createResult = PluginHook.CreateDatabaseFromMetadata(
                connection.Type,
                database.Provider.Metadata,
                databaseName,
                creationConnectionString,
                true);

            if (createResult.HasFailed)
            {
                database.Dispose();
                Assert.Fail(createResult.Failure.ToString());
            }

            return new TempCharPredicateDatabase(connection, database, cleanupTarget, cleanupFile);
        }

        public void Dispose()
        {
            Database.Dispose();

            if (cleanupFile)
            {
                try
                {
                    if (File.Exists(cleanupTarget))
                        File.Delete(cleanupTarget);
                }
                catch (IOException)
                {
                    // Best-effort cleanup for temp SQLite files; open handles can outlive provider disposal briefly.
                }
                return;
            }

            if (sourceConnection.Type is DatabaseType.MySQL or DatabaseType.MariaDB)
            {
                using var connection = new MySqlConnection(sourceConnection.ConnectionString.Original);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"DROP DATABASE IF EXISTS `{cleanupTarget}`";
                command.ExecuteNonQuery();
            }
        }
    }

    private static bool IsInMemorySQLiteConnection(DataLinqDatabaseConnection connection)
    {
        if (connection.Type != DatabaseType.SQLite)
            return false;

        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString.Original);
        return builder.Mode == SqliteOpenMode.Memory ||
            builder.DataSource == ":memory:" ||
            builder.DataSource.Equals("memory", StringComparison.OrdinalIgnoreCase);
    }

    private static Select<CharPredicateRow> BuildLinqSelect(Database<CharPredicateDb> database, IQueryable<CharPredicateRow> query)
    {
        var queryParser = QueryParser.CreateDefault();
        var queryModel = queryParser.GetParsedQuery(query.Expression);
        var table = database.Provider.Metadata.TableModels.Single(x => x.Model.CsType.Type == typeof(CharPredicateRow)).Table;
        var executor = new QueryExecutor(database.Provider.ReadOnlyAccess, table);
        var parseMethod = typeof(QueryExecutor)
            .GetMethod("ParseQueryModel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(CharPredicateRow));

        return (Select<CharPredicateRow>)parseMethod.Invoke(executor, [queryModel])!;
    }
}

[Database("charpredicate")]
public sealed partial class CharPredicateDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<CharPredicateRow> Rows { get; } = new(dataSource);
}

[Table("charpredicaterows")]
public abstract partial class CharPredicateRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<CharPredicateRow, CharPredicateDb>(rowData, dataSource), ITableModel<CharPredicateDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int? Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "char", 1)]
    [Type(DatabaseType.MariaDB, "char", 1)]
    [Column("status")]
    public abstract char Status { get; }
}
