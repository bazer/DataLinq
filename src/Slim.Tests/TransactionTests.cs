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
        public void Insert()
        {
            var emp_no = 999999;

            var employee = NewEmployee(emp_no);
            Assert.True(employee.HasPrimaryKeysSet());

            using (var transaction = fixture.employeesDb.Transaction())
            {
                foreach (var alreadyExists in fixture.employeesDb.Query().employees.Where(x => x.emp_no == emp_no))
                    transaction.Delete(alreadyExists);

                transaction.Insert(employee);
                Assert.True(employee.HasPrimaryKeysSet());
                var dbTransactionEmployee = transaction.From().employees.Single(x => x.emp_no == emp_no);
                Assert.Equal(employee.birth_date.ToShortDateString(), dbTransactionEmployee.birth_date.ToShortDateString());

                transaction.Commit();
            }

            var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == emp_no);

            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        }

        [Fact]
        public void InsertAutoIncrement()
        {
            var employee = NewEmployee();
            Assert.False(employee.HasPrimaryKeysSet());

            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Insert(employee);
                Assert.NotNull(employee.emp_no);
                Assert.True(employee.HasPrimaryKeysSet());

                var dbTransactionEmployee = transaction.From().employees.Single(x => x.emp_no == employee.emp_no);
                Assert.Equal(employee.birth_date.ToShortDateString(), dbTransactionEmployee.birth_date.ToShortDateString());
                Assert.True(dbTransactionEmployee.HasPrimaryKeysSet());

                transaction.Commit();
            }

            var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == employee.emp_no);

            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            Assert.True(dbEmployee.HasPrimaryKeysSet());

            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Delete(dbEmployee);
                transaction.Commit();
            }

            Assert.False(fixture.employeesDb.Query().employees.Any(x => x.emp_no == employee.emp_no));
        }

        [Fact]
        public void InsertAndUpdateAutoIncrement()
        {
            var employee = NewEmployee();
            Assert.False(employee.HasPrimaryKeysSet());

            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Insert(employee);
                Assert.NotNull(employee.emp_no);
                Assert.True(employee.HasPrimaryKeysSet());

                transaction.Commit();
            }

            Assert.True(employee.HasPrimaryKeysSet());
            employee.birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));


            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Update(employee);
                Assert.True(employee.HasPrimaryKeysSet());
                transaction.Commit();
            }

            var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == employee.emp_no);
            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            Assert.True(dbEmployee.HasPrimaryKeysSet());

            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Delete(dbEmployee);
                transaction.Commit();
            }

            Assert.False(fixture.employeesDb.Query().employees.Any(x => x.emp_no == employee.emp_no));
        }

        [Fact]
        public void Update()
        {
            var emp_no = 999997;

            var employee = fixture.employeesDb.Query().employees.SingleOrDefault(x => x.emp_no == emp_no) ?? NewEmployee(emp_no);

            if (employee.IsNewModel())
            {
                fixture.employeesDb.Transaction().Insert(employee).Commit();
                employee = fixture.employeesDb.Query().employees.SingleOrDefault(x => x.emp_no == emp_no);
            }

            var orgBirthDate = employee.birth_date;

            var employeeMut = employee.Mutate();

            var newBirthDate = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            fixture.employeesDb.Transaction().Update(employeeMut).Commit();

            var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == emp_no);

            Assert.Equal(employeeMut.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        }

        private employees NewEmployee(int? emp_no = null)
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