using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Tests;
using DataLinq.Tests.Models;

namespace DataLinq.Tests
{
    internal class Helpers
    {
        private Random rnd = new Random();
        private DatabaseFixture fixture { get; }

        public Helpers(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        public employees GetEmployee(int? emp_no)
        {
            var employee = fixture.employeesDb.Query().employees.SingleOrDefault(x => x.emp_no == emp_no) ?? NewEmployee(emp_no);

            if (employee.IsNewModel())
                return fixture.employeesDb.Insert(employee);

            return employee;
        }

        public employees NewEmployee(int? emp_no = null)
        {
            return new employees
            {
                birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                emp_no = emp_no,
                first_name = "Test employee",
                last_name = "Test",
                gender = 1,
                hire_date = DateTime.Now
            };
        }

        public DateTime RandomDate(DateTime rangeStart, DateTime rangeEnd)
        {
            TimeSpan span = rangeEnd - rangeStart;

            int randomMinutes = rnd.Next(0, (int)span.TotalMinutes);
            return rangeStart + TimeSpan.FromMinutes(randomMinutes);
        }
    }
}
