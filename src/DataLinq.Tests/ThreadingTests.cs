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
                SetAndTest(1004);
                SetAndTest(1005);
                SetAndTest(1006);
                SetAndTest(1007);
                SetAndTest(1008);
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

            Parallel.For(0, 10, i =>
            {
                var id = emp_no - i;

                var employee = helpers.GetEmployee(id);
                var orgBirthDate = employee.birth_date;
                var employeeMut = employee.Mutate();

                var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
                employeeMut.birth_date = newBirthDate;
                Assert.Equal(newBirthDate, employeeMut.birth_date);

                using var transaction = fixture.employeesDb.Transaction();
                var dbEmployeeReturn = transaction.Update(employeeMut);

                transaction.Commit();

                var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == id);
                Assert.NotEqual(orgBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
                Assert.Equal(newBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            });
        }

        //[Fact]
        //public void LazyLoadSingleValue()
        //{
        //    var emp_no = 10001;

        //    Parallel.For(0, 100, i =>
        //    {
        //        var manager = fixture.employeesDb.Query().dept_manager.Single(x => x.dept_no == "d005" && x.emp_no == emp_no + i);

        //        Assert.NotNull(manager.departments);
        //        Assert.Equal("d005", manager.departments.dept_no);
        //    });
        //}

        [Fact]
        public void LazyLoadList()
        {
            Parallel.For(0, 100, i =>
            {
                var department = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d005");

                Assert.NotNull(department.dept_manager);
                Assert.NotEmpty(department.dept_manager);
                Assert.True(10 < department.dept_manager.Count());
                Assert.Equal("d005", department.dept_manager.First().departments.dept_no);
            });
        }
    }
}