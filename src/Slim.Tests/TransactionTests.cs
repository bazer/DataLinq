using System;
using System.Linq;
using Slim;
using Slim.Extensions;
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

            var employee = NewEmployee(emp_no);

            using (var transaction = fixture.employeesDb_provider.Transaction())
            {
                foreach (var alreadyExists in fixture.employeesDb.employees.Where(x => x.emp_no == emp_no))
                    transaction.Delete(alreadyExists);

                transaction.Insert(employee);
                var dbTransactionEmployee = transaction.Read().employees.Single(x => x.emp_no == emp_no);
                Assert.Equal(employee.birth_date.ToShortDateString(), dbTransactionEmployee.birth_date.ToShortDateString());

                transaction.Commit();
            }

            var dbEmployee = fixture.employeesDb.employees.Single(x => x.emp_no == emp_no);

            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        }

        [Fact]
        public void Update()
        {
            var emp_no = 999997;

            var employee = fixture.employeesDb.employees.SingleOrDefault(x => x.emp_no == emp_no) ?? NewEmployee(emp_no);

            if (employee.IsNew())
            {
                fixture.employeesDb_provider.Transaction().Insert(employee).Commit();
                employee = fixture.employeesDb.employees.SingleOrDefault(x => x.emp_no == emp_no);
            }

            var orgBirthDate = employee.birth_date;

            var employeeMut = employee.Mutate();

            var newBirthDate = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            fixture.employeesDb_provider.Transaction().Update(employeeMut).Commit();

            var dbEmployee = fixture.employeesDb.employees.Single(x => x.emp_no == emp_no);

            Assert.Equal(employeeMut.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        }

        private employees NewEmployee(int emp_no)
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