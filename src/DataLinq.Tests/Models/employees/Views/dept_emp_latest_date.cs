using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Definition("select `dept_emp`.`emp_no` AS `emp_no`,max(`dept_emp`.`from_date`) AS `from_date`,max(`dept_emp`.`to_date`) AS `to_date` from `dept_emp` group by `dept_emp`.`emp_no`")]
    [Name("dept_emp_latest_date")]
    public partial record dept_emp_latest_date : IViewModel
    {
        [Type("int")]
        public virtual int emp_no { get; set; }

        [Nullable]
        [Type("date")]
        public virtual DateOnly? from_date { get; set; }

        [Nullable]
        [Type("date")]
        public virtual DateOnly? to_date { get; set; }

    }
}