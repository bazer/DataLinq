using System.IO;
using Microsoft.Extensions.Configuration;
using DataLinq.MySql;
using DataLinq.MySql.Models;

namespace DataLinq.Tools
{
    internal static class Program
    {
        private static MySqlDatabase<information_schema> Database { get; set; }
        private static string DbName { get; set; }
        private static string WritePath { get; set; }
        private static string Namespace { get; set; }

        private static void Main(string[] args)
        {
            DatabaseFixture();

            DbName = args[0];
            Namespace = args[1];
            WritePath = Path.GetFullPath(args[2]);

            new CreateModels().Execute(DbName, Namespace, WritePath, Database);
        }

        private static void DatabaseFixture()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            var configuration = builder.Build();

            var connectionString = configuration.GetConnectionString("information_schema");
            Database = new MySqlDatabase<information_schema>(connectionString);
        }
    }
}