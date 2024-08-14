﻿using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

[Table("salaries")]
public abstract partial class Salaries(RowData RowData, DataSourceAccess DataSource) : Immutable<Salaries>(RowData, DataSource), ITableModel<EmployeesDb>
{
    [PrimaryKey]
    [ForeignKey("employees", "emp_no", "salaries_ibfk_1")]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    public abstract int emp_no { get; }

    [PrimaryKey]
    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("from_date")]
    public abstract DateOnly FromDate { get; }

    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("salary")]
    public abstract int salary { get; }

    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("to_date")]
    public abstract DateOnly ToDate { get; }

    [Relation("employees", "emp_no", "salaries_ibfk_1")]
    public abstract Employee employees { get; }

}