using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Models.Employees;

public partial interface Idept_emp_latest_date
{
}

[Definition("select `dept-emp`.`emp_no` AS `emp_no`,max(`dept-emp`.`from_date`) AS `from_date`,max(`dept-emp`.`to_date`) AS `to_date` from `dept-emp` group by `dept-emp`.`emp_no`")]
[View("dept_emp_latest_date")]
[Interface<Idept_emp_latest_date>]
public abstract partial class dept_emp_latest_date(IRowData rowData, IDataSourceAccess dataSource) : Immutable<dept_emp_latest_date, EmployeesDb>(rowData, dataSource), IViewModel<EmployeesDb>
{
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