using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models;

[Definition("select `dept-emp`.`emp_no` AS `emp_no`,max(`dept-emp`.`from_date`) AS `from_date`,max(`dept-emp`.`to_date`) AS `to_date` from `dept-emp` group by `dept-emp`.`emp_no`")]
[View("dept_emp_latest_date")]
public interface IDept_emp_latest_date : IViewModel<IEmployees>
{
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.SQLite, "integer")]
    [Column("emp_no")]
    int emp_no { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("from_date")]
    DateOnly? from_date { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Type(DatabaseType.SQLite, "text")]
    [Column("to_date")]
    DateOnly? to_date { get; set; }

}