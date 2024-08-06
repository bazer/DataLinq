using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Table("salaries")]
public interface ISalaries : ITableModel<IEmployees>
{
    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "salaries_ibfk_1")]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    int emp_no { get; set; }

    [PrimaryKey]
    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("from_date")]
    DateOnly FromDate { get; set; }

    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("salary")]
    int salary { get; set; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("to_date")]
    DateOnly ToDate { get; set; }

    [Relation("employees", "emp_no", "salaries_ibfk_1")]
    IEmployee employees { get; }

}