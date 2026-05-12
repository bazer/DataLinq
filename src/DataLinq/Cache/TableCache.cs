using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Extensions.Helpers;
using DataLinq.Instances;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Utils;

namespace DataLinq.Cache;

public partial class TableCache
{
    private readonly object indexCacheGate = new();
    private Dictionary<ColumnIndex, IIndexCache>? indexCaches;
    private RowCache? rowCache;
    private ConcurrentDictionary<Transaction, RowCache>? transactionRows;

    protected int primaryKeyColumnsCount;
    protected IReadOnlyList<ColumnIndex> indices;
    protected (IndexCacheType type, int? amount) indexCachePolicy;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;
    private readonly DataLinqTelemetryContext telemetryContext;
    internal DataLinqTableMetricsHandle MetricsHandle { get; }

    // This table weakly maps a relation object to its subscription manager.
    private CacheNotificationManager? notificationManager;

    public TableCache(TableDefinition table, DatabaseCache databaseCache, DataLinqLoggingConfiguration loggingConfiguration)
    {
        this.Table = table;
        this.DatabaseCache = databaseCache;
        this.loggingConfiguration = loggingConfiguration;
        this.telemetryContext = DataLinqTelemetryContext.FromProvider(databaseCache.Database);
        this.primaryKeyColumnsCount = Table.PrimaryKeyColumns.Length;
        this.indices = Table.ColumnIndices;
        this.indexCachePolicy = GetIndexCachePolicy();
        MetricsHandle = DataLinqMetrics.RegisterTable(databaseCache.Database, table.DbName);
        DataLinqTelemetry.RegisterTableCache(
            telemetryContext,
            table.DbName,
            GetOccupancySnapshot,
            MetricsHandle.GetCacheNotificationSnapshot);

        RefreshOccupancyMetrics();
    }

    public long? OldestTick => rowCache?.OldestTick;
    public long? NewestTick => rowCache?.NewestTick;
    public int RowCount => rowCache?.Count ?? 0;
    public long TotalBytes => rowCache?.TotalBytes ?? 0;
    public string TotalBytesFormatted => TotalBytes.ToFileSize();
    public int TransactionRowsCount => transactionRows?.Count ?? 0;
    public IEnumerable<(string index, int count)> IndicesCount => indices.Select(x => (x.Name, TryGetIndexCache(x)?.Count ?? 0));

    public TableDefinition Table { get; }
    public DatabaseCache DatabaseCache { get; }

    private RowCache GetOrCreateRowCache()
    {
        var cache = rowCache;
        if (cache is not null)
            return cache;

        cache = new RowCache();
        var existing = Interlocked.CompareExchange(ref rowCache, cache, null);
        return existing ?? cache;
    }

    private ConcurrentDictionary<Transaction, RowCache> GetOrCreateTransactionRows()
    {
        var rows = transactionRows;
        if (rows is not null)
            return rows;

        rows = new ConcurrentDictionary<Transaction, RowCache>();
        var existing = Interlocked.CompareExchange(ref transactionRows, rows, null);
        return existing ?? rows;
    }
}
