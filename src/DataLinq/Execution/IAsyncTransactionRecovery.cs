using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// Internal, owned resource bundle after all earlier provider work has settled. Successful
/// rollback return confirms database rollback; a throw provides no such confirmation.
/// Disposal steps must be safe to attempt independently and must await their actual work.
/// They must not retry explicit rollback. Native adapters have not implemented this contract.
/// </summary>
internal interface IAsyncTransactionRecovery
{
    Task RollbackAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken);
    ValueTask DisposeTransactionAsync(TransactionOperationGate.Step owner);
    ValueTask DisposeConnectionAsync(TransactionOperationGate.Step owner);
}
