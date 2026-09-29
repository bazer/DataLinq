using System.Reflection;
using System.Runtime.Loader;
using DataLinq;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.PackageConsumer;
using DataLinq.SQLite;

internal static class PackedChecks
{
    internal static async Task RunAsync(string[] args)
    {
        if (args.Length is < 1 or > 2) throw new ArgumentException("Supply the matching-TFM 0.9.2 consumer DLL and optional manifest output path.");
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
        var exports = assembly.GetType("Legacy092Consumer.LegacyExports", throwOnError: true)!;
        T Old<T>(string name) => (T)exports.GetMethod(name)!.Invoke(null, null)!;
        if (Old<int>("SynchronousGeneratedConsumer") != 17) throw new Exception("Old synchronous generated consumer failed.");
        _ = Old<IImmutable<IModel>>("StaticInterfaceImplementer");
        var oldReference = Old<IImmutableForeignKey<IImmutableInstance>>("CovariantReference");
        if (oldReference is IAsyncImmutableForeignKey<IImmutableInstance>) throw new Exception("Old covariance incorrectly supplied async capability.");
        try
        {
            exports.GetMethod("RemovedKeyedEnumeration")!.Invoke(null, null);
            throw new Exception("The approved removed keyed API unexpectedly remained callable.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is MissingMethodException) { }
        var staticLookup = typeof(IImmutable<IModel>).GetMethod("GetByProviderKeyAsync")!;
        if (!staticLookup.IsStatic || staticLookup.IsAbstract || staticLookup.IsVirtual || staticLookup.GetMethodBody() is null)
            throw new Exception("The new static lookup unexpectedly requires an implementer slot.");
        var provider = Old<IDatabaseProvider>("Provider");
        if (Old<string>("ConstructorBinding") != typeof(DataLinq.Logging.DataLinqLoggingConfiguration).FullName ||
            ExternalConstructorBindings.Constructors.UntypedNullBinding() != typeof(DataLinq.Logging.DataLinqLoggingConfiguration).FullName)
            throw new Exception("SQLite's existing untyped-null constructor binding changed.");
        await using (var derived = Old<DatabaseProvider>("DerivedProvider"))
        {
            if (derived.ExecutionOptions.RecoveryRollbackTimeout != TimeSpan.FromSeconds(30)) throw new Exception("Old derived constructor settings changed.");
        }
        if (provider.ExecutionOptions.RecoveryRollbackTimeout != TimeSpan.FromSeconds(30)) throw new Exception("Old default options.");
        await Reject(() => ((IAsyncDisposable)provider).DisposeAsync().AsTask());
        await Reject(() => provider.CommitAsync(_ => Task.CompletedTask));
        await Reject(() => provider.DatabaseExistsAsync());
        await Reject(() => provider.FileOrServerExistsAsync());
        await Reject(() => provider.TableExistsAsync("unused"));
        var access = Old<IDatabaseAccess>("Access");
        await Reject(() => access.ExecuteNonQueryAsync("unused"));
        await Reject(() => access.ExecuteScalarAsync<int>("unused"));
        await Reject(() => access.ExecuteReaderAsync("unused"));
        await Reject(async () => { await foreach (var _ in access.ReadReaderAsync("unused")) { } });
        var transaction = Old<DatabaseTransaction>("Transaction");
        await Reject(() => transaction.CommitAsync());
        await Reject(() => transaction.RollbackAsync());
        await Reject(() => transaction.DisposeAsync().AsTask());
        var source = Old<DataSourceAccess>("Source");
        await Reject(async () => { await foreach (var _ in source.GetFromQueryAsync<IModel>("unused")) { } });
        await Reject(() => Old<IMetadataFromSqlFactory>("Metadata").ParseDatabaseAsync("Db", "Db", "Ns", "db", "invalid"));
        await Reject(() => Old<ISqlFromMetadataFactory>("Provisioning").CreateDatabaseAsync(new("sql"), "db", "invalid", false));
        await GeneratedAsync();
        await GeneratedExecution.RunAsync();
        if (args.Length == 2) ContractManifest.Write(args[1]);
        Console.WriteLine("Packed async and unchanged 0.9.2 binary consumers passed on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
    }

    private static async Task GeneratedAsync()
    {
        SQLiteProvider.RegisterProvider();
        var name = "packed_async_" + Guid.NewGuid().ToString("N");
        await using var database = new SQLiteDatabase<PackageConsumerDatabase>($"Data Source={name};Mode=Memory;Cache=Shared", name);
        var created = await PluginHook.CreateDatabaseFromMetadataAsync(DatabaseType.SQLite,
            database.Provider.Metadata, name, database.Provider.ConnectionString, true);
        if (created.HasFailed) throw new Exception(created.Failure.ToString());
        var row = await new MutablePackageConsumerRow { Id = 27, GroupId = 7, Name = "packed", ExternalGuid = Guid.NewGuid() }
            .InsertAsync(changes: mutable => mutable.Name = "edited", database: database);
        if ((await PackageConsumerRow.GetAsync(27, database))?.Name != "edited") throw new Exception("Generated key/mutation failed.");
        if (await database.Query().Rows.Select(value => value.Id).SingleAsync() != 27) throw new Exception("Scalar query failed.");
        if (await database.Query().Rows.SumAsync(value => value.Id) != 27) throw new Exception("Numeric query failed.");
        if (await database.CommitAsync(async (transaction, token) =>
            (await transaction.Query().Rows.SingleAsync(cancellationToken: token)).Id) != 27) throw new Exception("Typed callback failed.");
        await using var reader = await database.Provider.DatabaseAccess.ExecuteReaderAsync("SELECT 1");
        if (!await reader.ReadNextRowAsync()) throw new Exception("Public raw reader failed.");
    }

    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (NotSupportedException) { return; }
        throw new Exception("Old implementation accepted an unsupported async path.");
    }
}
