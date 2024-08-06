using System;
using System.Linq;
using DataLinq.Tests.Models;

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
        var employee = employeesDb.Query().Employees.SingleOrDefault(x => x.emp_no == emp_no) ?? NewEmployee(emp_no);

        if (employee.IsNewModel())
            return employeesDb.Insert(employee);

        return employee;
    }

    public Employee NewEmployee(int? emp_no = null)
    {
        return new Employee
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
