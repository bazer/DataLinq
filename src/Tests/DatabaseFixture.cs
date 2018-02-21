using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using Modl.Db;
using Modl.Db.DatabaseProviders;
using MySql.Data.MySqlClient;
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

            ConnectionString = configuration.GetConnectionString("Slim");

            var provider = new MySQLProvider<employeesDb>("employees", ConnectionString);

            //provider.

            Employees = new employeesDb(provider);

            //DbAccess.ConnectionString = ConnectionString;

            //DbName = "slim";

            //CreateDatabase();
        }

        public employeesDb Employees { get; set; }

        public string ConnectionString { get; private set; }
        public string DbName { get; private set; }

        //public void CreateDatabase()
        //{
        //    Action<string> dropTable = name => DbAccess.ExecuteNonQuery($"DROP TABLE IF EXISTS `{name}`;");

        //    //DbAccess.ExecuteNonQuery($"DROP DATABASE IF EXISTS {DbName}; CREATE DATABASE {DbName}; USE {DbName};");
        //    dropTable("Stuff");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE Stuff (TheId int not null AUTO_INCREMENT PRIMARY KEY, Name nvarchar(100) not null, Created DateTime null);");
        //    dropTable("People");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE People (Id int not null AUTO_INCREMENT PRIMARY KEY, Name nvarchar(100) not null);");
        //    dropTable("Users");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE Users (Id int not null AUTO_INCREMENT PRIMARY KEY, Name nvarchar(100) not null, Age int not null);");
        //    dropTable("Automobiles");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE Automobiles (Id int not null AUTO_INCREMENT PRIMARY KEY, Name nvarchar(100) not null);");
        //    dropTable("Results");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE Results (Id int not null AUTO_INCREMENT PRIMARY KEY, Name nvarchar(100) not null, `Order` int not null);");
        //    dropTable("ObjectX");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE ObjectX (ObjectXId nvarchar(100) not null, Name nvarchar(100) not null);");
        //    dropTable("ObjectY");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE ObjectY (ObjectYId int not null, Name nvarchar(100) not null);");
        //    dropTable("ObjectZ");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE ObjectZ (Id int not null, Name nvarchar(100) not null);");
        //    dropTable("GenericType");
        //    DbAccess.ExecuteNonQuery("CREATE TABLE GenericType (Id nvarchar(100) not null, Name nvarchar(100) not null);");
        //}

        public void Dispose()
        {
            
        }
    }
}
