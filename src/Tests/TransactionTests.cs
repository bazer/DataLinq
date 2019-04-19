using System;
using System.Linq;
using Slim.Metadata;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class TransactionTests
    {
        private readonly DatabaseFixture fixture;
        private Random rnd = new Random();

        public TransactionTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void Add()
        {
            var emp_no = 999999;

            var employee = new employees
            {
                birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                emp_no = emp_no,
                first_name = "Test employee",
                last_name = "Test",
                gender = 1,
                hire_date = DateTime.Now
            };

            using (var transaction = fixture.employeesDb_provider.StartTransaction())
            {
                foreach (var alreadyExists in transaction.Read().employees.Where(x => x.emp_no == emp_no))
                    transaction.Delete(alreadyExists);

                transaction.Insert(employee);
                transaction.Commit();
            }

            var dbEmployee = fixture.employeesDb.employees.Single(x => x.emp_no == emp_no);

            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        }


        public DateTime RandomDate(DateTime rangeStart, DateTime rangeEnd)
        {
            TimeSpan span = rangeEnd - rangeStart;

            int randomMinutes = rnd.Next(0, (int)span.TotalMinutes);
            return rangeStart + TimeSpan.FromMinutes(randomMinutes);
        }

        //[Fact]
        //public void Mutate()
        //{
        //    using (var transaction = fixture.employeesDb_provider.StartTransaction())
        //    {
        //        var dept = transaction.Schema.current_dept_emp.First();

        //        dept.dept_no = "45353";


        //        transaction.Add(employee);
        //        transaction.Commit();
        //    }

        //    fixture.employeesDb_provider.Schema.

        //    Assert.Equal(9, fixture.employeesDb.departments.ToList().Count);
        //}
    }
}