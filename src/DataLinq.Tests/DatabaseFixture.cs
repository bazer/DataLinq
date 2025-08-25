using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bogus;
using DataLinq.Config;
using DataLinq.MariaDB;
using DataLinq.MariaDB.information_schema;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.information_schema;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using Microsoft.Extensions.Logging;
using Serilog;
using Xunit;

namespace DataLinq.Tests;

public class DatabaseFixture : IDisposable
{
    static DatabaseFixture()
    {
        MySQLProvider.RegisterProvider();
        MariaDBProvider.RegisterProvider();
        SQLiteProvider.RegisterProvider();

        DataLinqConfig = DataLinqConfig.FindAndReadConfigs($"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}datalinq.json", _ => { });
    }

    public DatabaseFixture()
    {
        var employees = DataLinqConfig.Databases.Single(x => x.Name == "employees");

        EmployeeConnections = employees.Connections;
        var lockObject = new object();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("D:\\git\\DataLinq\\logs\\tests.txt", rollingInterval: RollingInterval.Day, flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        // Set up logging with Serilog
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        //var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));

        foreach (var connection in EmployeeConnections)
        {
            var provider = PluginHook.DatabaseProviders.Single(x => x.Key == connection.Type).Value;
            provider.UseLoggerFactory(loggerFactory);

            var dbEmployees = provider.GetDatabaseProvider<EmployeesDb>(connection.ConnectionString.Original, connection.DataSourceName);

            lock (lockObject)
            {
                if (!dbEmployees.FileOrServerExists() || !dbEmployees.DatabaseExists() || !dbEmployees.TableExists("employees"))
                {
                    var result = PluginHook.CreateDatabaseFromMetadata(connection.Type,
                        dbEmployees.Provider.Metadata, connection.DataSourceName, connection.ConnectionString.Original, true);

                    if (result.HasFailed)
                        Assert.Fail(result.Failure.ToString());
                }

                if (!dbEmployees.Query().Employees.Any())
                {
                    FillEmployeesWithBogusData(dbEmployees);
                }

                //CleanupTestEmployees(dbEmployees);
            }

            AllEmployeesDb.Add(dbEmployees);

        }

        if (EmployeeConnections.Count == 0)
            Assert.Fail("No employee connections found in datalinq.json");

        if (EmployeeConnections.Count(x => x.Type == DatabaseType.MariaDB) == 1)
            MariaDB_information_schema = new MariaDBDatabase<MariaDBInformationSchema>(EmployeeConnections.Single(x => x.Type == DatabaseType.MariaDB).ConnectionString.Original);

        if (EmployeeConnections.Count(x => x.Type == DatabaseType.MySQL) == 1)
            MySQL_information_schema = new MySqlDatabase<MySQLInformationSchema>(EmployeeConnections.Single(x => x.Type == DatabaseType.MySQL).ConnectionString.Original);
    }

    public static DataLinqConfig DataLinqConfig { get; set; }
    public List<DataLinqDatabaseConnection> EmployeeConnections { get; set; } = new();
    public List<Database<EmployeesDb>> AllEmployeesDb { get; set; } = new();
    public MariaDBDatabase<MariaDBInformationSchema>? MariaDB_information_schema { get; set; }
    public MySqlDatabase<MySQLInformationSchema>? MySQL_information_schema { get; set; }

    public void FillEmployeesWithBogusData(Database<EmployeesDb> database)
    {
        var seed = 59345922;

        var numEmployees = 1000;
        using var transaction = database.Transaction();

        var employeeFaker = new Faker<MutableEmployee>()
            .UseSeed(seed)
            .StrictMode(false)
            .RuleFor(x => x.first_name, x => x.Person.FirstName)
            .RuleFor(x => x.last_name, x => x.Person.LastName)
            .RuleFor(x => x.birth_date, x => DateOnly.FromDateTime(x.Person.DateOfBirth.Date))
            .RuleFor(x => x.hire_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.gender, x => (Employee.Employeegender)(((int)x.Person.Gender) + 1))
            .RuleFor(x => x.last_login, f => TimeOnly.FromDateTime(f.Date.Past(1)))
            .RuleFor(x => x.created_at, f => f.Date.Past(5));
        var employees = transaction.Insert(employeeFaker.Generate(numEmployees));

        var usedDepartmentNames = new HashSet<string>();
        var deptNo = 1;
        var departmentFaker = new Faker<MutableDepartment>()
            .UseSeed(seed)
            .StrictMode(false)
            .RuleFor(x => x.DeptNo, x => $"d{deptNo++:000}")
            .RuleFor(x => x.Name, x => {
                string name;
                do
                {
                    name = x.Commerce.Department();
                } while (usedDepartmentNames.Contains(name));

                usedDepartmentNames.Add(name);
                return name;
            });
        var departments = transaction.Insert(departmentFaker.Generate(20));

        var empNo = 0;
        var dept_empFaker = new Faker<MutableDept_emp>()
            .UseSeed(seed)
            .StrictMode(false)
            .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.emp_no, x => employees[empNo++].emp_no)
            .RuleFor(x => x.dept_no, x => x.PickRandom(departments).DeptNo);
        transaction.Insert(dept_empFaker.Generate(numEmployees));

        empNo = 0;
        var titlesFaker = new Faker<MutableTitles>()
            .UseSeed(seed)
            .StrictMode(false)
            .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.emp_no, x => employees[empNo++].emp_no)
            .RuleFor(x => x.title, x => x.Name.JobTitle());
        transaction.Insert(titlesFaker.Generate(numEmployees));

        empNo = 0;
        var salariesFaker = new Faker<MutableSalaries>()
            .UseSeed(seed)
            .StrictMode(false)
            .RuleFor(x => x.FromDate, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.ToDate, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.emp_no, x => employees[empNo++].emp_no)
            .RuleFor(x => x.salary, x => (uint)x.Finance.Amount(10000, 200000, 0));
        transaction.Insert(salariesFaker.Generate(numEmployees));

        var dept_managerFaker = new Faker<MutableManager>()
            .UseSeed(seed)
            .StrictMode(false)
            .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.Type, x => x.PickRandom<ManagerType>())
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

    // Helper to clean up specific test employees to avoid PK conflicts across test runs
    public void CleanupTestEmployees(Database<EmployeesDb> db, params int[] empNos)
    {
        var employeesToDelete = (empNos == null || empNos.Length == 0)
            ? db.Query().Employees.Where(e => e.emp_no >= 990000).ToList()
            : db.Query().Employees.Where(e => empNos.Contains(e.emp_no!.Value)).ToList();

        if (employeesToDelete.Any())
        {
            var transaction = db.Transaction();

            foreach (var emp in employeesToDelete)
            {
                foreach (var salary in emp.salaries)
                    salary.Delete(transaction);

                foreach (var dept_emp in emp.dept_emp)
                    dept_emp.Delete(transaction);

                foreach (var title in emp.titles)
                    title.Delete(transaction);

                emp.Delete(transaction);
            }

            transaction.Commit();

            db.Provider.State.ClearCache(); // Clear cache after delete
        }
    }


    public void Dispose()
    {
        foreach (var db in AllEmployeesDb)
            db.Dispose();

        MariaDB_information_schema?.Dispose();
        MySQL_information_schema?.Dispose();
    }
}