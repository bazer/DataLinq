using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

[Table("dept_manager")]
public abstract partial class Manager(RowData rowData, DataSourceAccess dataSource) : Immutable<Manager, EmployeesDb>(rowData, dataSource), ITableModel<EmployeesDb>
{
    [PrimaryKey]
    [ForeignKey("departments", "dept_no", "dept_manager_ibfk_2")]
    [Type(DatabaseType.MySQL, "char", 4)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_fk")]
    public abstract string dept_fk { get; }

    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "dept_manager_ibfk_1")]
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

    [Type(DatabaseType.MySQL, "tinyint", 3, false)]
    [Column("type")]
    public abstract ManagerType Type { get; }

    [Relation("departments", "dept_no", "dept_manager_ibfk_2")]
    public abstract Department Department { get; }

    [Relation("employees", "emp_no", "dept_manager_ibfk_1")]
    public abstract Employee employees { get; }

}