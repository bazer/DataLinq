using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataLinq.Logging;

public sealed class DataLinqLoggingConfiguration(ILoggerFactory loggerFactory)
{
    public static DataLinqLoggingConfiguration NullConfiguration { get; } = new DataLinqLoggingConfiguration(NullLoggerFactory.Instance);

    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public ILogger SqlCommandLogger { get; } = loggerFactory.CreateLogger("DataLinq.SqlCommand");
    public ILogger TransactionLogger { get; } = loggerFactory.CreateLogger("DataLinq.Transaction");
    public ILogger CacheLogger { get; } = loggerFactory.CreateLogger("DataLinq.Cache");
}
