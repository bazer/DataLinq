using DataLinq;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.SQLite;
using Microsoft.Extensions.DependencyInjection;

namespace SessionConsumer;

internal static class ServiceConsumer
{
    internal static async Task Run()
    {
        await using var first = await CreateDatabase();
        await using var second = await CreateDatabase();
        var firstFactory = new DataLinqUnitOfWorkFactory<TestDb>(first);
        var secondFactory = new DataLinqUnitOfWorkFactory<TestDb>(second);
        var service = new BindingService(secondFactory, new ServicePolicy(10));
        if (service.Echo() != 17 || await service.EchoAsync(1) != 11 || service.Sum(1, 2, 3) != 6 || service.Payload(4).Value != 4)
            throw new Exception("Generated ordinary return families changed their payloads.");
        var touched = 0;
        service.Touch(() => touched++);
        using var cancellation = new CancellationTokenSource();
        await service.TouchAsync(token =>
        {
            if (token != cancellation.Token) throw new Exception("Generated cancellation token was not forwarded.");
            touched++;
            return Task.CompletedTask;
        }, cancellation.Token);
        if (touched != 2) throw new Exception("Generated no-value operations did not run once.");
        cancellation.Cancel();
        await Expect<OperationCanceledException>(() => service.TouchAsync(_ => { touched++; return Task.CompletedTask; }, cancellation.Token));
        if (touched != 2) throw new Exception("Pre-cancelled operation entered business code.");

        var prepared = second.PrepareQuery(0, minimum => second.Query().Rows.Count(row => row.Id > minimum));
        await using (var owner = firstFactory.Create())
        {
            service.Insert(owner, 1, "participating");
            await service.InsertAsync(owner, 2, "participating async");
            if (service.Prepared(owner, prepared, 0) != 2 || second.Query().Rows.Count() != 0)
                throw new Exception("Generated participant used its standalone factory or lost prepared transaction identity.");
            await owner.CommitAsync();
        }
        if (first.Query().Rows.Count() != 2 || second.Query().Rows.Count() != 0) throw new Exception("Participant completed another database.");
        service.Insert(3, "owning");
        await service.InsertAsync(4, "owning async");
        if (second.Query().Rows.Count() != 2) throw new Exception("Generated owning methods did not commit to their selected factory.");

        if (service.General(false).Reason != "mapped" || service.Native(true).Reason != "mapped" ||
            (await service.NativeAsync(true)).Reason != 42 || !service.Nullable().TryUnwrap(out var nullable, out _) || nullable is not null || !service.Empty().IsSuccess)
            throw new Exception("Optional generated result behavior changed.");
        Expect<DataLinqRollbackRequestedException>(() => service.General(true));
        Expect<InvalidOperationException>(() => service.Invalid());
        await using (var owner = firstFactory.Create())
        {
            service.Insert(owner, 5, "must roll back");
            var child = service.Native(owner, true);
            if (child.Reason != "mapped" || !owner.TryGetRollbackReason<string>(out var reason) || reason != "original")
                throw new Exception("Participating result mapping lost the first veto reason.");
            owner.RequestRollback("parent mapping");
            Expect<DataLinqRollbackRequestedException>(() => service.Touch(owner, () => touched++));
            await Expect<DataLinqRollbackRequestedException>(() => owner.CommitAsync());
        }
        if (first.Query().Rows.Any(row => row.Id == 5)) throw new Exception("Nested veto did not roll back the outer owner.");
        await using (var owner = firstFactory.Create())
        {
            var invalid = service.Invalid(owner);
            Expect<InvalidOperationException>(() => _ = invalid.IsSuccess);
            if (owner.IsRollbackRequested) throw new Exception("Local invalid-result inspection changed transaction state.");
            owner.Commit();
            Expect<InvalidOperationException>(() => service.Touch(owner, () => touched++));
            await Expect<InvalidOperationException>(() => service.TouchAsync(owner, _ => Task.CompletedTask));
        }

        var services = new ServiceCollection();
        services.AddSingleton<IServicePolicy>(new ServicePolicy(20));
        services.AddKeyedSingleton("explicit", new ServiceDependency("keyed"));
        await using var dependencies = services.BuildServiceProvider();
        var traditional = Construct<TraditionalService>(dependencies, firstFactory);
        if (traditional.Echo(1) != 21 || traditional.Label != "fallback" || traditional.Dependency?.Label != "keyed")
            throw new Exception("Generated constructor bridge lost ordinary/default/keyed dependencies.");
        if (new TraditionalService(new ServicePolicy(30), firstFactory).Echo(1) != 31)
            throw new Exception("Direct construction of a traditional service required DI initialization.");

        var admitted = new List<IDataLinqSession<TestDb>>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Enumerable.Range(0, 16).Select(index => service.ObserveAsync(async session =>
        {
            admitted.Add(session);
            await release.Task;
            session.ValidateParticipation();
            return index;
        })).ToArray();
        if (admitted.Count != 16 || admitted.Distinct(ReferenceEqualityComparer.Instance).Count() != 16)
            throw new Exception("Concurrent singleton-service calls retained or shared a current transaction.");
        release.SetResult();
        if ((await Task.WhenAll(pending)).Sum() != 120) throw new Exception("Concurrent owned payloads changed.");
        foreach (var session in admitted) Expect<ObjectDisposedException>(session.ValidateParticipation);
    }

    private static TService Construct<TService>(IServiceProvider services, IDataLinqUnitOfWorkFactory<TestDb> factory)
        where TService : class, IDataLinqService<TestDb>, IDataLinqServiceFactory<TestDb, TService> => TService.Create(services, factory);

    private static async Task<SQLiteDatabase<TestDb>> CreateDatabase()
    {
        var name = "w4_services_" + Guid.NewGuid().ToString("N");
        var database = new SQLiteDatabase<TestDb>($"Data Source={name};Mode=Memory;Cache=Shared", name);
        var created = await PluginHook.CreateDatabaseFromMetadataAsync(DatabaseType.SQLite, database.Provider.Metadata, name, database.Provider.ConnectionString, true);
        if (created.HasFailed) { await database.DisposeAsync(); throw new Exception(created.Failure.ToString()); }
        return database;
    }
    private static void Expect<TException>(Action action) where TException : Exception
    {
        try { action(); } catch (TException) { return; }
        throw new Exception("Expected " + typeof(TException).Name);
    }
    private static async Task Expect<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); } catch (TException) { return; }
        throw new Exception("Expected " + typeof(TException).Name);
    }
}
