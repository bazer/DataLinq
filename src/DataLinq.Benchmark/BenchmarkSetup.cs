using BenchmarkDotNet.Attributes;
using DataLinq.Config;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Allround;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DataLinq.Benchmark;

public class BenchmarkSetup
{
    private Database<AllroundBenchmark> db;
    private DataLinqDatabaseConnection conn;

    public BenchmarkSetup()
    {
        MySQLProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.File("D:\\git\\DataLinq\\logs\\benchmark.txt", rollingInterval: RollingInterval.Day, flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        // Set up logging with Serilog
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        //var logger = loggerFactory.CreateLogger<Program>();

        //var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        DataLinqConfig config = DataLinqConfig.FindAndReadConfigs("D:\\git\\DataLinq\\src\\DataLinq.Tests.Models\\datalinq.json", Console.WriteLine);
        conn = config.Databases.Single(x => x.Name == "AllroundBenchmark").Connections.Single(x => x.Type == DatabaseType.MySQL);
        db = new MySqlDatabase<AllroundBenchmark>(conn.ConnectionString.Original, conn.DataSourceName, loggerFactory);
    }

    [GlobalSetup]
    public void Setup()
    {
        if (!db.FileOrServerExists() || !db.DatabaseExists())
        {
            PluginHook.CreateDatabaseFromMetadata(conn.Type,
                db.Provider.Metadata, conn.DataSourceName, conn.ConnectionString.Original, true);
        }

        // Check if the database has already been populated
        if (!IsDatabasePopulated())
        {
            AllroundBenchmarkBogusData.FillAllroundBenchmarkWithBogusData(db);
        }
    }

    private bool IsDatabasePopulated()
    {
        // This is a simple check to see if the Users table has any data
        // You can expand this check to other primary tables if desired
        return db.Query().Users.Any();
    }

    [Benchmark]
    public void LoadAllUsers()
    {
        for (int i = 0; i < 1000; i++)
        {
            var reviews = db.Query().Productreviews.Take(1000).ToArray();

            foreach (var user in reviews.Select(x => x.users))
            {
                var orders = user.orders.ToList();
                //Console.WriteLine($"Num orders for user {user.UserName}: {user.orders.Count()}");
            }
        }
    }

    [Benchmark]
    public void YourBenchmarkMethod()
    {
        var users = db.Query().Users.Where(x => x.UserName.StartsWith("John")).ToList();
        var orders = db.Query().Orders.Where(x => x.OrderTimestamp < DateTime.Now).ToList();

        Console.WriteLine($"{users.Count} users");
        Console.WriteLine($"{orders.Count} orders");

    }

    // ... Your benchmarking methods ...

    // And the rest of your FillAllroundBenchmarkWithBogusData method ...
}
