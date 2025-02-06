using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

[Definition("select `l`.`emp_no` AS `emp_no`,`d`.`dept_no` AS `dept_no`,`l`.`from_date` AS `from_date`,`l`.`to_date` AS `to_date` from (`dept-emp` `d` join `dept_emp_latest_date` `l` on(`d`.`emp_no` = `l`.`emp_no` and `d`.`from_date` = `l`.`from_date` and `l`.`to_date` = `d`.`to_date`))")]
[View("current_dept_emp")]
public abstract partial class current_dept_emp(RowData rowData, DataSourceAccess dataSource) : Immutable<current_dept_emp, EmployeesDb>(rowData, dataSource), IViewModel<EmployeesDb>
{
    [Type(DatabaseType.MySQL, "char", 4)]
    [Column("dept_no")]
    public abstract string dept_no { get; }

    [Type(DatabaseType.MySQL, "int", 11)]
    [Column("emp_no")]
    public abstract int emp_no { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("from_date")]
    public abstract DateOnly? from_date { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "date")]
    [Column("to_date")]
    public abstract DateOnly? to_date { get; }

}