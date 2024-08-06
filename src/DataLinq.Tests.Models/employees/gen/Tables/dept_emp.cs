using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Table("dept-emp")]
[IndexCache(IndexCacheType.None)]
public interface IDept_emp : ITableModel<IEmployees>
{
    [PrimaryKey]
    [ForeignKey("departments", "dept_no", "dept_emp_ibfk_2")]
    [Type(DatabaseType.MySQL, "char", 4)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_no")]
    string dept_no { get; set; }

    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "dept_emp_ibfk_1")]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    int emp_no { get; set; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("from_date")]
    DateOnly from_date { get; set; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("to_date")]
    DateOnly to_date { get; set; }

    [Relation("departments", "dept_no", "dept_emp_ibfk_2")]
    IDepartment departments { get; }

    [Relation("employees", "emp_no", "dept_emp_ibfk_1")]
    IEmployee employees { get; }

}