using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

public partial interface Idept
{
}

[Table("dept-emp")]
[Interface<Idept>]
public abstract partial class Dept_emp(RowData rowData, DataSourceAccess dataSource) : Immutable<Dept_emp, EmployeesDb>(rowData, dataSource), ITableModel<EmployeesDb>
{
    [PrimaryKey]
    [ForeignKey("departments", "dept_no", "dept_emp_ibfk_2")]
    [Type(DatabaseType.MySQL, "char", 4)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_no")]
    public abstract string dept_no { get; }

    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "dept_emp_ibfk_1")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    public abstract int emp_no { get; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("from_date")]
    public abstract DateOnly from_date { get; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("to_date")]
    public abstract DateOnly to_date { get; }

    [Relation("departments", "dept_no", "dept_emp_ibfk_2")]
    public abstract Department departments { get; }

    [Relation("employees", "emp_no", "dept_emp_ibfk_1")]
    public abstract Employee employees { get; }

}