using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests
{
    public class ThreadingTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture fixture;
        private Helpers helpers;

        public ThreadingTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
            this.helpers = new Helpers(fixture);
        }

        [Fact]
        public void ReadParallel()
        {
            Parallel.For(0, 10, i =>
            {
                SetAndTest(10004);
                SetAndTest(10005);
                SetAndTest(10006);
                SetAndTest(10007);
                SetAndTest(10008);
            });
        }

        private void SetAndTest(int value)
        {
            var employee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == value);
            Assert.Equal(value, employee.emp_no);
        }

        [Fact]
        public void CommitTransactionParallel()
        {
            var emp_no = 999990;

            Parallel.For(0, 100, i =>
            {
                var employee = helpers.GetEmployee(emp_no - i);
                var orgBirthDate = employee.birth_date;
                var employeeMut = employee.Mutate();

                var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
                employeeMut.birth_date = newBirthDate;
                Assert.Equal(newBirthDate, employeeMut.birth_date);

                using var transaction = fixture.employeesDb.Transaction();
                var dbEmployeeReturn = transaction.Update(employeeMut);

                transaction.Commit();

                var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == emp_no - i);
                Assert.Equal(newBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            });
        }

        
    }
}