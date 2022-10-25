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

namespace DataLinq.Tests
{
    //[CollectionDefinition("Database")]
    //public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
    //{
    //}

    public class DatabaseFixture : IDisposable
    {
        public DatabaseFixture()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            var configuration = builder.Build();
            var connDataLinq = configuration.GetConnectionString("employees");
            EmployeesDbName = configuration.GetSection("employeesDbName")?.Value ?? "employees";
            employeesDb = new MySqlDatabase<employees>(connDataLinq, EmployeesDbName);
            information_schema = new MySqlDatabase<information_schema>(configuration.GetConnectionString("information_schema"));
        
            if (!employeesDb.Exists())
            {
                MySql.SqlFromMetadataFactory.Register();

                DatabaseFactory.CreateDatabaseFromMetadata(DatabaseType.MySQL, 
                    employeesDb.Provider.Metadata, EmployeesDbName, connDataLinq, true);

                FillEmployeesWithBogusData(employeesDb);
            }
        }

        public MySqlDatabase<employees> employeesDb { get; set; }
        //public employeesDb employeesDb => employeesDb_provider.Read();
        public MySqlDatabase<information_schema> information_schema { get; set; }
        //public information_schema information_schema => information_schema_provider.Read();

        //public string ConnectionString { get; private set; }
        public string EmployeesDbName { get; private set; }

        public void FillEmployeesWithBogusData(Database<employees> database)
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
               .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
               .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
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
            information_schema.Dispose();
        }
    }
}