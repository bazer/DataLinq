using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.SQLite;
using DataLinq.Validation;

namespace DataLinq.Testing;

/// <summary>Owns an isolated SQLite in-memory store provisioned from a generated database model.</summary>
/// <remarks>Dispose application hosts, transactions and readers before disposing the fixture.</remarks>
public sealed class SqliteTestDatabase<TDatabase> : IDisposable, IAsyncDisposable
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    // SQLiteProvider gives each anonymous memory store a unique shared name and owns the keeper.
    private const string ConnectionString = "Data Source=:memory:;Foreign Keys=True;Pooling=False";

    private SqliteTestDatabase()
    {
        Database = new SQLiteDatabase<TDatabase>(ConnectionString);
        Factory = new(Database);
    }

    /// <summary>The database that owns the fixture's keeper connection.</summary>
    public SQLiteDatabase<TDatabase> Database { get; }
    internal DataLinqUnitOfWorkFactory<TDatabase> Factory { get; }

    /// <summary>The application's unit-of-work factory, bound to this fixture.</summary>
    public IDataLinqUnitOfWorkFactory<TDatabase> UnitOfWorkFactory => Factory;

    /// <summary>Creates an empty database with the model's SQLite schema.</summary>
    public static SqliteTestDatabase<TDatabase> Create()
    {
        var fixture = new SqliteTestDatabase<TDatabase>();
        try
        {
            var provider = fixture.Database.Provider;
            var ddl = new SqlFromSQLiteFactory();
            var schema = BuildSchema(ddl, provider.Metadata);
            var result = ddl.CreateDatabase(schema, provider.DatabaseName, provider.ConnectionString, foreignKeyRestrict: true);
            if (result.HasFailed) throw new InvalidOperationException($"SQLite fixture schema creation failed: {result.Failure}");
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <summary>Provisions the schema with the provider's asynchronous execution path.</summary>
    public static async Task<SqliteTestDatabase<TDatabase>> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fixture = new SqliteTestDatabase<TDatabase>();
        try
        {
            var provider = fixture.Database.Provider;
            var ddl = new SqlFromSQLiteFactory();
            var schema = BuildSchema(ddl, provider.Metadata);
            var result = await ddl.CreateDatabaseAsync(schema, provider.DatabaseName,
                provider.ConnectionString, foreignKeyRestrict: true, cancellationToken).ConfigureAwait(false);
            if (result.HasFailed) throw new InvalidOperationException($"SQLite fixture schema creation failed: {result.Failure}");
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static Sql BuildSchema(SqlFromSQLiteFactory ddl, DatabaseDefinition metadata)
    {
        SchemaIdentifierRules.RequireUnambiguous(metadata.TableModels.Select(model => model.Table.DbName),
            SchemaIdentifierRules.SQLite.TableNames, "table/view", model: true);
        // SQLite index names are database-wide. CREATE INDEX IF NOT EXISTS would silently skip a duplicate.
        var indexNames = new HashSet<string>(SchemaIdentifierRules.SQLite.IndexNames);
        foreach (var table in metadata.TableModels.Select(model => model.Table))
        {
            var checks = table.Model.Attributes.OfType<CheckAttribute>();
            var check = checks.FirstOrDefault(item => item.DatabaseType == DatabaseType.SQLite)
                ?? checks.FirstOrDefault(item => item.DatabaseType == DatabaseType.Default);
            if (check is not null)
                throw new NotSupportedException($"SQLite provisioning cannot create check constraint '{check.Name}' on '{table.DbName}'.");

            foreach (var index in table.ColumnIndices.Where(index => index.Characteristic != IndexCharacteristic.VirtualDataLinq))
            {
                if (table.Type == TableType.View)
                    throw new NotSupportedException($"SQLite provisioning cannot create index '{index.Name}' on view '{table.DbName}'.");
                if (index.Type != IndexType.BTREE || index.Characteristic is IndexCharacteristic.EXCLUSION or IndexCharacteristic.FILTERED)
                    throw new NotSupportedException($"SQLite provisioning cannot create {index.Type} {index.Characteristic} index '{index.Name}' on '{table.DbName}'.");
                if (index.Characteristic is IndexCharacteristic.Simple or IndexCharacteristic.Unique && !indexNames.Add(index.Name))
                    throw new NotSupportedException($"SQLite index '{index.Name}' on '{table.DbName}' needs a unique database-wide name.");
            }
        }

        var generated = ddl.GetCreateTables(metadata, foreignKeyRestrict: true);
        if (generated.HasFailed) throw new InvalidOperationException($"SQLite fixture schema generation failed: {generated.Failure}");
        var script = generated.Value;
        // SQLite defers binding a view until first use. Prepare its declared columns as part
        // of provisioning so missing sources, functions or columns fail before the fixture escapes.
        foreach (var view in metadata.TableModels.Select(model => model.Table).Where(table => table.Type == TableType.View))
        {
            var columns = view.Columns.Select(column => "v." + SqlIdentifier.Quote(column.DbName, "\""));
            script.AddText($"\nSELECT {string.Join(", ", columns)} FROM {SqlIdentifier.Quote(view.DbName, "\"")} AS v LIMIT 0;");
        }
        return script;
    }

    /// <summary>Returns the committed read root.</summary>
    public TDatabase Query() => Database.Query();

    /// <summary>Commits a synchronous seed callback as one transaction.</summary>
    public void Seed(Action<IDataLinqSession<TDatabase>> action)
    {
        EnsureSynchronous(action, typeof(void));
        Factory.Execute(action);
    }

    /// <summary>Returns the seed result after commit and cleanup.</summary>
    public TResult Seed<TResult>(Func<IDataLinqSession<TDatabase>, TResult> action)
    {
        EnsureSynchronous(action, typeof(TResult));
        return Factory.Execute(action);
    }

    private static void EnsureSynchronous(Delegate action, Type resultType)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsAsyncResult(resultType) || action.GetInvocationList().Any(callback =>
            IsAsyncResult(callback.Method.ReturnType) || callback.Method.IsDefined(typeof(AsyncStateMachineAttribute), false)))
            throw new ArgumentException("Use SeedAsync for asynchronous callbacks.", nameof(action));
    }

    private static bool IsAsyncResult(Type type) => typeof(Task).IsAssignableFrom(type) || type == typeof(ValueTask)
        || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);

    private static void RequireSingleCallback(Delegate action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.GetInvocationList().Length != 1)
            throw new ArgumentException("SeedAsync requires one callback. Await multiple steps inside that callback.", nameof(action));
    }

    /// <summary>Awaits the callback, commits its transaction and completes cleanup.</summary>
    public Task SeedAsync(Func<IDataLinqSession<TDatabase>, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        RequireSingleCallback(action);
        return Factory.ExecuteAsync(action, cancellationToken: cancellationToken);
    }

    /// <summary>Returns the asynchronous seed result after commit and cleanup.</summary>
    public Task<TResult> SeedAsync<TResult>(Func<IDataLinqSession<TDatabase>, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        RequireSingleCallback(action);
        return Factory.ExecuteAsync(action, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => Database.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Database.DisposeAsync();
}
