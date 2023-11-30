using System.Collections.Concurrent;
using System.Threading;

namespace DataLinq.Workers;

public interface IWorkQueue<T>
{
    int Count { get; }

    void Add(T varde);

    T Take(CancellationToken ct);

    T[] Values();
}

public class WorkQueue<T> : IWorkQueue<T>
{
    public int Count => Queue.Count;
    private BlockingCollection<T> Queue { get; }

    public WorkQueue(IProducerConsumerCollection<T> collection)
    {
        Queue = new BlockingCollection<T>(collection);
    }

    public static WorkQueue<T> NewStandardQueue() => new WorkQueue<T>(new ConcurrentQueue<T>());

    public void Add(T varde) => Queue.Add(varde);

    public T Take(CancellationToken ct) => Queue.Take(ct);

    public T[] Values() => Queue.ToArray();
}
