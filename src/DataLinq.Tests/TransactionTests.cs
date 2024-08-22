using System;
using System.Data;
using System.Linq;
using DataLinq.Mutation;
using DataLinq.Tests.Models;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Xunit;

namespace DataLinq.Tests;

public class TransactionTests : BaseTests
{
    private Helpers helpers = new Helpers();

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void AttachTransaction(Database<EmployeesDb> employeesDb)
    {
        using IDbConnection dbConnection = employeesDb.DatabaseType == DatabaseType.MySQL
            ? new MySqlConnection(employeesDb.Provider.ConnectionString)
            : new SqliteConnection(employeesDb.Provider.ConnectionString);

        dbConnection.Open();
        using var dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        var command = employeesDb
            .From("departments")
            .Set("dept_no", "d099")
            .Set("dept_name", "Transactions")
            .InsertQuery()
            .ToDbCommand();

        command.Connection = dbConnection;
        command.Transaction = dbTransaction;
        command.ExecuteNonQuery();

        using var transaction = employeesDb.AttachTransaction(dbTransaction);
        Assert.Equal(DatabaseTransactionStatus.Open, transaction.Status);

        var dept = transaction.Query().Departments.Single(x => x.DeptNo == "d099");
        Assert.Equal("Transactions", dept.Name);

        var numDept = employeesDb.Query().Departments.Count(x => x.DeptNo == "d099");
        Assert.Equal(0, numDept);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void AttachMutateTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999700;

        foreach (var alreadyExists in employeesDb.Query().Employees.Where(x => x.emp_no == emp_no).ToList())
            employeesDb.Delete(alreadyExists);

        var employee = employeesDb.Query().Employees.SingleOrDefault(x => x.emp_no == emp_no)?.Mutate() ?? helpers.NewEmployee(emp_no);
        employee.first_name = "Bob";
        employeesDb.InsertOrUpdate(employee);

        var dbEmployee = employeesDb.Query().Employees.SingleOrDefault(x => x.emp_no == emp_no).Mutate();
        Assert.Equal("Bob", dbEmployee.first_name);


        using IDbConnection dbConnection = employeesDb.DatabaseType == DatabaseType.MySQL
            ? new MySqlConnection(employeesDb.Provider.ConnectionString)
            : new SqliteConnection(employeesDb.Provider.ConnectionString);

        dbConnection.Open();
        using var dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var transaction = employeesDb.AttachTransaction(dbTransaction);

        dbEmployee.first_name = "Rick";
        transaction.InsertOrUpdate(dbEmployee);
        dbTransaction.Commit();
        transaction.Commit();

        var dbEmployee2 = employeesDb.Query().Employees.SingleOrDefault(x => x.emp_no == emp_no);
        Assert.Equal("Rick", dbEmployee2.first_name);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void GetTransaction(Database<EmployeesDb> employeesDb)
    {
        using var transaction = employeesDb.Transaction();

        transaction.Insert(new MutableDepartment
        {
            DeptNo = "d099",
            Name = "Transactions"
        });

        var command = employeesDb
            .From("departments")
            .Where("dept_no")
            .EqualTo("d099")
            .SelectQuery()
            .ToDbCommand();

        var dbTransaction = transaction.DatabaseAccess.DbTransaction;

        command.Connection = dbTransaction.Connection;
        command.Transaction = dbTransaction;
        using var reader = command.ExecuteReader();

        var rows = 0;
        while (reader.Read())
        {
            rows++;
            Assert.Equal("d099", reader.GetString(0));
            Assert.Equal("Transactions", reader.GetString(1));
        }

        Assert.Equal(1, rows);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Insert(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999999;

        foreach (var alreadyExists in employeesDb.Query().Employees.Where(x => x.emp_no == emp_no).ToList())
            employeesDb.Delete(alreadyExists);

        var employee = helpers.NewEmployee(emp_no);
        Assert.True(employee.HasPrimaryKeysSet());

        using var transaction = employeesDb.Transaction();
        Assert.Equal(DatabaseTransactionStatus.Closed, transaction.Status);

        transaction.OnStatusChanged += (x, args) =>
        {
            Assert.Same(transaction, x);
            Assert.Same(transaction, args.Transaction);
            Assert.Equal(transaction.Status, args.Status);
        };

        transaction.Insert(employee);
        Assert.True(employee.HasPrimaryKeysSet());
        var dbTransactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
        Assert.NotSame(employee, dbTransactionEmployee);
        Assert.Equal(employee.birth_date, dbTransactionEmployee.birth_date);

        var table = employeesDb.Provider.Metadata
                .TableModels.Single(x => x.Table.DbName == "employees").Table;

        var cache = employeesDb.Provider.State.Cache.TableCaches[table];
        Assert.True(cache.IsTransactionInCache(transaction));
        Assert.Single(cache.GetTransactionRows(transaction));
        Assert.Same(dbTransactionEmployee, cache.GetTransactionRows(transaction).First());
        Assert.Equal(DatabaseTransactionStatus.Open, transaction.Status);

        transaction.Commit();
        Assert.False(cache.IsTransactionInCache(transaction));
        Assert.Equal(DatabaseTransactionStatus.Committed, transaction.Status);

        var dbEmployee = employeesDb.Query().Employees.Single(x => x.emp_no == emp_no);

        Assert.Equal(employee.birth_date, dbEmployee.birth_date);
        Assert.Equal(dbTransactionEmployee, dbEmployee);
        Assert.NotSame(dbTransactionEmployee, dbEmployee);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void InsertAutoIncrement(Database<EmployeesDb> employeesDb)
    {
        var employee = helpers.NewEmployee();
        Assert.False(employee.HasPrimaryKeysSet());

        using (var transaction = employeesDb.Transaction())
        {
            transaction.Insert(employee);
            Assert.NotNull(employee.emp_no);
            Assert.True(employee.HasPrimaryKeysSet());

            var dbTransactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employee.emp_no);
            Assert.Equal(employee.birth_date.ToShortDateString(), dbTransactionEmployee.birth_date.ToShortDateString());
            Assert.True(dbTransactionEmployee.HasPrimaryKeysSet());

            transaction.Commit();
        }

        var dbEmployee = employeesDb.Query().Employees.Single(x => x.emp_no == employee.emp_no);

        Assert.Equal(employee.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        Assert.True(dbEmployee.HasPrimaryKeysSet());

        employeesDb.Delete(dbEmployee);
        Assert.False(employeesDb.Query().Employees.Any(x => x.emp_no == employee.emp_no));
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void InsertAndUpdateAutoIncrement(Database<EmployeesDb> employeesDb)
    {
        var employee = helpers.NewEmployee();
        Assert.False(employee.HasPrimaryKeysSet());

        MutableEmployee dbEmployee;
        using (var transaction = employeesDb.Transaction())
        {
            dbEmployee = transaction.Insert(employee).Mutate();
            Assert.NotNull(employee.emp_no);
            Assert.True(employee.HasPrimaryKeysSet());

            transaction.Commit();
        }

        Assert.True(employee.HasPrimaryKeysSet());
        dbEmployee.birth_date = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));


        using (var transaction = employeesDb.Transaction())
        {
            transaction.Update(dbEmployee);
            Assert.True(dbEmployee.HasPrimaryKeysSet());
            transaction.Commit();
        }

        var dbEmployee2 = employeesDb.Query().Employees.Single(x => x.emp_no == employee.emp_no);
        Assert.Equal(dbEmployee.birth_date, dbEmployee2.birth_date);
        Assert.True(dbEmployee2.HasPrimaryKeysSet());

        employeesDb.Delete(dbEmployee2);
        Assert.False(employeesDb.Query().Employees.Any(x => x.emp_no == employee.emp_no));
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void UpdateImplicitTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999998;

        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var orgBirthDate = employee.birth_date;
        var employeeMut = employee.Mutate();
        Assert.False(employeeMut.IsNewModel());

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        employeeMut.birth_date = newBirthDate;
        Assert.Equal(newBirthDate, employeeMut.birth_date);

        var dbEmployeeReturn = employeesDb.Update(employeeMut);
        var dbEmployee = employeesDb.Query().Employees.Single(x => x.emp_no == emp_no);

        Assert.NotSame(dbEmployeeReturn, dbEmployee);
        Assert.NotEqual(orgBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
        Assert.Equal(employeeMut.birth_date.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void UpdateExplicitTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999997;

        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var orgBirthDate = employee.birth_date;
        var employeeMut = employee.Mutate();

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        employeeMut.birth_date = newBirthDate;
        Assert.Equal(newBirthDate, employeeMut.birth_date);

        using var transaction = employeesDb.Transaction();
        var dbEmployeeReturn = transaction.Update(employeeMut);
        var dbEmployee = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
        Assert.Same(dbEmployeeReturn, dbEmployee);
        transaction.Commit();

        var dbEmployee2 = employeesDb.Query().Employees.Single(x => x.emp_no == emp_no);

        Assert.NotSame(dbEmployeeReturn, dbEmployee2);
        Assert.NotEqual(orgBirthDate.ToShortDateString(), dbEmployee2.birth_date.ToShortDateString());
        Assert.Equal(employeeMut.birth_date.ToShortDateString(), dbEmployee2.birth_date.ToShortDateString());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void RollbackTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999996;

        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var orgBirthDate = employee.birth_date;
        var employeeMut = employee.Mutate();

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        employeeMut.birth_date = newBirthDate;
        Assert.Equal(newBirthDate, employeeMut.birth_date);

        using var transaction = employeesDb.Transaction();
        var dbEmployeeReturn = transaction.Update(employeeMut);
        var dbEmployee = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
        Assert.Same(dbEmployeeReturn, dbEmployee);

        var table = employeesDb.Provider.Metadata
                .TableModels.Single(x => x.Table.DbName == "employees").Table;
        //Assert.Equal(1, table.Cache.TransactionRowsCount);
        Assert.Equal(DatabaseTransactionStatus.Open, transaction.Status);

        transaction.Rollback();
        //Assert.Equal(0, table.Cache.TransactionRowsCount);
        Assert.Equal(DatabaseTransactionStatus.RolledBack, transaction.Status);

        var dbEmployee2 = employeesDb.Query().Employees.Single(x => x.emp_no == emp_no);

        Assert.NotSame(dbEmployeeReturn, dbEmployee2);
        Assert.NotEqual(employeeMut.birth_date.ToShortDateString(), dbEmployee2.birth_date.ToShortDateString());
        Assert.Equal(orgBirthDate.ToShortDateString(), dbEmployee2.birth_date.ToShortDateString());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void DoubleCommitTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999995;

        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var orgBirthDate = employee.birth_date;
        var employeeMut = employee.Mutate();

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        employeeMut.birth_date = newBirthDate;
        Assert.Equal(newBirthDate, employeeMut.birth_date);

        using var transaction = employeesDb.Transaction();
        var dbEmployeeReturn = transaction.Update(employeeMut);

        transaction.Commit();
        Assert.Throws<Exception>(() => transaction.Commit());
        Assert.Throws<Exception>(() => transaction.Rollback());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void DoubleRollbackTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999994;

        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var orgBirthDate = employee.birth_date;
        var employeeMut = employee.Mutate();

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        employeeMut.birth_date = newBirthDate;
        Assert.Equal(newBirthDate, employeeMut.birth_date);

        using var transaction = employeesDb.Transaction();
        var dbEmployeeReturn = transaction.Update(employeeMut);

        transaction.Rollback();
        Assert.Throws<Exception>(() => transaction.Rollback());
        Assert.Throws<Exception>(() => transaction.Commit());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void CommitRollbackTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999993;

        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var orgBirthDate = employee.birth_date;
        var employeeMut = employee.Mutate();

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        employeeMut.birth_date = newBirthDate;
        Assert.Equal(newBirthDate, employeeMut.birth_date);

        using var transaction = employeesDb.Transaction();
        var dbEmployeeReturn = transaction.Update(employeeMut);

        transaction.Commit();
        Assert.Throws<Exception>(() => transaction.Rollback());
        Assert.Throws<Exception>(() => transaction.Commit());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void RollbackCommitTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999992;

        var employee = helpers.GetEmployee(emp_no, employeesDb);
        var orgBirthDate = employee.birth_date;
        var employeeMut = employee.Mutate();

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        employeeMut.birth_date = newBirthDate;
        Assert.Equal(newBirthDate, employeeMut.birth_date);

        using var transaction = employeesDb.Transaction();
        var dbEmployeeReturn = transaction.Update(employeeMut);

        transaction.Rollback();
        Assert.Throws<Exception>(() => transaction.Commit());
        Assert.Throws<Exception>(() => transaction.Rollback());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TransactionCache(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999991;
        var employee = helpers.GetEmployee(emp_no, employeesDb);
        Transaction<EmployeesDb>[] transactions = new Transaction<EmployeesDb>[10];

        for (int i = 0; i < 10; i++)
        {
            transactions[i] = employeesDb.Transaction(TransactionType.ReadAndWrite);
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

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void InsertOrUpdate(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999800;
        var employee = helpers.GetEmployee(emp_no, employeesDb).Mutate();

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        var dbEmployee = employee.InsertOrUpdate(x => { x.birth_date = newBirthDate; }, employeesDb);
        Assert.Equal(emp_no, dbEmployee.emp_no);
        Assert.Equal(newBirthDate.ToShortDateString(), dbEmployee.birth_date.ToShortDateString());
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void InsertRelations(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999799;
        var employee = helpers.GetEmployee(emp_no, employeesDb);

        foreach (var salary in employee.salaries)
            employeesDb.Delete(salary);

        using (var transaction = employeesDb.Transaction())
        {
            Assert.Empty(employee.salaries);

            var newSalary = new MutableSalaries
            {
                emp_no = employee.emp_no.Value,
                salary = 50000,
                FromDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                ToDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
            };

            Assert.Empty(employee.salaries);
            transaction.Insert(newSalary);
            Assert.Empty(employee.salaries);
            transaction.Commit();
        }

        Assert.Single(employee.salaries);
        employeesDb.Delete(employee.salaries.First());
        Assert.Empty(employee.salaries);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void InsertRelationsInTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999798;
        var employee = helpers.GetEmployee(emp_no, employeesDb);

        foreach (var salary in employee.salaries)
            employeesDb.Delete(salary);

        using (var transaction = employeesDb.Transaction())
        {
            var employeeDb = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
            Assert.Empty(employeeDb.salaries);

            var newSalary = new MutableSalaries
            {
                emp_no = employeeDb.emp_no.Value,
                salary = 50000,
                FromDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                ToDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
            };

            //Assert.Null(newSalary.employees);
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
        employeesDb.Delete(employee.salaries.First());
        Assert.Empty(employee.salaries);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void InsertRelationsReadAfterTransaction(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999797;
        var employee = helpers.GetEmployee(emp_no, employeesDb);

        foreach (var s in employee.salaries)
            employeesDb.Delete(s);

        Salaries salary = null;
        Employee employeeDb = null;

        var table = employeesDb.Provider.Metadata
                .TableModels.Single(x => x.Table.DbName == "salaries").Table;

        var cache = employeesDb.Provider.State.Cache.TableCaches[table];

        using var transaction = employeesDb.Transaction();

        Assert.False(cache.IsTransactionInCache(transaction));
        Assert.Empty(cache.GetTransactionRows(transaction));
        employeeDb = transaction.Query().Employees.Single(x => x.emp_no == emp_no);
        Assert.Empty(employeeDb.salaries);

        var newSalary = new MutableSalaries
        {
            emp_no = employeeDb.emp_no.Value,
            salary = 50000,
            FromDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
            ToDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
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
        employeesDb.Delete(employee.salaries.First());
        Assert.Empty(employee.salaries);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void UpdateOldModel(Database<EmployeesDb> employeesDb)
    {
        var emp_no = 999796;
        var employee = helpers.GetEmployee(emp_no, employeesDb).Mutate();

        var newBirthDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        var dbEmployee = employee.InsertOrUpdate(x => { x.birth_date = newBirthDate; }, employeesDb);
        Assert.Equal(emp_no, dbEmployee.emp_no);
        Assert.Equal(newBirthDate, dbEmployee.birth_date);

        var newHireDate = helpers.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        var dbEmployee2 = employee.InsertOrUpdate(x => { x.hire_date = newHireDate; }, employeesDb);
        Assert.Equal(emp_no, dbEmployee2.emp_no);
        Assert.Equal(newBirthDate, dbEmployee2.birth_date);
        Assert.Equal(newHireDate, dbEmployee2.hire_date);
    }



    //[Fact]
    //public void InsertUpdateTwice()
    //{
    //    var employee = NewEmployee();

    //    using (var transaction = employeesDb.Transaction())
    //    {
    //        transaction.Insert(employee);
    //        transaction.Commit();
    //    }

    //    employee.birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

    //    using (var transaction = employeesDb.Transaction())
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
    //    employeesDb.Update(employeeMut);

    //    employeeMut.birth_date = RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)); ;
    //    Assert.Throws<InvalidMutationObjectException>(() => employeesDb.Update(employeeMut));
    //}


}