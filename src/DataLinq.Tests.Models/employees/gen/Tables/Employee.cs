using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

[GenerateInterface]
[Table("employees")]
public abstract partial class Employee(RowData rowData, DataSourceAccess dataSource) : Immutable<Employee, EmployeesDb>(rowData, dataSource), ITableModel<EmployeesDb>
{
    public enum Employeegender
    {
        M = 1,
        F = 2,
    }
    
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 11)]
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
    public abstract Employeegender gender { get; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("hire_date")]
    public abstract DateOnly hire_date { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bit", 1, 0)]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("IsDeleted")]
    public abstract bool? IsDeleted { get; }

    [Type(DatabaseType.MySQL, "varchar", 16)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("last_name")]
    public abstract string last_name { get; }

    [Relation("dept-emp", "emp_no", "dept_emp_ibfk_1")]
    public abstract IImmutableRelation<Dept_emp> dept_emp { get; }

    [Relation("dept_manager", "emp_no", "dept_manager_ibfk_1")]
    public abstract IImmutableRelation<Manager> dept_manager { get; }

    [Relation("salaries", "emp_no", "salaries_ibfk_1")]
    public abstract IImmutableRelation<Salaries> salaries { get; }

    [Relation("titles", "emp_no", "titles_ibfk_1")]
    public abstract IImmutableRelation<Titles> titles { get; }

}