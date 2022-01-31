using System;
using System.Linq;
using DataLinq;
using DataLinq.Exceptions;
using DataLinq.Extensions;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Tests;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests
{
    public class TransactionTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture fixture;
        private Helpers helpers;

        public TransactionTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
            this.helpers = new Helpers(fixture);
        }

        [Fact]
        public void Insert()
        {
            var emp_no = 999999;

            foreach (var alreadyExists in fixture.employeesDb.Query().employees.Where(x => x.emp_no == emp_no))
                fixture.employeesDb.Delete(alreadyExists);

            var employee = helpers.NewEmployee(emp_no);
            Assert.True(employee.HasPrimaryKeysSet());

            using var transaction = fixture.employeesDb.Transaction();
            Assert.Equal(DatabaseTransactionStatus.Closed, transaction.Status);

            transaction.Insert(employee);
            Assert.True(employee.HasPrimaryKeysSet());
            var dbTransactionEmployee = transaction.Query().employees.Single(x => x.emp_no == emp_no);
            Assert.Equal(employee.birth_date.ToShortDateString(), dbTransactionEmployee.birth_date.ToShortDateString());

            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "employees");

            //Assert.Equal(1, table.Cache.TransactionRowsCount);
            Assert.Equal(DatabaseTransactionStatus.Open, transaction.Status);

            transaction.Commit();
            //Assert.Equal(0, table.Cache.TransactionRowsCount);
            Assert.Equal(DatabaseTransactionStatus.Committed, transaction.Status);

            var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == emp_no);

            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        }

        [Fact]
        public void InsertAutoIncrement()
        {
            var employee = helpers.NewEmployee();
            Assert.False(employee.HasPrimaryKeysSet());

            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Insert(employee);
                Assert.NotNull(employee.emp_no);
                Assert.True(employee.HasPrimaryKeysSet());

                var dbTransactionEmployee = transaction.Query().employees.Single(x => x.emp_no == employee.emp_no);
                Assert.Equal(employee.birth_date.ToShortDateString(), dbTransactionEmployee.birth_date.ToShortDateString());
                Assert.True(dbTransactionEmployee.HasPrimaryKeysSet());

                transaction.Commit();
            }

            var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == employee.emp_no);

            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            Assert.True(dbEmployee.HasPrimaryKeysSet());

            fixture.employeesDb.Delete(dbEmployee);
            Assert.False(fixture.employeesDb.Query().employees.Any(x => x.emp_no == employee.emp_no));
        }

        [Fact]
        public void InsertAndUpdateAutoIncrement()
        {
            var employee = helpers.NewEmployee();
            Assert.False(employee.HasPrimaryKeysSet());

            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Insert(employee);
                Assert.NotNull(employee.emp_no);
                Assert.True(employee.HasPrimaryKeysSet());

                transaction.Commit();
            }

            Assert.True(employee.HasPrimaryKeysSet());
            employee.birth_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));


            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Update(employee);
                Assert.True(employee.HasPrimaryKeysSet());
                transaction.Commit();
            }

            var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == employee.emp_no);
            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            Assert.True(dbEmployee.HasPrimaryKeysSet());

            fixture.employeesDb.Delete(dbEmployee);
            Assert.False(fixture.employeesDb.Query().employees.Any(x => x.emp_no == employee.emp_no));
        }

        [Fact]
        public void UpdateImplicitTransaction()
        {
            var emp_no = 999998;

            var employee = helpers.GetEmployee(emp_no);
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            var dbEmployeeReturn = fixture.employeesDb.Update(employeeMut);
            var dbEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == emp_no);

            Assert.NotSame(dbEmployeeReturn, dbEmployee);
            Assert.NotEqual(orgBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            Assert.Equal(employeeMut.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        }


        [Fact]
        public void UpdateExplicitTransaction()
        {
            var emp_no = 999997;

            var employee = helpers.GetEmployee(emp_no);
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            using var transaction = fixture.employeesDb.Transaction();
            var dbEmployeeReturn = transaction.Update(employeeMut);
            var dbEmployee = transaction.Query().employees.Single(x => x.emp_no == emp_no);
            Assert.Same(dbEmployeeReturn, dbEmployee);
            transaction.Commit();

            var dbEmployee2 = fixture.employeesDb.Query().employees.Single(x => x.emp_no == emp_no);

            Assert.NotSame(dbEmployeeReturn, dbEmployee2);
            Assert.NotEqual(orgBirthDate.ToShortDateString(), dbEmployee2.birth_date.ToShortDateString());
            Assert.Equal(employeeMut.birth_date.ToShortDateString(), dbEmployee2.birth_date.ToShortDateString());
        }

        [Fact]
        public void RollbackTransaction()
        {
            var emp_no = 999996;

            var employee = helpers.GetEmployee(emp_no);
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            using var transaction = fixture.employeesDb.Transaction();
            var dbEmployeeReturn = transaction.Update(employeeMut);
            var dbEmployee = transaction.Query().employees.Single(x => x.emp_no == emp_no);
            Assert.Same(dbEmployeeReturn, dbEmployee);
            
            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "employees");
            //Assert.Equal(1, table.Cache.TransactionRowsCount);
            Assert.Equal(DatabaseTransactionStatus.Open, transaction.Status);

            transaction.Rollback();
            //Assert.Equal(0, table.Cache.TransactionRowsCount);
            Assert.Equal(DatabaseTransactionStatus.RolledBack, transaction.Status);

            var dbEmployee2 = fixture.employeesDb.Query().employees.Single(x => x.emp_no == emp_no);

            Assert.NotSame(dbEmployeeReturn, dbEmployee2);
            Assert.NotEqual(employeeMut.birth_date.ToShortDateString(), dbEmployee2.birth_date.ToShortDateString());
            Assert.Equal(orgBirthDate.ToShortDateString(), dbEmployee2.birth_date.ToShortDateString());
        }

        [Fact]
        public void DoubleCommitTransaction()
        {
            var emp_no = 999995;

            var employee = helpers.GetEmployee(emp_no);
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            using var transaction = fixture.employeesDb.Transaction();
            var dbEmployeeReturn = transaction.Update(employeeMut);

            transaction.Commit();
            Assert.Throws<Exception>(() => transaction.Commit());
            Assert.Throws<Exception>(() => transaction.Rollback());
        }

        [Fact]
        public void DoubleRollbackTransaction()
        {
            var emp_no = 999994;

            var employee = helpers.GetEmployee(emp_no);
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            using var transaction = fixture.employeesDb.Transaction();
            var dbEmployeeReturn = transaction.Update(employeeMut);

            transaction.Rollback();
            Assert.Throws<Exception>(() => transaction.Rollback());
            Assert.Throws<Exception>(() => transaction.Commit());
        }

        [Fact]
        public void CommitRollbackTransaction()
        {
            var emp_no = 999993;

            var employee = helpers.GetEmployee(emp_no);
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            using var transaction = fixture.employeesDb.Transaction();
            var dbEmployeeReturn = transaction.Update(employeeMut);

            transaction.Commit();
            Assert.Throws<Exception>(() => transaction.Rollback());
            Assert.Throws<Exception>(() => transaction.Commit());
        }

        [Fact]
        public void RollbackCommitTransaction()
        {
            var emp_no = 999992;

            var employee = helpers.GetEmployee(emp_no);
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            using var transaction = fixture.employeesDb.Transaction();
            var dbEmployeeReturn = transaction.Update(employeeMut);

            transaction.Rollback();
            Assert.Throws<Exception>(() => transaction.Commit());
            Assert.Throws<Exception>(() => transaction.Rollback());
        }

        [Fact]
        public void TransactionCache()
        {
            var emp_no = 999991;
            var employee = helpers.GetEmployee(emp_no);
            Transaction<employeesDb>[] transactions = new Transaction<employeesDb>[10];

            for (int i = 0; i < 10; i++)
            {
                transactions[i] = fixture.employeesDb.Transaction(TransactionType.ReadOnly);
                var dbEmployee = transactions[i].Query().employees.Single(x => x.emp_no == emp_no);
                var dbEmployee2 = transactions[i].Query().employees.Single(x => x.emp_no == emp_no);
                Assert.Same(dbEmployee, dbEmployee2);

                if (i > 0)
                {
                    var dbEmployeePrev = transactions[i - 1].Query().employees.Single(x => x.emp_no == emp_no);
                    Assert.NotSame(dbEmployee, dbEmployeePrev);
                }
            }

            foreach (var transaction in transactions)
                transaction.Dispose();
        }

        //[Fact]
        //public void InsertUpdateTwice()
        //{
        //    var employee = NewEmployee();

        //    using (var transaction = fixture.employeesDb.Transaction())
        //    {
        //        transaction.Insert(employee);
        //        transaction.Commit();
        //    }

        //    employee.birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        //    using (var transaction = fixture.employeesDb.Transaction())
        //    {
        //        Assert.Throws<InvalidMutationObjectException>(() => transaction.Insert(employee));
        //    }
        //}

        //[Fact]
        //public void UpdateTwice()
        //{
        //    var emp_no = 999996;

        //    var employeeMut = GetEmployee(emp_no).Mutate();

        //    employeeMut.birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)); ;
        //    fixture.employeesDb.Update(employeeMut);

        //    employeeMut.birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)); ;
        //    Assert.Throws<InvalidMutationObjectException>(() => fixture.employeesDb.Update(employeeMut));
        //}

       
    }
}