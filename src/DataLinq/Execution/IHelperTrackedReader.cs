using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>Private reader control. StopAdmission is nonthrowing, nonblocking and does no I/O.</summary>
internal interface IHelperTrackedReader
{
    void StopAdmission();
    // Wait for an existing call, close resources and release the lease even on failure.
    ValueTask DrainAsync();
}
