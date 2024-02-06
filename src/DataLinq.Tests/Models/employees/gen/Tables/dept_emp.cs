using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Table("dept_emp")]
public partial record dept_emp : ITableModel<Employees>
{
    [PrimaryKey]
    [ForeignKey("departments", "dept_no", "dept_emp_ibfk_2")]
    [Type(DatabaseType.MySQL, "char", 4)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_no")]
    public virtual string dept_no { get; set; }

    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "dept_emp_ibfk_1")]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    public virtual int emp_no { get; set; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("from_date")]
    public virtual DateOnly from_date { get; set; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("to_date")]
    public virtual DateOnly to_date { get; set; }

    [Relation("departments", "dept_no", "dept_emp_ibfk_2")]
    public virtual Department departments { get; }

    [Relation("employees", "emp_no", "dept_emp_ibfk_1")]
    public virtual Employee employees { get; }

}