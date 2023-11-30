using System;
using System.Linq;
using DataLinq.Utils;

namespace DataLinq.Cache
{
    public class DatabaseCacheSnapshot(DateTime timestamp, TableCacheSnapshot[] tableCaches)
    {
        public DateTime Timestamp { get; } = timestamp;
        public TableCacheSnapshot[] TableCaches { get; } = tableCaches;

        public long? NewestTick => TableCaches.Max(x => x.NewestTick);
        public long? OldestTick => TableCaches.Min(x => x.OldestTick);
        public int RowCount => TableCaches.Sum(x => x.RowCount);
        public long TotalBytes => TableCaches.Sum(x => x.TotalBytes);
        public string TotalBytesFormatted => TotalBytes.ToFileSize();
        public DateTime? NewestDateTime => NewestTick.HasValue ? new(NewestTick.Value) : null;
        public DateTime? OldestDateTime => OldestTick.HasValue ? new(OldestTick.Value) : null;
    }
}
