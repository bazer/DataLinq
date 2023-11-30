using System;
using DataLinq.Utils;

namespace DataLinq.Cache
{
    public class TableCacheSnapshot(string tableName, int rowCount, long totalBytes, long? newestTick, long? oldestTick)
    {
        public string TableName { get; } = tableName;
        public long? NewestTick { get; } = newestTick > 0 ? newestTick : null;
        public long? OldestTick { get; } = oldestTick > 0 ? oldestTick : null;
        public int RowCount { get; } = rowCount;
        public long TotalBytes { get; } = totalBytes;
        public string TotalBytesFormatted => TotalBytes.ToFileSize();
        public DateTime? NewestDateTime => NewestTick.HasValue ? new(NewestTick.Value) : null;
        public DateTime? OldestDateTime => OldestTick.HasValue ? new(OldestTick.Value) : null;
    }
}
