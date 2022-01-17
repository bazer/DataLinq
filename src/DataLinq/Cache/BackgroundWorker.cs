using DataLinq.Metadata;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DataLinq.Cache
{
    public enum WorkerStatus
    {
        Off,
        Waiting,
        Running
    }

    public class BackgroundWorker
    {
        public int IntervalSeconds { get; set; }
        public bool IsActivated { get; set; }
        public WorkerStatus Status { get; set; }
    }

    public class CleanCacheWorker : BackgroundWorker
    {

    }
}
