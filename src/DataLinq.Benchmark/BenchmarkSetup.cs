using BenchmarkDotNet.Attributes;
using DataLinq.Config;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Allround;

namespace DataLinq.Benchmark;

public class BenchmarkSetup
{
    private Database<AllroundBenchmark> db;
    private DataLinqDatabaseConnection conn;

    public BenchmarkSetup()
    {
        MySQLProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();

        DataLinqConfig config = DataLinqConfig.FindAndReadConfigs("D:\\git\\DataLinq\\src\\DataLinq.Benchmark\\datalinq.json", Console.WriteLine);
        conn = config.Databases.Single(x => x.Name == "AllroundBenchmark").Connections.Single(x => x.Type == DatabaseType.MySQL);
        db = new MySqlDatabase<AllroundBenchmark>(conn.ConnectionString.Original, conn.DataSourceName);
    }

    [GlobalSetup]
    public void Setup()
    {
        if (!db.FileOrServerExists() || !db.Exists())
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
            var users = db.Query().Productreviews.ToList();
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
