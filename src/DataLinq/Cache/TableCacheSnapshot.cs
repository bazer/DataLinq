using System;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Utils;

namespace DataLinq.Cache;

public class TableCacheSnapshot(string tableName, int rowCount, long totalBytes, long? newestTick, long? oldestTick, (string index, int count)[] indices)
{
    public string TableName { get; } = tableName;
    public long? NewestTick { get; } = newestTick > 0 ? newestTick : null;
    public long? OldestTick { get; } = oldestTick > 0 ? oldestTick : null;
    public (string index, int count)[] Indices { get; }  = indices;
    public string IndicesFormatted => Indices.Select(x => $"{x.index} ({x.count})").ToJoinedString(", ");
    public int RowCount { get; } = rowCount;
    /// <summary>Estimated bytes for row values only, excluding cache container overhead.</summary>
    public long RowPayloadBytes { get; } = totalBytes;
    public string RowPayloadBytesFormatted => RowPayloadBytes.ToFileSize();
    /// <summary>Compatibility alias for <see cref="RowPayloadBytes"/>.</summary>
    public long TotalBytes => RowPayloadBytes;
    public string TotalBytesFormatted => RowPayloadBytesFormatted;
    public DateTime? NewestDateTime => NewestTick.HasValue ? new(NewestTick.Value) : null;
    public DateTime? OldestDateTime => OldestTick.HasValue ? new(OldestTick.Value) : null;
}
