using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

//public static class EmployeeExtensions
//{
    //public static Employee Update<T>(this Database<T> database, Employee model, Action<MutableEmployee> changes) where T : class, IDatabaseModel =>
    //    database.Commit(transaction => model.Update(changes, transaction));

    //public static Employee InsertOrUpdate<T>(this Database<T> database, Employee model, Action<MutableEmployee> changes) where T : class, IDatabaseModel =>
    //    database.Commit(transaction => model.InsertOrUpdate(changes, transaction));

    //public static Employee Update(this Transaction transaction, Employee model, Action<MutableEmployee> changes) =>
    //    model.Update(changes, transaction);

    //public static Employee InsertOrUpdate(this Transaction transaction, Employee model, Action<MutableEmployee> changes) =>
    //    model.InsertOrUpdate(changes, transaction);



    //{
    //    return Commit(transaction => transaction.Update(model, changes), transactionType);
    //}

    //public M Update<M>(M model, Action<Mutable<M>> changes, TransactionType transactionType = TransactionType.ReadAndWrite) where M : ImmutableInstanceBase
    //{
    //    return Commit(transaction => transaction.Update(model, changes), transactionType);
    //}

    //    public static MutableEmployee Mutate(this Employee model) => new(model);

    //    public static Employee Update(this Employee model, Action<MutableEmployee> changes, Transaction transaction)
    //    {
    //        var mutable = new MutableEmployee(model);
    //        changes(mutable);

    //        return transaction.Update(mutable);
    //    }

    //    public static Employee InsertOrUpdate(this Employee model, Action<MutableEmployee> changes, Transaction transaction)
    //    {
    //        var mutable = model == null
    //            ? new MutableEmployee()
    //            : new MutableEmployee(model);

    //        changes(mutable);

    //        return transaction.InsertOrUpdate(mutable);
    //    }
//}

[Table("employees")]
public abstract partial class Employee(RowData RowData, DataSourceAccess DataSource) : Immutable<Employee>(RowData, DataSource), ITableModel<EmployeesDb>
{
    public enum Employeegender
    {
        M = 1,
        F = 2,
    }
    
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    public abstract int? emp_no { get; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("birth_date")]
    public abstract DateOnly birth_date { get; }

    [Type(DatabaseType.MySQL, "varchar", 14)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("first_name")]
    public abstract string first_name { get; }

    [Type(DatabaseType.MySQL, "enum")]
    [Type(DatabaseType.SQLite, "integer")]
    [Enum("M", "F")]
    [Column("gender")]
    public abstract Employeegender? gender { get; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("hire_date")]
    public abstract DateOnly hire_date { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bit", 1)]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("IsDeleted")]
    public abstract bool? IsDeleted { get; }

    [Type(DatabaseType.MySQL, "varchar", 16)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("last_name")]
    public abstract string last_name { get; }

    [Relation("dept-emp", "emp_no", "dept_emp_ibfk_1")]
    public abstract IEnumerable<Dept_emp> dept_emp { get; }

    [Relation("dept_manager", "emp_no", "dept_manager_ibfk_1")]
    public abstract IEnumerable<Manager> dept_manager { get; }

    [Relation("salaries", "emp_no", "salaries_ibfk_1")]
    public abstract IEnumerable<Salaries> salaries { get; }

    [Relation("titles", "emp_no", "titles_ibfk_1")]
    public abstract IEnumerable<Titles> titles { get; }

}