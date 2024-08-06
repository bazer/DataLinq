using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

public enum ManagerType
{
    Unknown,
    Manager,
    AssistantManager,
    FestiveManager
}


[Table("dept_manager")]
public interface IManager : ITableModel<IEmployees>
{
    [PrimaryKey]
    [ForeignKey("departments", "dept_no", "dept_manager_ibfk_2")]
    [Type(DatabaseType.MySQL, "char", 4)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("dept_fk")]
    string dept_fk { get; set; }

    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "dept_manager_ibfk_1")]
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

    [Type(DatabaseType.MySQL, "tinyint", false)]
    [Column("type")]
    ManagerType Type { get; set; }

    [Relation("departments", "dept_no", "dept_manager_ibfk_2")]
    IDepartment Department { get; }

    [Relation("employees", "emp_no", "dept_manager_ibfk_1")]
    IEmployee employees { get; }

}