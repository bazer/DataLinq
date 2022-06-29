using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Definition("select `l`.`emp_no` AS `emp_no`,`d`.`dept_no` AS `dept_no`,`l`.`from_date` AS `from_date`,`l`.`to_date` AS `to_date` from (`employees`.`dept_emp` `d` join `employees`.`dept_emp_latest_date` `l` on(`d`.`emp_no` = `l`.`emp_no` and `d`.`from_date` = `l`.`from_date` and `l`.`to_date` = `d`.`to_date`))")]
    [Name("current_dept_emp")]
    public partial record current_dept_emp : IViewModel
    {
        [Type("char", 4)]
        public virtual string dept_no { get; set; }

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