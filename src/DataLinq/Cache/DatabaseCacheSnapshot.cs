using System;
using System.Linq;
using DataLinq.Utils;

namespace DataLinq.Cache;

public class DatabaseCacheSnapshot(DateTime timestamp, TableCacheSnapshot[] tableCaches)
{
    public DateTime Timestamp { get; } = timestamp;
    public TableCacheSnapshot[] TableCaches { get; } = tableCaches;

    public long? NewestTick => TableCaches.Max(x => x.NewestTick);
    public long? OldestTick => TableCaches.Min(x => x.OldestTick);
    public int RowCount => TableCaches.Sum(x => x.RowCount);
    /// <summary>Estimated bytes for row values only, excluding cache container overhead.</summary>
    public long RowPayloadBytes => TableCaches.Sum(x => x.RowPayloadBytes);
    public string RowPayloadBytesFormatted => RowPayloadBytes.ToFileSize();
    /// <summary>Compatibility alias for <see cref="RowPayloadBytes"/>.</summary>
    public long TotalBytes => RowPayloadBytes;
    public string TotalBytesFormatted => RowPayloadBytesFormatted;
    public DateTime? NewestDateTime => NewestTick.HasValue ? new(NewestTick.Value) : null;
    public DateTime? OldestDateTime => OldestTick.HasValue ? new(OldestTick.Value) : null;
}
