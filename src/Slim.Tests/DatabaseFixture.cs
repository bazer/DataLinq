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
            employeesDb = new MySqlDatabase<employeesDb>(configuration.GetConnectionString("Employees"));
            information_schema = new MySqlDatabase<information_schema>(configuration.GetConnectionString("information_schema"));
        }

        public MySqlDatabase<employeesDb> employeesDb { get; set; }
        //public employeesDb employeesDb => employeesDb_provider.Read();
        public MySqlDatabase<information_schema> information_schema { get; set; }
        //public information_schema information_schema => information_schema_provider.Read();

        //public string ConnectionString { get; private set; }
        public string DbName { get; private set; }

        public void Dispose()
        {
        }
    }
}