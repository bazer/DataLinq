using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

[Table("titles")]
public abstract partial class Titles(RowData rowData, DataSourceAccess dataSource) : Immutable<Titles, EmployeesDb>(rowData, dataSource), ITableModel<EmployeesDb>
{
    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "titles_ibfk_1")]
    [Type(DatabaseType.MySQL, "int", 0)]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    public abstract int emp_no { get; }

    [PrimaryKey]
    [Type(DatabaseType.MySQL, "date", 0)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("from_date")]
    public abstract DateOnly from_date { get; }

    [PrimaryKey]
    [Type(DatabaseType.MySQL, "varchar", 50)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("title")]
    public abstract string title { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date", 0)]
    [Type(DatabaseType.SQLite, "text")]
    [Column("to_date")]
    public abstract DateOnly? to_date { get; }

    [Relation("employees", "emp_no", "titles_ibfk_1")]
    public abstract Employee employees { get; }

}