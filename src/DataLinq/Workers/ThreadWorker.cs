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
    private readonly object lifecycleSync = new();
    public WorkerStatus Status { get; private set; }
    protected IThreadCreator ThreadCreator { get; }
    protected IWorkQueue<T> WorkQueue { get; } = WorkQueue<T>.NewStandardQueue();
    private CancellationTokenSource? CancellationTokenSource { get; set; }
    private CancellationTokenSource? VantaCancellationTokenSource { get; set; }
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
        CancellationToken token;
        lock (lifecycleSync)
        {
            if (Status != WorkerStatus.Stopped)
                return;

            CancellationTokenSource = new CancellationTokenSource();
            ResetWaitCancellationTokenSource(CancellationTokenSource.Token);
            SetStatus(WorkerStatus.WaitingForJob);
            token = CancellationTokenSource.Token;
        }

        ThreadCreator.CreateNewThread(_ => WorkLoop(WorkQueue, token));
    }

    public void Stop()
    {
        CancellationTokenSource? cancellationTokenSource;
        CancellationTokenSource? vantaCancellationTokenSource;

        lock (lifecycleSync)
        {
            if (Status == WorkerStatus.Stopped || Status == WorkerStatus.Stopping)
                return;

            cancellationTokenSource = CancellationTokenSource;
            vantaCancellationTokenSource = VantaCancellationTokenSource;
        }

        SetStatus(WorkerStatus.Stopping);
        vantaCancellationTokenSource?.Cancel();
        cancellationTokenSource?.Cancel();
    }

    public void Run()
    {
        if (Status != WorkerStatus.WaitingUntilTime)
            return;

        VantaCancellationTokenSource?.Cancel();
    }

    protected void Wait(TimeSpan tid)
    {
        var cancellationTokenSource = CancellationTokenSource;
        var vantaCancellationTokenSource = VantaCancellationTokenSource;
        if (cancellationTokenSource?.IsCancellationRequested == true || vantaCancellationTokenSource?.IsCancellationRequested == true)
            return;

        if (tid == TimeSpan.MinValue)
            return;

        WaitingUntil = DateTime.Now.Add(tid);
        SetStatus(WorkerStatus.WaitingUntilTime);

        vantaCancellationTokenSource?.Token.WaitHandle.WaitOne(tid);
        WaitingUntil = null;
    }

    protected void SetStatus(WorkerStatus status)
    {
        Status = status;
    }

    protected void WorkLoop(IWorkQueue<T> queue, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ResetWaitCancellationTokenSource(ct);
                SetStatus(WorkerStatus.WaitingForJob);
                var varde = queue.Take(ct);
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
        finally
        {
            CancellationTokenSource? cancellationTokenSource;
            CancellationTokenSource? vantaCancellationTokenSource;

            lock (lifecycleSync)
            {
                cancellationTokenSource = CancellationTokenSource;
                vantaCancellationTokenSource = VantaCancellationTokenSource;
                CancellationTokenSource = null;
                VantaCancellationTokenSource = null;
            }

            vantaCancellationTokenSource?.Dispose();
            cancellationTokenSource?.Dispose();
            WaitingUntil = null;
            SetStatus(WorkerStatus.Stopped);
        }
    }

    protected abstract void DoWork(T value);

    public void Dispose()
    {
        Stop();
    }

    private void ResetWaitCancellationTokenSource(CancellationToken workerToken)
    {
        CancellationTokenSource? previous;

        lock (lifecycleSync)
        {
            previous = VantaCancellationTokenSource;
            VantaCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(workerToken);
        }

        previous?.Dispose();
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
