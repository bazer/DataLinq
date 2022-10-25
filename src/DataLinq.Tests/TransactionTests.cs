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

            foreach (var alreadyExists in fixture.employeesDb.Query().Employees.Where(x => x.emp_no == emp_no))
                fixture.employeesDb.Delete(alreadyExists);

            var employee = helpers.NewEmployee(emp_no);
            Assert.True(employee.HasPrimaryKeysSet());

            using var transaction = fixture.employeesDb.Transaction();
            Assert.Equal(DatabaseTransactionStatus.Closed, transaction.Status);

            transaction.Insert(employee);
            Assert.True(employee.HasPrimaryKeysSet());
            var dbTransactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
            Assert.NotSame(employee, dbTransactionEmployee);
            Assert.Equal(employee.birth_date, dbTransactionEmployee.birth_date);

            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "employees");

            var cache = fixture.employeesDb.Provider.State.Cache.TableCaches.Single(x => x.Table == table);
            Assert.True(cache.IsTransactionInCache(transaction));
            Assert.Single(cache.GetTransactionRows(transaction));
            Assert.Same(dbTransactionEmployee, cache.GetTransactionRows(transaction).First());
            Assert.Equal(DatabaseTransactionStatus.Open, transaction.Status);

            transaction.Commit();
            Assert.False(cache.IsTransactionInCache(transaction));
            Assert.Equal(DatabaseTransactionStatus.Committed, transaction.Status);

            var dbEmployee = fixture.employeesDb.Query().Employees.Single(x => x.emp_no == emp_no);

            Assert.Equal(employee.birth_date, dbEmployee.birth_date);
            Assert.Equal(dbTransactionEmployee, dbEmployee);
            Assert.NotSame(dbTransactionEmployee, dbEmployee);
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

                var dbTransactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employee.emp_no);
                Assert.Equal(employee.birth_date.ToShortDateString(), dbTransactionEmployee.birth_date.ToShortDateString());
                Assert.True(dbTransactionEmployee.HasPrimaryKeysSet());

                transaction.Commit();
            }

            var dbEmployee = fixture.employeesDb.Query().Employees.Single(x => x.emp_no == employee.emp_no);

            Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
            Assert.True(dbEmployee.HasPrimaryKeysSet());

            fixture.employeesDb.Delete(dbEmployee);
            Assert.False(fixture.employeesDb.Query().Employees.Any(x => x.emp_no == employee.emp_no));
        }

        [Fact]
        public void InsertAndUpdateAutoIncrement()
        {
            var employee = helpers.NewEmployee();
            Assert.False(employee.HasPrimaryKeysSet());

            Employee dbEmployee;
            using (var transaction = fixture.employeesDb.Transaction())
            {
                dbEmployee = transaction.Insert(employee).Mutate();
                Assert.NotNull(employee.emp_no);
                Assert.True(employee.HasPrimaryKeysSet());

                transaction.Commit();
            }

            Assert.True(employee.HasPrimaryKeysSet());
            dbEmployee.birth_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));


            using (var transaction = fixture.employeesDb.Transaction())
            {
                transaction.Update(dbEmployee);
                Assert.True(dbEmployee.HasPrimaryKeysSet());
                transaction.Commit();
            }

            var dbEmployee2 = fixture.employeesDb.Query().Employees.Single(x => x.emp_no == employee.emp_no);
            Assert.Equal(dbEmployee.birth_date, dbEmployee2.birth_date);
            Assert.True(dbEmployee2.HasPrimaryKeysSet());

            fixture.employeesDb.Delete(dbEmployee2);
            Assert.False(fixture.employeesDb.Query().Employees.Any(x => x.emp_no == employee.emp_no));
        }

        [Fact]
        public void UpdateImplicitTransaction()
        {
            var emp_no = 999998;

            var employee = helpers.GetEmployee(emp_no);
            Assert.False(employee.IsNewModel());
            var orgBirthDate = employee.birth_date;
            var employeeMut = employee.Mutate();
            Assert.False(employeeMut.IsNewModel());

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            employeeMut.birth_date = newBirthDate;
            Assert.Equal(newBirthDate, employeeMut.birth_date);

            var dbEmployeeReturn = fixture.employeesDb.Update(employeeMut);
            var dbEmployee = fixture.employeesDb.Query().Employees.Single(x => x.emp_no == emp_no);

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
            var dbEmployee = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
            Assert.Same(dbEmployeeReturn, dbEmployee);
            transaction.Commit();

            var dbEmployee2 = fixture.employeesDb.Query().Employees.Single(x => x.emp_no == emp_no);

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
            var dbEmployee = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
            Assert.Same(dbEmployeeReturn, dbEmployee);

            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "employees");
            //Assert.Equal(1, table.Cache.TransactionRowsCount);
            Assert.Equal(DatabaseTransactionStatus.Open, transaction.Status);

            transaction.Rollback();
            //Assert.Equal(0, table.Cache.TransactionRowsCount);
            Assert.Equal(DatabaseTransactionStatus.RolledBack, transaction.Status);

            var dbEmployee2 = fixture.employeesDb.Query().Employees.Single(x => x.emp_no == emp_no);

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
            Transaction<employees>[] transactions = new Transaction<employees>[10];

            for (int i = 0; i < 10; i++)
            {
                transactions[i] = fixture.employeesDb.Transaction(TransactionType.ReadOnly);
                var dbEmployee = transactions[i].Query().Employees.Single(x => x.emp_no == emp_no);
                var dbEmployee2 = transactions[i].Query().Employees.Single(x => x.emp_no == emp_no);
                Assert.Same(dbEmployee, dbEmployee2);

                if (i > 0)
                {
                    var dbEmployeePrev = transactions[i - 1].Query().Employees.Single(x => x.emp_no == emp_no);
                    Assert.NotSame(dbEmployee, dbEmployeePrev);
                }
            }

            foreach (var transaction in transactions)
                transaction.Dispose();
        }

        [Fact]
        public void InsertOrUpdate()
        {
            var emp_no = 999800;
            var employee = helpers.GetEmployee(emp_no);

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            var dbEmployee = fixture.employeesDb.InsertOrUpdate(employee, x => { x.birth_date = newBirthDate; });
            Assert.Equal(emp_no, dbEmployee.emp_no);
            Assert.Equal(newBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        }


        [Fact]
        public void InsertRelations()
        {
            var emp_no = 999799;
            var employee = helpers.GetEmployee(emp_no);

            foreach (var salary in employee.salaries)
                fixture.employeesDb.Delete(salary);

            using (var transaction = fixture.employeesDb.Transaction())
            {
                Assert.Empty(employee.salaries);

                var newSalary = new salaries
                {
                    emp_no = employee.emp_no.Value,
                    salary = 50000,
                    from_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                    to_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
                };

                Assert.Empty(employee.salaries);
                transaction.Insert(newSalary);
                Assert.Empty(employee.salaries);
                transaction.Commit();
            }

            Assert.Single(employee.salaries);
            fixture.employeesDb.Delete(employee.salaries.First());
            Assert.Empty(employee.salaries);
        }

        [Fact]
        public void InsertRelationsInTransaction()
        {
            var emp_no = 999798;
            var employee = helpers.GetEmployee(emp_no);

            foreach (var salary in employee.salaries)
                fixture.employeesDb.Delete(salary);

            using (var transaction = fixture.employeesDb.Transaction())
            {
                var employeeDb = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
                Assert.Empty(employeeDb.salaries);

                var newSalary = new salaries
                {
                    emp_no = employeeDb.emp_no.Value,
                    salary = 50000,
                    from_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                    to_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
                };

                Assert.Null(newSalary.employees);
                Assert.Empty(employeeDb.salaries);
                var salary = transaction.Insert(newSalary);
                Assert.NotNull(salary);
                Assert.NotNull(salary.employees);
                Assert.Single(employeeDb.salaries);
                Assert.Same(salary, salary.employees.salaries.First());
                Assert.Same(salary, employeeDb.salaries.First().employees.salaries.First());
                Assert.Same(employeeDb, employeeDb.salaries.First().employees);
                Assert.Same(employeeDb, salary.employees.salaries.First().employees);
                transaction.Commit();
            }

            Assert.Single(employee.salaries);
            fixture.employeesDb.Delete(employee.salaries.First());
            Assert.Empty(employee.salaries);
        }

        [Fact]
        public void InsertRelationsReadAfterTransaction()
        {
            var emp_no = 999797;
            var employee = helpers.GetEmployee(emp_no);

            foreach (var s in employee.salaries)
                fixture.employeesDb.Delete(s);

            salaries salary = null;
            Employee employeeDb = null;

            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "salaries");

            var cache = fixture.employeesDb.Provider.State.Cache.TableCaches.Single(x => x.Table == table);

            using var transaction = fixture.employeesDb.Transaction();

            Assert.False(cache.IsTransactionInCache(transaction));
            Assert.Empty(cache.GetTransactionRows(transaction));
            employeeDb = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
            Assert.Empty(employeeDb.salaries);

            var newSalary = new salaries
            {
                emp_no = employeeDb.emp_no.Value,
                salary = 50000,
                from_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                to_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
            };

            //Assert.Empty(employeeDb.salaries);
            salary = transaction.Insert(newSalary);
            Assert.True(cache.IsTransactionInCache(transaction));
            Assert.Single(cache.GetTransactionRows(transaction));
            //Assert.Single(employeeDb.salaries);
            transaction.Commit();
            Assert.Equal(DatabaseTransactionStatus.Committed, transaction.Status);
            Assert.False(cache.IsTransactionInCache(transaction));
            Assert.Empty(cache.GetTransactionRows(transaction));


            Assert.Equal(salary, salary.employees.salaries.First());
            Assert.Equal(salary, employeeDb.salaries.First().employees.salaries.First());
            Assert.Equal(employeeDb, employeeDb.salaries.First().employees);
            Assert.Equal(employeeDb, salary.employees.salaries.First().employees);

            Assert.False(cache.IsTransactionInCache(transaction));
            Assert.Empty(cache.GetTransactionRows(transaction));

            Assert.Single(employee.salaries);
            fixture.employeesDb.Delete(employee.salaries.First());
            Assert.Empty(employee.salaries);
        }

        [Fact]
        public void UpdateOldModel()
        {
            var emp_no = 999796;
            var employee = helpers.GetEmployee(emp_no);

            var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            var dbEmployee = fixture.employeesDb.InsertOrUpdate(employee, x => { x.birth_date = newBirthDate; });
            Assert.Equal(emp_no, dbEmployee.emp_no);
            Assert.Equal(newBirthDate, dbEmployee.birth_date);

            var newHireDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
            var dbEmployee2 = fixture.employeesDb.InsertOrUpdate(employee, x => { x.hire_date = newHireDate; });
            Assert.Equal(emp_no, dbEmployee2.emp_no);
            Assert.Equal(newBirthDate, dbEmployee2.birth_date);
            Assert.Equal(newHireDate, dbEmployee2.hire_date);
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