using System.Collections.Concurrent;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.SQLite;
using SessionConsumer;

namespace W4HostConsumer;

public sealed partial class HostOperations(IDataLinqUnitOfWorkFactory<TestDb> factory, HostTrace trace) : IDataLinqService<TestDb>
{
    [DataLinqOperation]
    private async Task<int> WriteWaitCoreAsync(IDataLinqSession<TestDb> session, int id, Task release, CancellationToken cancellationToken)
    {
        await session.InsertAsync(new MutableTestRow { Id = id, Value = "pending host work" }, cancellationToken);
        trace.Written(id).TrySetResult(session);
        try { await release.WaitAsync(cancellationToken); }
        finally { trace.Events.Enqueue("callback-exit:" + id); }
        return id;
    }

    [DataLinqOperation]
    private Task<uint> ObserveCoreAsync(IDataLinqSession<TestDb> session,
        Func<IDataLinqSession<TestDb>, CancellationToken, Task<uint>> action, CancellationToken cancellationToken) => action(session, cancellationToken);
}

public sealed class HostTrace
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<IDataLinqSession<TestDb>>> written = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource> finished = new();
    public ConcurrentQueue<string> Events { get; } = new();
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource WorkerFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? Failure { get; set; }
    public int RootCreations { get; set; }
    public int RootDisposals { get; set; }
    public TaskCompletionSource<IDataLinqSession<TestDb>> Written(int id) => written.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
    public TaskCompletionSource Finished(int id) => finished.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

internal static class Fixture
{
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    internal static async Task CreateSchema(SQLiteDatabase<TestDb> database)
    {
        // Explicit fixture provisioning; DataLinq registration never creates a schema.
        var result = await PluginHook.CreateDatabaseFromMetadataAsync(DatabaseType.SQLite, database.Provider.Metadata,
            database.Provider.DatabaseName, database.Provider.ConnectionString, true);
        if (result.HasFailed) throw new InvalidOperationException(result.Failure.ToString());
    }
}
