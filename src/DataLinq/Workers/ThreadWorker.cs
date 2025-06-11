using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;

namespace DataLinq.Workers;

public enum WorkerStatus
{
    Stopped,
    Stopping,
    Running,
    WaitingForJob,
    WaitingUntilTime
}

public interface IThreadCreator
{
    void CreateNewThread(Action<CancellationToken> arbete);
}

//public static class ThreadCreator
//{
//    public static IThreadCreator Tradskapare { get; set; } = new LongRunningTaskCreator();

//    public static void CreateNewThread(Action<CancellationToken> arbete) =>
//        Tradskapare.CreateNewThread(arbete);
//}

public class LongRunningTaskCreator : IThreadCreator
{
    public void CreateNewThread(Action<CancellationToken> arbete) =>
        Task.Factory.StartNew(() => arbete(new CancellationToken()), TaskCreationOptions.LongRunning);
}

public abstract class ThreadWorker<T> : IDisposable
{
    public WorkerStatus Status { get; private set; }
    protected IThreadCreator ThreadCreator { get; }
    protected IWorkQueue<T> WorkQueue { get; } = WorkQueue<T>.NewStandardQueue();
    private CancellationToken CancellationToken => CancellationTokenSource.Token;
    private CancellationTokenSource CancellationTokenSource { get; set; }
    private CancellationToken VantaCancellationToken => VantaCancellationTokenSource.Token;
    private CancellationTokenSource VantaCancellationTokenSource { get; set; }
    public DateTime? WaitingUntil { get; private set; }

    public ThreadWorker(IThreadCreator threadCreator)
    {
        this.ThreadCreator = threadCreator;
    }

    public void AddWork(T work)
    {
        WorkQueue.Add(work);
    }

    public void Start()
    {
        if (Status != WorkerStatus.Stopped)
            return;

        SetStatus(WorkerStatus.WaitingForJob);

        ThreadCreator.CreateNewThread(ct => WorkLoop(WorkQueue, ct));
    }

    public void Stop()
    {
        SetStatus(WorkerStatus.Stopping);
        CancellationTokenSource.Cancel();
    }

    public void Run()
    {
        if (Status != WorkerStatus.WaitingUntilTime)
            return;

        VantaCancellationTokenSource?.Cancel();
    }

    protected void Wait(TimeSpan tid)
    {
        if (CancellationToken.IsCancellationRequested || VantaCancellationToken.IsCancellationRequested)
            return;

        if (tid == TimeSpan.MinValue)
            return;

        WaitingUntil = DateTime.Now.Add(tid);
        SetStatus(WorkerStatus.WaitingUntilTime);

        VantaCancellationToken.WaitHandle.WaitOne(tid);
    }

    protected void SetStatus(WorkerStatus status)
    {
        Status = status;
    }

    protected void WorkLoop(IWorkQueue<T> queue, CancellationToken ct)
    {
        CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                VantaCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, new CancellationToken());
                SetStatus(WorkerStatus.WaitingForJob);
                var varde = queue.Take(CancellationToken);
                SetStatus(WorkerStatus.Running);
                DoWork(varde);
            }
        }
        catch (OperationCanceledException)
        {
            //TODO: Logging
        }
        catch (Exception)
        {
            //TODO: Logging
        }

        SetStatus(WorkerStatus.Stopped);
    }

    protected abstract void DoWork(T value);

    public void Dispose()
    {
        Stop();
    }
}

public class CleanCacheWorker : ThreadWorker<int>
{
    public TimeSpan WaitTime { get; private set; }
    protected IDatabaseProvider DatabaseProvider { get; }

    public CleanCacheWorker(IDatabaseProvider database, IThreadCreator threadCreator, TimeSpan waitTime) : base(threadCreator)
    {
        this.DatabaseProvider = database;
        this.WaitTime = waitTime;

        AddWork(0);
    }

    protected override void DoWork(int value)
    {
        DatabaseProvider.State?.Cache.CleanRelationNotifications();
        var rows = DatabaseProvider.State?.Cache.RemoveRowsBySettings().ToList();
        //TODO: Logging

        if (WorkQueue.Count == 0)
        {
            Wait(WaitTime);
            AddWork(++value);
        }
    }
}
