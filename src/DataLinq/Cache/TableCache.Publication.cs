using DataLinq.Instances;

namespace DataLinq.Cache;

public partial class TableCache
{
    // Only publication and invalidation run under this gate. Never hold it across
    // source I/O, model construction, callbacks, or another table's invalidation.
    private readonly object publicationGate = new();
    private RowReadGeneration readGeneration = new();

    internal RowReadGeneration CaptureReadGeneration()
    {
        lock (publicationGate)
            return readGeneration;
    }

    // Caller holds publicationGate, together with the destructive cache operation.
    private void AdvanceReadGeneration() => readGeneration = new RowReadGeneration();
}
