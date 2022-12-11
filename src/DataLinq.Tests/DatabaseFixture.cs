using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tests.Models;
using Xunit;
using DataLinq.Metadata;
using Bogus;
using System.Linq;
using System.Collections.Generic;
using DataLinq.Config;
using System.Reflection;
using Xunit.Sdk;
using DataLinq.SQLite;

namespace DataLinq.Tests
{
    public class DatabaseFixture : IDisposable
    {
        static DatabaseFixture()
        {
            MySQLProvider.RegisterProvider();
            SQLiteProvider.RegisterProvider();
        }

        public DatabaseFixture()
        {
            var config = ConfigReader.Read("datalinq.json");

            var employees = config.Databases.Single(x => x.Name == "employees");

            //var builder = new ConfigurationBuilder()
            //    .SetBasePath(Directory.GetCurrentDirectory())
            //    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            //var configuration = builder.Build();
            //var connDataLinq = configuration.GetConnectionString("employees");
            //EmployeesDbName = configuration.GetSection("employeesDbName")?.Value ?? "employees";

            EmployeeConnections = employees.Connections;

            foreach (var connection in employees.Connections)
            {
                var provider = PluginHook.DatabaseProviders.Single(x => x.Key == connection.ParsedType).Value;

                var dbEmployees = provider.GetDatabaseProvider<Employees>(connection.ConnectionString, connection.DatabaseName);

                //if (!dbEmployees.Exists())
                //{
                //    //MySql.SqlFromMetadataFactory.Register();

                //    PluginHook.CreateDatabaseFromMetadata(connection.ParsedType.Value,
                //        employeesDb.Provider.Metadata, connection.DatabaseName, connection.ConnectionString, true);

                //    FillEmployeesWithBogusData(employeesDb);
                //}

                AllEmployeesDb.Add(dbEmployees);
            }
            employeesDb = AllEmployeesDb[0];
            //employeesDb = new MySqlDatabase<Employees>(connDataLinq, EmployeesDbName);
            //information_schema = new MySqlDatabase<information_schema>(configuration.GetConnectionString("information_schema"));

            //if (!employeesDb.Exists())
            //{
            //    //MySql.SqlFromMetadataFactory.Register();

            //    PluginHook.CreateDatabaseFromMetadata(DatabaseType.MySQL, 
            //        employeesDb.Provider.Metadata, EmployeesDbName, connDataLinq, true);

            //    FillEmployeesWithBogusData(employeesDb);
            //}
        }

        public List<DatabaseConnectionConfig> EmployeeConnections { get; set; } = new();
        public List<Database<Employees>> AllEmployeesDb { get; set; } = new();
        public Database<Employees> employeesDb { get; set; }
        //public employeesDb employeesDb => employeesDb_provider.Read();
        //public MySqlDatabase<information_schema> information_schema { get; set; }
        //public information_schema information_schema => information_schema_provider.Read();

        //public string ConnectionString { get; private set; }
        //public string EmployeesDbName { get; private set; }

        public void FillEmployeesWithBogusData(Database<Employees> database)
        {
            Randomizer.Seed = new Random(59345922);

            var numEmployees = 10000;
            using var transaction = database.Transaction();

            var employeeFaker = new Faker<Employee>()
                .StrictMode(false)
                .RuleFor(x => x.first_name, x => x.Person.FirstName)
                .RuleFor(x => x.last_name, x => x.Person.LastName)
                .RuleFor(x => x.birth_date, x => DateOnly.FromDateTime(x.Person.DateOfBirth.Date))
                .RuleFor(x => x.hire_date, x => x.Date.PastDateOnly(20))
                .RuleFor(x => x.gender, x => (Employee.Employeegender)(((int)x.Person.Gender) + 1));
            var employees = transaction.Insert(employeeFaker.Generate(numEmployees));

            var deptNo = 1;
            var departmentFaker = new Faker<Department>()
                .StrictMode(false)
                .RuleFor(x => x.DeptNo, x => $"d{deptNo++:000}")
                .RuleFor(x => x.Name, x => x.Commerce.Department());
            var departments = transaction.Insert(departmentFaker.Generate(20));

            var empNo = 0;
            var dept_empFaker = new Faker<dept_emp>()
               .StrictMode(false)
               .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.emp_no, x => employees[empNo++].emp_no)
               .RuleFor(x => x.dept_no, x => x.PickRandom(departments).DeptNo);
            transaction.Insert(dept_empFaker.Generate(numEmployees));

            empNo = 0;
            var titlesFaker = new Faker<titles>()
               .StrictMode(false)
               .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.emp_no, x => employees[empNo++].emp_no)
               .RuleFor(x => x.title, x => x.Name.JobTitle());
            transaction.Insert(titlesFaker.Generate(numEmployees));

            empNo = 0;
            var salariesFaker = new Faker<salaries>()
               .StrictMode(false)
               .RuleFor(x => x.FromDate, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.ToDate, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.emp_no, x => employees[empNo++].emp_no)
               .RuleFor(x => x.salary, x => (int)x.Finance.Amount(10000, 200000, 0));
            transaction.Insert(salariesFaker.Generate(numEmployees));

            var dept_managerFaker = new Faker<Manager>()
               .StrictMode(false)
               .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.emp_no, x => x.PickRandom(employees).emp_no)
               .RuleFor(x => x.dept_no, x => x.PickRandom(departments).DeptNo);

            var dept_managers = dept_managerFaker.Generate(numEmployees / 10);

            foreach (var dm in dept_managers)
            {
                if (!transaction.Query().Managers.Any(x => x.dept_no == dm.dept_no && x.emp_no == dm.emp_no))
                    transaction.Insert(dm);
            }

            transaction.Commit();
        }

        public void Dispose()
        {
            employeesDb.Dispose();
            //information_schema.Dispose();
        }
    }
}