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

            Employees = new MySQLProvider<employeesDb>(ConnectionString);

            //provider.Query.dept_manager

            //provider.

            //Employees = new employeesDb(provider);

            //DbAccess.ConnectionString = ConnectionString;

            //DbName = "slim";

            //CreateDatabase();
        }

        public MySQLProvider<employeesDb> Employees { get; set; }

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