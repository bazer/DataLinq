using System;
using System.Linq;
using DataLinq.Tests.Models;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests;

internal class Helpers
{
    private Random rnd = new Random();
    //private DatabaseFixture fixture { get; }

    public Helpers()
    {
        //this.fixture = fixture;
    }

    public Employee GetEmployee(int? emp_no, Database<EmployeesDb> employeesDb)
    {
        var employee = employeesDb.Query().Employees.SingleOrDefault(x => x.emp_no == emp_no);

        if (employee is null)
            return employeesDb.Insert(NewEmployee(emp_no));

        return employee;
    }

    public MutableEmployee NewEmployee(int? emp_no = null)
    {
        return new MutableEmployee
        {
            birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
            emp_no = emp_no,
            first_name = "Test employee",
            last_name = "Test",
            gender = Employee.Employeegender.M,
            hire_date = DateOnly.FromDateTime(DateTime.Now)
        };
    }

    public DateOnly RandomDate(DateTime rangeStart, DateTime rangeEnd)
    {
        TimeSpan span = rangeEnd - rangeStart;

        int randomMinutes = rnd.Next(0, (int)span.TotalMinutes);
        return DateOnly.FromDateTime(rangeStart + TimeSpan.FromMinutes(randomMinutes));
    }
}
