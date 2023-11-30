using BenchmarkDotNet.Attributes;
using DataLinq.Config;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Allround;

namespace DataLinq.Benchmark;

public class BenchmarkSetup
{
    private Database<AllroundBenchmark> db;

    public BenchmarkSetup()
    {
        MySQLProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();

        DataLinqConfig config = DataLinqConfig.FindAndReadConfigs("D:\\git\\DataLinq\\src\\DataLinq.Benchmark\\datalinq.json", Console.WriteLine);
        var conn = config.Databases.Single(x => x.Name == "AllroundBenchmark").Connections.Single(x => x.Type == DatabaseType.MySQL);

        db = new MySqlDatabase<AllroundBenchmark>(conn.ConnectionString.Original, conn.DatabaseName);
    }

    [GlobalSetup]
    public void Setup()
    {
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
