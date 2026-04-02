using System;
using System.Collections.Generic;
using System.Linq;
using Bogus;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Testing;

internal static class EmployeesBogusSeeder
{
    private const int DeterministicSeed = 59345922;
    private const int EmployeeCount = 1000;

    public static void Seed(Database<EmployeesDb> database)
    {
        using var transaction = database.Transaction();

        var employeeFaker = new Faker<MutableEmployee>()
            .UseSeed(DeterministicSeed)
            .StrictMode(false)
            .RuleFor(x => x.first_name, x => x.Person.FirstName)
            .RuleFor(x => x.last_name, x => x.Person.LastName)
            .RuleFor(x => x.birth_date, x => DateOnly.FromDateTime(x.Person.DateOfBirth.Date))
            .RuleFor(x => x.hire_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.gender, x => (Employee.Employeegender)(((int)x.Person.Gender) + 1))
            .RuleFor(x => x.last_login, x => TimeOnly.FromDateTime(x.Date.Past(1)))
            .RuleFor(x => x.created_at, x => x.Date.Past(5));
        var employees = transaction.Insert(employeeFaker.Generate(EmployeeCount));

        var usedDepartmentNames = new HashSet<string>(StringComparer.Ordinal);
        var departmentNumber = 1;
        var departmentFaker = new Faker<MutableDepartment>()
            .UseSeed(DeterministicSeed)
            .StrictMode(false)
            .RuleFor(x => x.DeptNo, _ => $"d{departmentNumber++:000}")
            .RuleFor(x => x.Name, x =>
            {
                string name;
                do
                {
                    name = x.Commerce.Department();
                } while (!usedDepartmentNames.Add(name));

                return name;
            });
        var departments = transaction.Insert(departmentFaker.Generate(20));

        var employeeIndex = 0;
        var departmentEmployeeFaker = new Faker<MutableDept_emp>()
            .UseSeed(DeterministicSeed)
            .StrictMode(false)
            .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.emp_no, _ => employees[employeeIndex++].emp_no)
            .RuleFor(x => x.dept_no, x => x.PickRandom(departments).DeptNo);
        transaction.Insert(departmentEmployeeFaker.Generate(EmployeeCount));

        employeeIndex = 0;
        var titlesFaker = new Faker<MutableTitles>()
            .UseSeed(DeterministicSeed)
            .StrictMode(false)
            .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.emp_no, _ => employees[employeeIndex++].emp_no)
            .RuleFor(x => x.title, x => x.Name.JobTitle());
        transaction.Insert(titlesFaker.Generate(EmployeeCount));

        employeeIndex = 0;
        var salariesFaker = new Faker<MutableSalaries>()
            .UseSeed(DeterministicSeed)
            .StrictMode(false)
            .RuleFor(x => x.FromDate, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.ToDate, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.emp_no, _ => employees[employeeIndex++].emp_no)
            .RuleFor(x => x.salary, x => (uint)x.Finance.Amount(10000, 200000, 0));
        transaction.Insert(salariesFaker.Generate(EmployeeCount));

        var managerFaker = new Faker<MutableManager>()
            .UseSeed(DeterministicSeed)
            .StrictMode(false)
            .RuleFor(x => x.from_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.to_date, x => x.Date.PastDateOnly(20))
            .RuleFor(x => x.Type, x => x.PickRandom<ManagerType>())
            .RuleFor(x => x.emp_no, x => x.PickRandom(employees).emp_no)
            .RuleFor(x => x.dept_fk, x => x.PickRandom(departments).DeptNo);

        foreach (var manager in managerFaker.Generate(EmployeeCount / 10))
        {
            if (!transaction.Query().Managers.Any(x => x.dept_fk == manager.dept_fk && x.emp_no == manager.emp_no))
                transaction.Insert(manager);
        }

        transaction.Commit();
    }
}
