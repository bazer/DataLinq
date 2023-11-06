using Bogus;
using DataLinq.Config;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.SQLite;
using DataLinq.Tests.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
            DataLinqConfig = DataLinqConfig.FindAndReadConfigs($"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json", _ => { });
            var employees = DataLinqConfig.Databases.Single(x => x.Name == "employees");

            EmployeeConnections = employees.Connections;
            var lockObject = new object();

            foreach (var connection in employees.Connections)
            {
                var provider = PluginHook.DatabaseProviders.Single(x => x.Key == connection.Type).Value;

                var dbEmployees = provider.GetDatabaseProvider<Employees>(connection.ConnectionString.Original, connection.DatabaseName);
                
                lock(lockObject)
                {
                    if (!dbEmployees.FileOrServerExists() || !dbEmployees.Exists())
                    {
                        PluginHook.CreateDatabaseFromMetadata(connection.Type,
                            dbEmployees.Provider.Metadata, connection.DatabaseName, connection.ConnectionString.Original, true);
                    }

                    if (dbEmployees.Query().Employees.Count() == 0)
                    {
                        FillEmployeesWithBogusData(dbEmployees);
                    }
                }

                AllEmployeesDb.Add(dbEmployees);
            }

            information_schema = new MySqlDatabase<information_schema>(EmployeeConnections.Single(x => x.Type == DatabaseType.MySQL).ConnectionString.Original);
        }

        public DataLinqConfig DataLinqConfig { get; set; }
        public List<DataLinqDatabaseConnection> EmployeeConnections { get; set; } = new();
        public List<Database<Employees>> AllEmployeesDb { get; set; } = new();
        public MySqlDatabase<information_schema> information_schema { get; set; }

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
               .RuleFor(x => x.Type, x => x.PickRandom<Manager.ManagerType>())
               .RuleFor(x => x.emp_no, x => x.PickRandom(employees).emp_no)
               .RuleFor(x => x.dept_fk, x => x.PickRandom(departments).DeptNo);

            var dept_managers = dept_managerFaker.Generate(numEmployees / 10);

            foreach (var dm in dept_managers)
            {
                if (!transaction.Query().Managers.Any(x => x.dept_fk == dm.dept_fk && x.emp_no == dm.emp_no))
                    transaction.Insert(dm);
            }

            transaction.Commit();
        }

        public void Dispose()
        {
            foreach (var db in AllEmployeesDb)
                db.Dispose();

            information_schema.Dispose();
        }
    }
}