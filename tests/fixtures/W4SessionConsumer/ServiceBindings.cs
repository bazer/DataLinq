#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Mutation;
using Microsoft.Extensions.DependencyInjection;

namespace SessionConsumer;

public interface IServicePolicy { int Offset { get; } }
public sealed record ServicePolicy(int Offset) : IServicePolicy;
public sealed record ServiceDependency(string Label);
public readonly record struct ServicePayload(int Value, string? Detail);

public sealed partial class BindingService(IDataLinqUnitOfWorkFactory<TestDb> transactions, IServicePolicy policy) : IDataLinqService<TestDb>
{
    [DataLinqOperation] private int EchoCore(IDataLinqSession<TestDb> session, int value = 7) => value + policy.Offset;
    [DataLinqOperation] private void TouchCore(IDataLinqSession<TestDb> session, Action action) => action();
    [DataLinqOperation] private Task<int> EchoCoreAsync(IDataLinqSession<TestDb> session, int value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value + policy.Offset);
    }
    [DataLinqOperation] private Task TouchCoreAsync(IDataLinqSession<TestDb> session, Func<CancellationToken, Task> action, CancellationToken cancellationToken) => action(cancellationToken);
    [DataLinqOperation] private Task<int> ObserveCoreAsync(IDataLinqSession<TestDb> session, Func<IDataLinqSession<TestDb>, Task<int>> action, CancellationToken cancellationToken) => action(session);
    [DataLinqOperation] private ServicePayload PayloadCore(IDataLinqSession<TestDb> session, int value) => new(value, null);
    [DataLinqOperation] private int SumCore(IDataLinqSession<TestDb> session, params int[] values) => values.Sum();
    [DataLinqOperation] private TestRow InsertCore(IDataLinqSession<TestDb> session, int id, string value) => session.Insert(new MutableTestRow { Id = id, Value = value });
    [DataLinqOperation] private Task<TestRow> InsertCoreAsync(IDataLinqSession<TestDb> session, int id, string value, CancellationToken cancellationToken) => session.InsertAsync(new MutableTestRow { Id = id, Value = value }, cancellationToken);
    [DataLinqOperation] private int PreparedCore(IDataLinqSession<TestDb> session, PreparedQuery<TestDb, int, int> prepared, int value) => session.Execute(prepared, value);
    [DataLinqOperation] private TransactionResult<int> GeneralCore(IDataLinqSession<TestDb> session, bool veto)
    {
        if (veto) session.RequestRollback("original");
        return new Rollback<string>("mapped");
    }
    [DataLinqOperation(ResultAware = true)] private TransactionResult<int> NativeCore(IDataLinqSession<TestDb> session, bool veto) => GeneralCore(session, veto);
    [DataLinqOperation(ResultAware = true)] private Task<TransactionResult<int, int>> NativeCoreAsync(IDataLinqSession<TestDb> session, bool veto, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (veto) session.RequestRollback(17);
        return Task.FromResult(TransactionResult<int, int>.FromRollback(new(42)));
    }
    [DataLinqOperation(ResultAware = true)] private TransactionResult<string?> NullableCore(IDataLinqSession<TestDb> session) => TransactionResult<string?>.FromSuccess(null);
    [DataLinqOperation(ResultAware = true)] private TransactionResult<DataLinq.Mutation.Unit> EmptyCore(IDataLinqSession<TestDb> session) => TransactionResult.Success();
    [DataLinqOperation(ResultAware = true)] private TransactionResult<int> InvalidCore(IDataLinqSession<TestDb> session) => default;
}

public sealed partial class TraditionalService : IDataLinqService<TestDb>
{
    [DataLinqFactory] private IDataLinqUnitOfWorkFactory<TestDb> Transactions { get; }
    private readonly IServicePolicy policy;
    public string Label { get; }
    public ServiceDependency? Dependency { get; }

    public TraditionalService(IServicePolicy policy, IDataLinqUnitOfWorkFactory<TestDb> factory,
        string label = "fallback", [FromKeyedServices("explicit")] ServiceDependency? dependency = null)
    {
        this.policy = policy;
        Transactions = factory;
        Label = label;
        Dependency = dependency;
    }
    [DataLinqOperation] private int EchoCore(IDataLinqSession<TestDb> session, int value) => value + policy.Offset;
}

public static class ServiceBindings
{
    public static void Compile(BindingService service, TraditionalService traditional, IDataLinqSession<TestDb> session, CancellationToken token)
    {
        int normal = service.Echo(value: 1);
        int participating = service.Echo(session: session, value: 2);
        Action<Action> noValue = service.Touch;
        Action<IDataLinqSession<TestDb>, Action> participatingNoValue = service.Touch;
        Task<int> asynchronous = service.EchoAsync(1);
        Task<int> participatingAsync = service.EchoAsync(session, value: 2, cancellationToken: token);
        Task noValueAsync = service.TouchAsync(_ => Task.CompletedTask, token);
        Task participatingNoValueAsync = service.TouchAsync(session, _ => Task.CompletedTask);
        TransactionResult<int> general = service.General(true);
        TransactionResult<int> selected = service.Native(session, true);
        Task<TransactionResult<int, int>> selectedAsync = service.NativeAsync(false, token);
        TransactionResult<string?> nullable = service.Nullable();
        ServicePayload custom = service.Payload(1);
        int sum = service.Sum(1, 2, 3);
        int old = traditional.Echo(session, 1);
    }
}
