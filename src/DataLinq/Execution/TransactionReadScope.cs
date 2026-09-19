using System;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>
/// Owns one managed read, including its cache work and resource cleanup. Private
/// nested dispatch receives Step explicitly; models and callbacks never receive it.
/// </summary>
internal sealed class TransactionReadScope : IDisposable
{
    private readonly TransactionOperationGate.Lease lease;
    internal TransactionOperationGate.Step Step { get; }

    internal TransactionReadScope(Transaction transaction, string operation, ExecutionOperationKind operationKind)
    {
        lease = transaction.ExecutionGate.Enter(operation, operationKind: operationKind);
        try
        {
            Step = transaction.ExecutionGate.EnterStep(lease);
            try
            {
                // Completion may have won admission after the caller's initial check.
                transaction.EnsureCanRead(operation, Step);
            }
            catch
            {
                Step.Dispose();
                throw;
            }
        }
        catch (Exception failure)
        {
            lease.ReportFailure(failure);
            lease.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Step.Dispose();
        lease.Dispose();
    }

    internal void RegisterReader(IHelperTrackedReader reader) => lease.RegisterReader(reader);
    internal void ReportFailure(Exception failure) => lease.ReportFailure(failure);
}
