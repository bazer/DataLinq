using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Slim.MySql;
using Slim.MySql.Models;
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
            employeesDb_provider = new MySQLProvider<employeesDb>(configuration.GetConnectionString("Employees"));
            information_schema_provider = new MySQLProvider<information_schema>(configuration.GetConnectionString("information_schema"));
        }

        public MySQLProvider<employeesDb> employeesDb_provider { get; set; }
        public employeesDb employeesDb => employeesDb_provider.Read();
        public MySQLProvider<information_schema> information_schema_provider { get; set; }
        public information_schema information_schema => information_schema_provider.Read();

        //public string ConnectionString { get; private set; }
        public string DbName { get; private set; }

        public void Dispose()
        {
        }
    }
}