namespace DataLinq.Logging;

internal static class EventIds
{
	// Sql
	public const int SqlCommand = 1000;

	// Cache
	public const int IndexCachePreload = 2000;
    public const int RowCachePreload = 2001;
    public const int LoadRowsFromCache = 2002;
    public const int LoadRowsFromDatabase = 2003;
}
