using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataLinq.Config;
using DataLinq.MariaDB;
using DataLinq.MariaDB.information_schema;
using DataLinq.Metadata;
using DataLinq.MySql.information_schema;
using DataLinq.Tests.Models.Employees;
using Microsoft.Extensions.Logging;
using Serilog;
using Xunit;

namespace DataLinq.MySql.Tests;

public class DatabaseFixture : IDisposable
{
    public static readonly List<DatabaseType> SupportedDatabaseTypes =
    [
        DatabaseType.MySQL,
        DatabaseType.MariaDB
    ];

    static DatabaseFixture()
    {
        MySQLProvider.RegisterProvider();
        MariaDBProvider.RegisterProvider();

        DataLinqConfig = DataLinqConfig.FindAndReadConfigs($"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json", _ => { });
    }

    public DatabaseFixture()
    {
        var employees = DataLinqConfig.Databases.Single(x => x.Name == "employees");

        EmployeeConnections = employees.Connections.Where(x => x.Type == DatabaseType.MySQL || x.Type == DatabaseType.MariaDB).ToList();
        var lockObject = new object();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("D:\\git\\DataLinq\\logs\\tests.txt", rollingInterval: RollingInterval.Day, flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        // Set up logging with Serilog
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        //var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));

        foreach (var connection in EmployeeConnections)
        {
            var provider = PluginHook.DatabaseProviders.Single(x => x.Key == connection.Type).Value;
            provider.UseLoggerFactory(loggerFactory);

            var dbEmployees = provider.GetDatabaseProvider<EmployeesDb>(connection.ConnectionString.Original, connection.DataSourceName);

            lock (lockObject)
            {
                if (!dbEmployees.FileOrServerExists() || !dbEmployees.Exists())
                {
                    var result = connection.Type.CreateDatabaseFromMetadata(                        dbEmployees.Provider.Metadata, connection.DataSourceName, connection.ConnectionString.Original, true);

                    if (result.HasFailed)
                        Assert.Fail(result.Failure.ToString());
                }

                //if (!dbEmployees.Query().Employees.Any())
                //{
                //    FillEmployeesWithBogusData(dbEmployees);
                //}
            }

            AllEmployeesDb.Add(dbEmployees);

            //CleanupTestEmployees(dbEmployees);
        }

        if (EmployeeConnections.Count(x => x.Type == DatabaseType.MariaDB) != 1)
            MariaDB_information_schema = new MariaDBDatabase<MariaDBInformationSchema>(EmployeeConnections.Single(x => x.Type == DatabaseType.MariaDB).ConnectionString.Original);

        if (EmployeeConnections.Count(x => x.Type == DatabaseType.MySQL) != 1)
            MySQL_information_schema = new MySqlDatabase<MySQLInformationSchema>(EmployeeConnections.Single(x => x.Type == DatabaseType.MySQL).ConnectionString.Original);
    }

    public static DataLinqConfig DataLinqConfig { get; set; }
    public List<DataLinqDatabaseConnection> EmployeeConnections { get; set; } = new();
    public List<Database<EmployeesDb>> AllEmployeesDb { get; set; } = new();
    public MariaDBDatabase<MariaDBInformationSchema>? MariaDB_information_schema { get; set; }
    public MySqlDatabase<MySQLInformationSchema>? MySQL_information_schema { get; set; }

    

    public void Dispose()
    {
        foreach (var db in AllEmployeesDb)
            db.Dispose();

        MariaDB_information_schema?.Dispose();
        MySQL_information_schema?.Dispose();
    }
}