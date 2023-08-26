using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models
{
    [Definition("select `dept_emp`.`emp_no` AS `emp_no`,max(`dept_emp`.`from_date`) AS `from_date`,max(`dept_emp`.`to_date`) AS `to_date` from `dept_emp` group by `dept_emp`.`emp_no`")]
    [View("dept_emp_latest_date")]
    public partial record dept_emp_latest_date : IViewModel<Employees>
    {
        [Type(DatabaseType.MySQL, "int")]
        [Type(DatabaseType.SQLite, "integer")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "date")]
        [Type(DatabaseType.SQLite, "text")]
        [Column("from_date")]
        public virtual DateOnly? from_date { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "date")]
        [Type(DatabaseType.SQLite, "text")]
        [Column("to_date")]
        public virtual DateOnly? to_date { get; set; }

    }
}