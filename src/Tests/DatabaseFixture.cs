using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Slim.MySql;
using Tests.Models;
using Xunit;

namespace Tests
{
    [CollectionDefinition("Database")]
    public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
    {
    }

    public class DatabaseFixture : IDisposable
    {
        public DatabaseFixture()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            var configuration = builder.Build();
            ConnectionString = configuration.GetConnectionString("Employees");

            Provider = new MySQLProvider<employeesDb>(ConnectionString);
        }

        public MySQLProvider<employeesDb> Provider { get; set; }
        public employeesDb Query => Provider.Query;

        public string ConnectionString { get; private set; }
        public string DbName { get; private set; }

        public void Dispose()
        {
        }
    }
}