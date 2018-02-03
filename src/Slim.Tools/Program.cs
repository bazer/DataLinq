using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Slim.MySql;

namespace Slim.Tools
{
    static class Program
    {
        static string ConnectionString { get; set; }
        static string DbName { get; set; }
        static string WritePath { get; set; }
        static string Namespace { get; set; }

        static void Main(string[] args)
        {
            DatabaseFixture();

            DbName = args[0];
            Namespace = args[1];
            WritePath = Path.GetFullPath(args[2]);

            new CreateModels().Execute(DbName, Namespace, WritePath);

        }

        static void DatabaseFixture()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            var configuration = builder.Build();

            ConnectionString = configuration.GetConnectionString("Slim");
            DbAccess.ConnectionString = ConnectionString;

            DbName = "slim";
        }
    }
}
