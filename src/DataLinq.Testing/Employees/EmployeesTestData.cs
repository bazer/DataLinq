using System;
using System.Linq;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Testing;

public sealed class EmployeesTestData
{
    private readonly Random _random = new();

    public Employee GetOrCreateEmployee(int? employeeNumber, Database<EmployeesDb> employeesDatabase)
    {
        var employee = employeesDatabase.Query().Employees.SingleOrDefault(x => x.emp_no == employeeNumber);
        return employee ?? employeesDatabase.Insert(NewEmployee(employeeNumber));
    }

    public MutableEmployee NewEmployee(int? employeeNumber = null)
    {
        return new MutableEmployee
        {
            birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
            emp_no = employeeNumber,
            first_name = "Test employee",
            last_name = "Test",
            gender = Employee.Employeegender.M,
            hire_date = DateOnly.FromDateTime(DateTime.Now)
        };
    }

    public DateOnly RandomDate(DateTime rangeStart, DateTime rangeEnd)
    {
        var span = rangeEnd - rangeStart;
        var randomMinutes = _random.Next(0, (int)span.TotalMinutes);
        return DateOnly.FromDateTime(rangeStart + TimeSpan.FromMinutes(randomMinutes));
    }
}
